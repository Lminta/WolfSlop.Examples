// Run the game and Chromium with WebGPU/remote debugging first.
// node Tests/glassworks-browser-check.mjs 9252 http://127.0.0.1:5195
import assert from 'node:assert/strict';
import fs from 'node:fs';

const port = process.argv[2] ?? '9252';
const base = process.argv[3] ?? 'http://127.0.0.1:5195';
const level = JSON.parse(fs.readFileSync('WolfNET.Game/wwwroot/maps/glassworks.json'));
const definitions = new Map(Object.entries(level.objects).map(([id, name]) =>
    [Number(id), JSON.parse(fs.readFileSync(new URL(name, new URL('../WolfNET.Game/wwwroot/maps/glassworks.json', import.meta.url))))]));
const floorProps = [...definitions.values()].filter(d => d.type === 'spriteGroup')
    .flatMap(group => group.sprites.filter(s => [360,361,362].includes(s.spriteId))
        .map(member => ({member, sprite: definitions.get(member.spriteId)})));
const target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, {method: 'PUT'})).json();
const socket = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
let id = 0;
const pending = new Map(), errors = [], logs = [], failedRequests = [];
function send(method, params = {}) {
    return new Promise((resolve, reject) => {
        const requestId = ++id;
        pending.set(requestId, {resolve, reject});
        socket.send(JSON.stringify({id: requestId, method, params}));
    });
}
socket.onmessage = event => {
    const message = JSON.parse(event.data);
    if (message.id) {
        const task = pending.get(message.id);
        pending.delete(message.id);
        message.error ? task.reject(Error(JSON.stringify(message.error))) : task.resolve(message.result);
    } else if (message.method === 'Runtime.consoleAPICalled') {
        const line = message.params.args.map(a => a.value ?? a.description ?? '').join(' ');
        logs.push(line);
        if (/Initialization failed|Frame failed|Uncaptured error/.test(line)) errors.push(line);
    } else if (message.method === 'Runtime.exceptionThrown') errors.push(JSON.stringify(message.params));
    else if (message.method === 'Network.responseReceived' && message.params.response.status >= 400)
        failedRequests.push(message.params.response.url);
};
async function evaluate(expression) {
    const result = await send('Runtime.evaluate', {expression, returnByValue: true, awaitPromise: true});
    if (result.exceptionDetails) throw Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
}
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
async function waitFor(expression) {
    for (let attempt = 0; attempt < 400; attempt++) {
        assert.equal(errors.length, 0, errors.join('\n'));
        if (await evaluate(expression)) return;
        await delay(100);
    }
    throw Error(`Timed out: ${expression}\n${logs.slice(-15).join('\n')}`);
}
async function key(code, down = true) {
    await evaluate(`window.dispatchEvent(new KeyboardEvent('${down ? 'keydown' : 'keyup'}', {code:'${code}', bubbles:true}))`);
}
async function screenshot(name) {
    const result = await send('Page.captureScreenshot', {format: 'png'});
    fs.mkdirSync('artifacts/glassworks', {recursive: true});
    fs.writeFileSync(`artifacts/glassworks/${name}.png`, Buffer.from(result.data, 'base64'));
}
try {
    await send('Runtime.enable');
    await send('Page.enable');
    await send('Network.enable');
    await send('Emulation.setDeviceMetricsOverride', {width: 1280, height: 720, deviceScaleFactor: 1, mobile: false});
    await send('Page.addScriptToEvaluateOnNewDocument', {source: `
        window.__camera = null; window.__images = new Set(); window.__view = null; window.__directional = [];
        const originalFetch = window.fetch;
        window.fetch = async function(input, ...args) {
            const response = await originalFetch.call(this, input, ...args);
            if (String(input?.url ?? input).includes('graphics-settings.json')) {
                const settings = await response.json();
                settings.width = 960; settings.height = 540;
                return new Response(JSON.stringify(settings), {headers: {'Content-Type': 'application/json'}});
            }
            return response;
        };
        const bitmap = window.createImageBitmap;
        window.createImageBitmap = function(...args) {
            if (args[0]?.src) window.__images.add(args[0].src);
            return bitmap.apply(this, args);
        };
        const write = GPUQueue.prototype.writeBuffer;
        GPUQueue.prototype.writeBuffer = function(buffer, offset, data, ...args) {
            if(buffer.label === 'WolfNET Sprites') {
                const unit=data.BYTES_PER_ELEMENT??1,raw=data.buffer??data,start=(data.byteOffset??0)+(args[0]??0)*unit;
                const words=(args[1]??(data.byteLength/unit-(args[0]??0)))*unit/4,u=new Uint32Array(raw,start,words),f=new Float32Array(raw,start,words);
                window.__directional=[];
                for(let i=0;i<u.length;i+=8)if((u[i+2]>=32&&u[i+2]<40)||(u[i+2]>=48&&u[i+2]<56))
                    window.__directional.push({x:f[i],y:f[i+1],layer:u[i+2],kind:u[i+7]});
            }
            if (buffer.label === 'WolfNET Camera') {
                const values = new Float32Array(data.buffer ?? data, data.byteOffset ?? 0, 12);
                window.__camera = Array.from(values);
                if (window.__view) {
                    const v = new Float32Array(values);
                    const [x,y,dx,dy] = window.__view;
                    v[0]=x;v[1]=y;v[2]=dx;v[3]=dy;v[4]=dy;v[5]=-dx;
                    return write.call(this, buffer, offset, v, ...args);
                }
            }
            return write.call(this, buffer, offset, data, ...args);
        };
    `});
    await send('Page.navigate', {url: base + '/?map=glassworks'});
    await send('Page.bringToFront');
    await waitFor(`!!window.__camera && !document.querySelector('.loading-overlay')`);
    assert.deepEqual((await evaluate('window.__camera')).slice(0, 4), [12.5, 20.5, 0, -1]);
    assert.equal(await evaluate(`document.querySelector('button[aria-pressed="true"]').textContent`), 'The Glassworks');
    assert.equal(failedRequests.length, 0, 'Missing asset responses: ' + failedRequests.join(', '));
    const images = await evaluate('[...window.__images]');
    const directional=await evaluate('({sprites:window.__directional,camera:window.__camera})');
    assert(directional.sprites.length>0, 'No directional furnishings reached the renderer');
    for(const sprite of directional.sprites){
        const baseLayer=sprite.layer>=48?48:32;
        const angle=Math.atan2(directional.camera[1]-sprite.y,directional.camera[0]-sprite.x)-Math.PI/2;
        const view=(Math.floor(angle/(Math.PI*2)*8+.5)%8+8)%8;
        const normal=((90+view*45)%360)/360*65536;
        assert.equal(sprite.layer,baseLayer+view,'Uploaded texture does not match its view sector');
        assert.equal(sprite.kind>>>0,((normal<<16)|24)>>>0,'facingCamera=false did not upload a fixed directional plane');
    }
    for (const name of ['fern-grounded-v2.png', 'bench-directional-atlas-v2.png', 'pump-directional-atlas-v2.png', 'lantern-sprite.png', 'glass-roof.svg'])
        assert(images.some(url => url.endsWith(name)), `${name} was not uploaded to the GPU`);
    const floating = await evaluate(`(async()=>{
        const props=${JSON.stringify(floorProps)}, images=new Map(), bounds=new Map(), failures=[];
        for(const {member,sprite} of props){
            let image=images.get(sprite.texture);
            if(!image){image=new Image();image.src=sprite.texture;await image.decode();images.set(sprite.texture,image);}
            const frames=sprite.atlas?.frames??[{x:0,y:0,width:image.naturalWidth,height:image.naturalHeight}];
            for(let i=0;i<frames.length;i++){
                const f=frames[i], key=sprite.texture+':'+i;
                let bottom=bounds.get(key);
                if(bottom===undefined){
                    const canvas=new OffscreenCanvas(f.width,f.height),ctx=canvas.getContext('2d');
                    ctx.drawImage(image,f.x,f.y,f.width,f.height,0,0,f.width,f.height);
                    const pixels=ctx.getImageData(0,0,f.width,f.height).data;bottom=-1;
                    // Ground the largest connected silhouette, not detached generated specks.
                    const seen=new Uint8Array(f.width*f.height);
                    let largest=0,substantial=0,edge=0;
                    for(let seed=0;seed<seen.length;seed++){
                        if(seen[seed]||pixels[seed*4+3]<64)continue;
                        const queue=[seed];seen[seed]=1;let componentBottom=0;
                        for(let cursor=0;cursor<queue.length;cursor++){
                            const p=queue[cursor],x=p%f.width,y=Math.floor(p/f.width);
                            componentBottom=Math.max(componentBottom,y);
                            for(let oy=-1;oy<=1;oy++)for(let ox=-1;ox<=1;ox++){
                                const nx=x+ox,ny=y+oy,n=ny*f.width+nx;
                                if(nx>=0&&nx<f.width&&ny>=0&&ny<f.height&&!seen[n]&&pixels[n*4+3]>=64){seen[n]=1;queue.push(n);}
                            }
                        }
                        if(queue.length>=32)substantial++;
                        if(queue.length>largest){largest=queue.length;bottom=componentBottom;}
                    }
                    if(sprite.atlas?.directions===8){
                        for(let y=0;y<f.height;y++)for(let x=0;x<f.width;x++)
                            if((x===0||y===0||x===f.width-1||y===f.height-1)&&pixels[(y*f.width+x)*4+3]>=64)edge++;
                        if(substantial!==1||edge!==0)failures.push({sprite:sprite.id,frame:i,substantial,edge});
                    }
                    bounds.set(key,bottom);
                }
                const foot=.5+member.offset.z+member.scale.y*(.5-(bottom+1)/f.height);
                if(bottom<0||Math.abs(foot)>.003)failures.push({sprite:sprite.id,frame:i,foot});
            }
        }
        return failures;
    })()`);
    assert.deepEqual(floating, [], 'Visible sprite bases do not meet the floor');
    await screenshot('entrance');
    const rect = await evaluate(`(()=>{const r=document.querySelector('canvas').getBoundingClientRect();return{x:r.x+r.width/2,y:r.y+r.height/2}})()`);
    await send('Input.dispatchMouseEvent', {type: 'mousePressed', ...rect, button: 'left', clickCount: 1});
    await send('Input.dispatchMouseEvent', {type: 'mouseReleased', ...rect, button: 'left', clickCount: 1});
    await waitFor(`document.pointerLockElement === document.querySelector('canvas')`);
    await key('KeyW'); await waitFor('window.__camera[1] < 18.7'); await key('KeyW', false);
    await key('KeyF'); await key('KeyF', false); await delay(900);
    await key('KeyW'); await waitFor('window.__camera[1] < 16.6'); await key('KeyW', false);
    assert((await evaluate('window.__camera'))[1] < 17, 'Could not walk through the glasshouse entry');
    await evaluate('document.exitPointerLock()');
    // Fixed renderer viewpoints inspect the authored scene without changing its map or spawn.
    for (const [name, view] of [
        ['glasshouse', [12.5, 15.7, 0, -1]],
        ['potting-room', [3.5, 13.5, 0, -1]],
        ['pump-room', [12.5, 5.2, 0, -1]],
        ['herbarium', [21.5, 13, 0, -1]],
        ['pot-contact', [13.8, 13.3, -.492057, -.870562]],
        ['lantern-oblique', [8.3, 11.7, -.6, -.8]],
        ['lantern-reverse', [8.3, 9.3, -.6, .8]],
        ['lantern-east-wall', [16.7, 11.7, .6, -.8]],
        ['lantern-north-wall', [13, 2, -.8, -.6]],
        ['lantern-south-wall', [6.5, 20.5, -.707107, .707107]],
    ]) {
        await evaluate(`window.__view=${JSON.stringify(view)}`); await delay(300); await screenshot(name);
    }
    await evaluate('window.__view=null');
    const clickMap = async name => {
        await evaluate(`Array.from(document.querySelectorAll('.map-switcher button')).find(b=>b.textContent===${JSON.stringify(name)}).click()`);
        await waitFor(`!document.querySelector('.loading-overlay') && document.querySelector('button[aria-pressed="true"]').textContent===${JSON.stringify(name)}`);
    };
    await clickMap('The Brickyard');
    await clickMap('The Glassworks');
    await waitFor('window.__camera[0] === 12.5 && window.__camera[1] === 20.5');
    assert.equal(errors.length, 0, errors.join('\n'));
    assert.equal(failedRequests.length, 0, failedRequests.join('\n'));
    console.log('PASS: Glassworks startup, directional atlas uploads, connected-silhouette ground contact in every frame, entrance crossing, map round-trip, no missing assets or GPU errors. Eleven screenshots: artifacts/glassworks/.');
} finally {
    await evaluate('document.exitPointerLock?.()').catch(() => {});
    await send('Page.close').catch(() => {});
    socket.close();
}
