// Runs in the browser against the actual project shaders, with synthetic GPU fixtures.
export async function checkGpu(sources) {
    const adapter = await navigator.gpu.requestAdapter();
    const device = await adapter.requestDevice();
    const errors = [];
    device.addEventListener("uncapturederror", event => errors.push(event.error.message));
    let checks = 0;
    const assert = (condition, message) => { if (!condition) throw Error(message); checks++; };
    const modules = {};
    for (const [name, code] of Object.entries(sources)) {
        modules[name] = device.createShaderModule({code});
        const info = await modules[name].getCompilationInfo();
        assert(!info.messages.some(message => message.type === "error"), name + ": " + info.messages.map(m => m.message).join("; "));
    }
    const computePipeline = await device.createComputePipelineAsync({layout: "auto", compute: {module: modules.raycast, entryPoint: "main"}});
    const pipeline = (module, entryPoint, alpha = false) => device.createRenderPipelineAsync({layout: "auto",
        vertex: {module, entryPoint: "vertexMain"}, fragment: {module, entryPoint, targets: [{format: "rgba8unorm",
            ...(alpha ? {blend: {color: {srcFactor: "one", dstFactor: "one-minus-src-alpha"},
                alpha: {srcFactor: "one", dstFactor: "one-minus-src-alpha"}}} : {})}]}});
    const walls = await pipeline(modules.walls, "fragmentMain");
    const spritePipeline = await pipeline(modules.sprites, "fragmentMain", true);
    const minimap = await pipeline(modules.walls, "minimapMain");
    const buffer = (size, usage) => device.createBuffer({size, usage: usage | GPUBufferUsage.COPY_DST});
    const camera = buffer(32, GPUBufferUsage.UNIFORM);
    const info = buffer(16, GPUBufferUsage.UNIFORM);
    const cells = buffer(25 * 16, GPUBufferUsage.STORAGE);
    const materials = buffer(32, GPUBufferUsage.STORAGE);
    const hits = buffer(128 * 16, GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC);
    const sprites = buffer(128, GPUBufferUsage.STORAGE);
    const textures = device.createTexture({size: [4, 4, 8], format: "rgba8unorm", usage: GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST});
    const colors = [[255,0,0,255], [0,255,0,255], [0,0,255,255], [255,255,0,255],
        [255,255,255,255], [0,255,255,255], [255,0,255,255], [255,0,0,255]];
    const pixels = new Uint8Array(4 * 4 * 8 * 4);
    for (let layer = 0; layer < 8; layer++) for (let y = 0; y < 4; y++) for (let x = 0; x < 4; x++) {
        const cutout = layer === 7 && (x === 0 || x === 3 || y === 0 || y === 3);
        pixels.set(cutout ? [12,34,56,0] : colors[layer], (layer * 16 + y * 4 + x) * 4);
    }
    device.queue.writeTexture({texture: textures}, pixels, {bytesPerRow: 16, rowsPerImage: 4}, [4,4,8]);
    const bind = (pipe, entries) => device.createBindGroup({layout: pipe.getBindGroupLayout(0), entries});
    const entry = (binding, value) => ({binding, resource: {buffer: value}});
    const textureEntry = {binding: 2, resource: textures.createView()};
    // Keep geometry/color fixtures exact; the filtering regression below uses real mip samplers.
    const clampSampler = device.createSampler({addressModeU:"clamp-to-edge",addressModeV:"clamp-to-edge"});
    const repeatSampler = device.createSampler({addressModeU:"repeat",addressModeV:"repeat"});
    const computeGroup = bind(computePipeline, [entry(0,camera),entry(1,cells),entry(2,hits),entry(3,info)]);
    const wallGroup = bind(walls, [entry(0,hits),entry(1,camera),textureEntry,entry(3,cells),entry(4,info),entry(6,materials),
        {binding:7,resource:clampSampler},{binding:8,resource:repeatSampler}]);
    let activeWallGroup = wallGroup;
    const spriteGroup = bind(spritePipeline, [entry(0,hits),entry(1,camera),textureEntry,entry(3,sprites),{binding:4,resource:clampSampler}]);
    const minimapGroup = bind(minimap, [entry(0,hits),entry(1,camera),entry(3,cells),entry(4,info),entry(5,sprites)]);
    const target = device.createTexture({size: [128,128], format: "rgba8unorm", usage: GPUTextureUsage.RENDER_ATTACHMENT | GPUTextureUsage.COPY_SRC});
    const readback = device.createBuffer({size: 512 * 128, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ});
    const hitReadback = device.createBuffer({size: 128 * 16, usage: GPUBufferUsage.COPY_DST | GPUBufferUsage.MAP_READ});
    const map = new Uint32Array(25 * 4);
    function resetMap() {
        for (let y = 0; y < 5; y++) for (let x = 0; x < 5; x++) {
            const boundary = x === 0 || y === 0 || x === 4 || y === 4;
            map.set([boundary ? 1 : 0, 4, 5, boundary ? 3 : 0], (y * 5 + x) * 4);
        }
    }
    const floatBits = value => new Uint32Array(new Float32Array([value]).buffer)[0];
    function setSprites(values) {
        const data = new Uint32Array(32);
        values.forEach(([x,y,layer,owner,sx=1,sy=1,z=0], i) => data.set(
            [floatBits(x),floatBits(y),layer,owner,floatBits(sx),floatBits(sy),floatBits(z),0], i * 8));
        device.queue.writeBuffer(sprites, 0, data);
    }
    async function draw(dx = -1, dy = 0, count = 0, overlay = false, px = 2.5, py = 2.5, width = 128, height = 128) {
        device.queue.writeBuffer(camera, 0, new Float32Array([px,py,dx,dy,dy*.66,-dx*.66,width,height]));
        device.queue.writeBuffer(info, 0, new Uint32Array([5,5,count,0]));
        device.queue.writeBuffer(cells, 0, map);
        const encoder = device.createCommandEncoder();
        const compute = encoder.beginComputePass();
        compute.setPipeline(computePipeline); compute.setBindGroup(0,computeGroup); compute.dispatchWorkgroups(2); compute.end();
        const pass = encoder.beginRenderPass({colorAttachments: [{view: target.createView(),clearValue:[0,0,0,1],loadOp:"clear",storeOp:"store"}]});
        pass.setViewport(0,0,width,height,0,1);
        pass.setPipeline(walls); pass.setBindGroup(0,activeWallGroup); pass.draw(3);
        if (count) { pass.setPipeline(spritePipeline); pass.setBindGroup(0,spriteGroup); pass.draw(6,count); }
        if (overlay) { pass.setPipeline(minimap); pass.setBindGroup(0,minimapGroup); pass.draw(3); }
        pass.end();
        encoder.copyTextureToBuffer({texture:target},{buffer:readback,bytesPerRow:512},[128,128]);
        encoder.copyBufferToBuffer(hits,0,hitReadback,0,128*16);
        device.queue.submit([encoder.finish()]);
        await Promise.all([readback.mapAsync(GPUMapMode.READ),hitReadback.mapAsync(GPUMapMode.READ)]);
        const data = new Uint8Array(readback.getMappedRange()).slice();
        const hit = new DataView(hitReadback.getMappedRange());
        const center = Math.floor(width / 2) * 16;
        const result = {pixel:(x,y)=>Array.from(data.slice(y*512+x*4,y*512+x*4+4)), depth:hit.getFloat32(center,true), cell:hit.getUint32(center+8,true)};
        readback.unmap(); hitReadback.unmap(); return result;
    }
    const equal = (actual, expected) => actual.every((value,i)=>Math.abs(value-expected[i]) <= 1);
    const setMaterial = faces => device.queue.writeBuffer(materials,0,new Uint32Array([0xffffffff,0xffffffff,0xffffffff,0xffffffff,...faces]));
    resetMap(); setMaterial([0,1,2,3]);
    for (const [dx,dy,expected] of [[1,0,colors[3]],[-1,0,colors[1]],[0,1,[128,0,0,255]],[0,-1,[0,0,128,255]]]) {
        const result = await draw(dx,dy);
        assert(equal(result.pixel(64,64),expected), "Incorrect wall face for " + dx + "," + dy + ": " + result.pixel(64,64));
    }
    setMaterial([0,1,2,0xffffffff]);
    assert(equal((await draw(1,0)).pixel(64,64),[255,0,255,255]), "Missing wall side did not render pink");
    setMaterial([0,1,2,6]);
    assert(equal((await draw(1,0)).pixel(64,64),[255,0,255,255]), "Pink image fallback did not render");
    setMaterial([0,1,2,3]);
    map.set([1,4,5,3],(2*5+3)*4);
    let result = await draw(1,0);
    assert(result.cell === 14 && Math.abs(result.depth-.5)<.001, "Opaque wall did not stop DDA");
    map[13*4+3] = 2;
    result = await draw(1,0);
    assert(result.cell === 15 && Math.abs(result.depth-1.5)<.001, "Transparent wall stopped DDA");
    map.set([0,4,5,1],13*4);
    assert((await draw(1,0)).cell === 14, "Opaque non-wall did not stop DDA");
    resetMap();
    // Camera tile has a different floor; sampling must use the projected tile.
    map[12*4+1] = 6;
    result = await draw();
    assert(equal(result.pixel(64,120),colors[4]), "Floor sampled camera tile instead of projected tile");
    assert(equal(result.pixel(64,7),colors[5]), "Ceiling texture incorrect");
    map[11*4+1] = 0xffffffff;
    assert(equal((await draw()).pixel(64,120),[255,0,255,255]), "Missing floor did not render pink");
    map[11*4+2] = 0xffffffff;
    assert(equal((await draw()).pixel(64,7),[255,0,255,255]), "Missing ceiling did not render pink");
    resetMap(); map.set([0,4,5,1],11*4);
    setSprites([[1.5,2.5,1,12]]);
    assert(equal((await draw(-1,0,1)).pixel(64,64),colors[1]), "Opaque sprite hid itself");
    setSprites([[.5,2.5,1,11]]);
    assert(!equal((await draw(-1,0,1)).pixel(64,64),colors[1]), "Sprite behind blocker remained visible");
    setSprites([[1.2,2.5,1,12],[1.5,2.5,7,12]]);
    result = await draw(-1,0,2);
    assert(equal(result.pixel(64,64),colors[0]), "Near group sprite did not cover far sprite");
    assert(equal(result.pixel(96,64),colors[1]), "Alpha cutout hid group sprite behind it");
    const solid = new Uint8Array(4 * 4 * 4);
    for (let i = 0; i < solid.length; i += 4) solid.set([0,0,0,255], i);
    device.queue.writeTexture({texture: textures, origin: [0,0,7]}, solid, {bytesPerRow:16,rowsPerImage:4}, [4,4,1]);
    assert(equal((await draw(-1,0,2)).pixel(64,64),[0,0,0,255]), "Opaque black sprite became transparent");
    for (let i = 0; i < solid.length; i += 4) solid.set([128,0,0,128], i);
    device.queue.writeTexture({texture: textures, origin: [0,0,7]}, solid, {bytesPerRow:16,rowsPerImage:4}, [4,4,1]);
    assert(equal((await draw(-1,0,2)).pixel(64,64),[128,127,0,255]), "Partial alpha did not blend with the sprite behind it");
    for (const [width,height] of [[128,72],[128,128],[72,128]]) {
        resetMap(); setMaterial([0,1,2,3]);
        // Isolate one unit wall face with pink neighbors, viewed straight on.
        map[5*4] = 0; map[15*4] = 0;
        const wall = await draw(-1,0,0,false,2.5,2.5,width,height);
        const wallWidth = Array.from({length:width},(_,x)=>wall.pixel(x,height/2)).filter(pixel=>equal(pixel,colors[1])).length;
        const wallHeight = Array.from({length:height},(_,y)=>wall.pixel(width/2,y)).filter(pixel=>equal(pixel,colors[1])).length;
        assert(Math.abs(wallWidth-wallHeight)<=1, `Unit wall stretched at ${width}x${height}: ${wallWidth}x${wallHeight}`);
        const expectedSize = width / (2*.66*1.5);
        assert(Math.abs(wallHeight-expectedSize)<=1, "Wall projection did not preserve the horizontal field of view");
        // A unit billboard on that wall plane must occupy the same square.
        setSprites([[1,2.5,2,11]]);
        const sprite = await draw(-1,0,1,false,2.5,2.5,width,height);
        const spriteWidth = Array.from({length:width},(_,x)=>sprite.pixel(x,height/2)).filter(pixel=>equal(pixel,colors[2])).length;
        const spriteHeight = Array.from({length:height},(_,y)=>sprite.pixel(width/2,y)).filter(pixel=>equal(pixel,colors[2])).length;
        assert(Math.abs(spriteWidth-wallWidth)<=1 && Math.abs(spriteHeight-wallHeight)<=1, "Sprite and wall projection scales differ");
    }
    resetMap();
    function blueBounds(frame) {
        const points = [];
        for (let y=0;y<128;y++) for (let x=0;x<128;x++) if (equal(frame.pixel(x,y),colors[2])) points.push([x,y]);
        if (!points.length) return null;
        const xs=points.map(p=>p[0]), ys=points.map(p=>p[1]);
        return {width:Math.max(...xs)-Math.min(...xs)+1,height:Math.max(...ys)-Math.min(...ys)+1,
            centerY:(Math.max(...ys)+Math.min(...ys)+1)/2};
    }
    setSprites([[1,2.5,2,11,.5,.25,.25]]);
    const raised = blueBounds(await draw(-1,0,1));
    const unitSize = 128/(2*.66*1.5);
    assert(raised && Math.abs(raised.width-unitSize*.5)<=1 && Math.abs(raised.height-unitSize*.25)<=1,
        "Per-member scale did not independently change billboard width and height");
    assert(Math.abs(raised.centerY-(64-unitSize*.25))<=1, "Positive offset.z did not move the billboard up");
    setSprites([[1,2.5,2,11,.5,.25,-.25]]);
    const lowered = blueBounds(await draw(-1,0,1));
    assert(lowered && Math.abs(lowered.centerY-(64+unitSize*.25))<=1, "Negative offset.z did not move the billboard down");
    setSprites([[1,2.5,2,11,0,0,0]]);
    assert(blueBounds(await draw(-1,0,1))===null, "Zero-scale billboard still rendered");
    setSprites([[3.5,2.5,2,14]]);
    assert(!equal((await draw(-1,0,1)).pixel(64,64),colors[2]), "Sprite behind camera remained visible");
    result = await draw(-1,0,0,true);
    assert(equal(result.pixel(36,36),[69,191,255,255]), "GPU minimap player marker missing");
    // Facing north, A moves toward +world X: the minimap marker must move left.
    result = await draw(0,-1,0,true,3.25,2.5);
    assert(equal(result.pixel(31,36),[69,191,255,255]), "North-facing left strafe moved minimap marker right");
    assert(!equal(result.pixel(42,36),[69,191,255,255]), "Minimap player marker remained mirrored");
    result = await draw(0,-1,0,true,1.75,2.5);
    assert(equal(result.pixel(42,36),[69,191,255,255]), "North-facing right strafe moved minimap marker left");
    resetMap(); map.set([1,4,5,3],11*4);
    setSprites([[1.5,3.5,1,17]]);
    result = await draw(0,-1,1,true);
    assert(equal(result.pixel(44,36),[130,145,158,255]), "Minimap wall did not follow horizontal reflection");
    assert(equal(result.pixel(44,44),[227,173,97,255]), "Minimap sprite did not follow horizontal reflection");
    // A one-texel camera movement flips a distant, unfiltered checkerboard. Mips
    // must converge to its average instead, on walls and sloped floor/ceiling rays.
    const mipTexture = device.createTexture({size:[256,256,8],mipLevelCount:9,format:"rgba8unorm",
        usage:GPUTextureUsage.TEXTURE_BINDING|GPUTextureUsage.COPY_DST});
    for (let layer=0;layer<8;layer++) for (let mip=0;mip<9;mip++) {
        const size=256>>mip, data=new Uint8Array(size*size*4);
        for (let y=0;y<size;y++) for (let x=0;x<size;x++) {
            const value=mip===0?((x+y)%2)*255:128;
            data.set([value,value,value,255],(y*size+x)*4);
        }
        device.queue.writeTexture({texture:mipTexture,mipLevel:mip,origin:[0,0,layer]},data,
            {bytesPerRow:size*4,rowsPerImage:size},[size,size,1]);
    }
    const filteredClamp=device.createSampler({magFilter:"linear",minFilter:"linear",mipmapFilter:"linear",maxAnisotropy:8});
    const filteredRepeat=device.createSampler({magFilter:"linear",minFilter:"linear",mipmapFilter:"linear",maxAnisotropy:8,
        addressModeU:"repeat",addressModeV:"repeat"});
    const filteredGroup=(view,clamp,repeat)=>bind(walls,[entry(0,hits),entry(1,camera),{binding:2,resource:view},
        entry(3,cells),entry(4,info),entry(6,materials),{binding:7,resource:clamp},{binding:8,resource:repeat}]);
    resetMap(); setMaterial([0,1,2,3]);
    activeWallGroup=filteredGroup(mipTexture.createView({mipLevelCount:1}),clampSampler,repeatSampler);
    const noisyA=await draw(-1,0,0,false,3.5,2.5), noisyB=await draw(-1,0,0,false,3.5,2.5+1/256);
    const unfilteredChange=Math.abs(noisyA.pixel(64,64)[0]-noisyB.pixel(64,64)[0]);
    assert(unfilteredChange>200,"Checkerboard control did not expose minification aliasing");
    activeWallGroup=filteredGroup(mipTexture.createView(),filteredClamp,filteredRepeat);
    const smoothA=await draw(-1,0,0,false,3.5,2.5), smoothB=await draw(-1,0,0,false,3.5,2.5+1/256);
    assert(equal(smoothA.pixel(64,64),[128,128,128,255])&&equal(smoothB.pixel(64,64),[128,128,128,255]),
        "Wall mip filtering did not remove checkerboard shimmer");
    assert(equal(smoothA.pixel(64,118),[128,128,128,255]),"Floor gradients selected the wrong mip level");
    assert(equal(smoothA.pixel(64,5),[128,128,128,255]),"Ceiling gradients selected the wrong mip level");
    activeWallGroup=wallGroup;
    mipTexture.destroy();
    await device.queue.onSubmittedWorkDone();
    assert(errors.length === 0, "GPU validation errors: " + errors.join("; "));
    device.destroy();
    return {checks, passed:true, unfilteredCheckerboardChange:unfilteredChange, filteredCheckerboardChange:
        Math.abs(smoothA.pixel(64,64)[0]-smoothB.pixel(64,64)[0])};
}
