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
    const pipeline = (module, entryPoint) => device.createRenderPipelineAsync({layout: "auto",
        vertex: {module, entryPoint: "vertexMain"}, fragment: {module, entryPoint, targets: [{format: "rgba8unorm"}]}});
    const walls = await pipeline(modules.walls, "fragmentMain");
    const spritePipeline = await pipeline(modules.sprites, "fragmentMain");
    const minimap = await pipeline(modules.walls, "minimapMain");
    const buffer = (size, usage) => device.createBuffer({size, usage: usage | GPUBufferUsage.COPY_DST});
    const camera = buffer(32, GPUBufferUsage.UNIFORM);
    const info = buffer(16, GPUBufferUsage.UNIFORM);
    const cells = buffer(25 * 16, GPUBufferUsage.STORAGE);
    const materials = buffer(32, GPUBufferUsage.STORAGE);
    const hits = buffer(128 * 16, GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_SRC);
    const sprites = buffer(64, GPUBufferUsage.STORAGE);
    const textures = device.createTexture({size: [4, 4, 8], format: "rgba8unorm", usage: GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST});
    const colors = [[255,0,0,255], [0,255,0,255], [0,0,255,255], [255,255,0,255],
        [255,255,255,255], [0,255,255,255], [255,0,255,255], [255,0,0,255]];
    const pixels = new Uint8Array(4 * 4 * 8 * 4);
    for (let layer = 0; layer < 8; layer++) for (let y = 0; y < 4; y++) for (let x = 0; x < 4; x++) {
        const cutout = layer === 7 && (x === 0 || x === 3 || y === 0 || y === 3);
        pixels.set(cutout ? [0,0,0,255] : colors[layer], (layer * 16 + y * 4 + x) * 4);
    }
    device.queue.writeTexture({texture: textures}, pixels, {bytesPerRow: 16, rowsPerImage: 4}, [4,4,8]);
    const bind = (pipe, entries) => device.createBindGroup({layout: pipe.getBindGroupLayout(0), entries});
    const entry = (binding, value) => ({binding, resource: {buffer: value}});
    const textureEntry = {binding: 2, resource: textures.createView()};
    const computeGroup = bind(computePipeline, [entry(0,camera),entry(1,cells),entry(2,hits),entry(3,info)]);
    const wallGroup = bind(walls, [entry(0,hits),entry(1,camera),textureEntry,entry(3,cells),entry(4,info),entry(6,materials)]);
    const spriteGroup = bind(spritePipeline, [entry(0,hits),entry(1,camera),textureEntry,entry(3,sprites)]);
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
        const data = new Uint32Array(16);
        values.forEach(([x,y,layer,owner], i) => data.set([floatBits(x),floatBits(y),layer,owner], i * 4));
        device.queue.writeBuffer(sprites, 0, data);
    }
    async function draw(dx = -1, dy = 0, count = 0, overlay = false) {
        device.queue.writeBuffer(camera, 0, new Float32Array([2.5,2.5,dx,dy,dy*.66,-dx*.66,128,128]));
        device.queue.writeBuffer(info, 0, new Uint32Array([5,5,count,0]));
        device.queue.writeBuffer(cells, 0, map);
        const encoder = device.createCommandEncoder();
        const compute = encoder.beginComputePass();
        compute.setPipeline(computePipeline); compute.setBindGroup(0,computeGroup); compute.dispatchWorkgroups(2); compute.end();
        const pass = encoder.beginRenderPass({colorAttachments: [{view: target.createView(),clearValue:[0,0,0,1],loadOp:"clear",storeOp:"store"}]});
        pass.setPipeline(walls); pass.setBindGroup(0,wallGroup); pass.draw(3);
        if (count) { pass.setPipeline(spritePipeline); pass.setBindGroup(0,spriteGroup); pass.draw(6,count); }
        if (overlay) { pass.setPipeline(minimap); pass.setBindGroup(0,minimapGroup); pass.draw(3); }
        pass.end();
        encoder.copyTextureToBuffer({texture:target},{buffer:readback,bytesPerRow:512},[128,128]);
        encoder.copyBufferToBuffer(hits,0,hitReadback,0,128*16);
        device.queue.submit([encoder.finish()]);
        await Promise.all([readback.mapAsync(GPUMapMode.READ),hitReadback.mapAsync(GPUMapMode.READ)]);
        const data = new Uint8Array(readback.getMappedRange()).slice();
        const hit = new DataView(hitReadback.getMappedRange());
        const result = {pixel:(x,y)=>Array.from(data.slice(y*512+x*4,y*512+x*4+4)), depth:hit.getFloat32(64*16,true), cell:hit.getUint32(64*16+8,true)};
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
    assert(equal(result.pixel(110,64),colors[1]), "Black cutout hid group sprite behind it");
    setSprites([[3.5,2.5,1,14]]);
    assert(!equal((await draw(-1,0,1)).pixel(64,64),colors[1]), "Sprite behind camera remained visible");
    result = await draw(-1,0,0,true);
    assert(equal(result.pixel(36,36),[69,191,255,255]), "GPU minimap player marker missing");
    await device.queue.onSubmittedWorkDone();
    assert(errors.length === 0, "GPU validation errors: " + errors.join("; "));
    device.destroy();
    return {checks, passed:true};
}
