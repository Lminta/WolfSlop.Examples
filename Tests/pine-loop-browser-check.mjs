// Start the game and a Chromium browser with WebGPU and remote debugging first.
// node Tests/pine-loop-browser-check.mjs 9255 http://127.0.0.1:53719 start buffer-front buffer-side
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

const port = process.argv[2] ?? '9255';
const base = process.argv[3] ?? 'http://127.0.0.1:53719';
const requested = process.argv.slice(4).filter(name => name !== '--hd');
const hd = process.argv.includes('--hd');
const webRoot = path.resolve('WolfNET.Game/wwwroot');
const level = JSON.parse(fs.readFileSync(path.join(webRoot, 'maps/pine-loop.json')));
const graphicsSettings = JSON.parse(fs.readFileSync(path.join(webRoot, 'graphics-settings.json')));
const renderDistance = graphicsSettings.renderDistance ?? 60;
const fogStartDistance = graphicsSettings.fogStartDistance ?? 40;
const fixture = JSON.parse(fs.readFileSync('Tests/Fixtures/pine-loop-lap.json'));
const views = fixture.views.filter(([name]) => !requested.length || requested.includes(name));
assert(views.length > 0, 'No matching review viewpoints');
const objects = new Map(Object.entries(level.objects).map(([id, filename]) =>
    [Number(id), JSON.parse(fs.readFileSync(path.resolve(webRoot, 'maps', filename)))]));
const bufferDefinitions = [...objects.values()].filter(object => object.type === 'sprite' &&
    object.texture?.endsWith('/buffer-front.png'));
assert.equal(bufferDefinitions.length, 2);

const target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, {method: 'PUT'})).json();
const socket = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
let id = 0;
const pending = new Map(), errors = [], missing = [];
function send(method, params = {}) {
    return new Promise((resolve, reject) => {
        const requestId = ++id;
        const timer = setTimeout(() => { pending.delete(requestId); reject(Error(`CDP timeout: ${method}`)); }, 60000);
        pending.set(requestId, {resolve, reject, timer});
        socket.send(JSON.stringify({id: requestId, method, params}));
    });
}
socket.onmessage = event => {
    const message = JSON.parse(event.data);
    if (message.id) {
        const task = pending.get(message.id);
        if (!task) return;
        pending.delete(message.id); clearTimeout(task.timer);
        message.error ? task.reject(Error(JSON.stringify(message.error))) : task.resolve(message.result);
    } else if (message.method === 'Runtime.exceptionThrown') errors.push(JSON.stringify(message.params));
    else if (message.method === 'Runtime.consoleAPICalled') {
        const line = message.params.args.map(arg => arg.value ?? arg.description ?? '').join(' ');
        if (/Initialization failed|Frame failed|Uncaptured error/.test(line)) errors.push(line);
    } else if (message.method === 'Network.responseReceived' && message.params.response.status >= 400)
        missing.push(message.params.response.url);
};
async function evaluate(expression) {
    const result = await send('Runtime.evaluate', {expression, returnByValue: true, awaitPromise: true});
    if (result.exceptionDetails) throw Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
}
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
async function waitFor(expression) {
    for (let attempt = 0; attempt < 600; attempt++) {
        assert.deepEqual(errors, []);
        if (await evaluate(expression)) return;
        await delay(100);
    }
    throw Error('Timed out: ' + expression);
}
try {
    await send('Runtime.enable'); await send('Page.enable'); await send('Network.enable');
    await send('Emulation.setDeviceMetricsOverride', {width: 1280, height: 850, deviceScaleFactor: 1, mobile: false});
    await send('Page.addScriptToEvaluateOnNewDocument', {source: `
        window.__camera=null; window.__mapInfo=null; window.__buffers=[]; window.__sprites=[]; window.__frames=[]; window.__events={};
        const views=new Map(${JSON.stringify(fixture.views)}), originalFetch=window.fetch;
        window.fetch=async function(input,...args){
            const response=await originalFetch.call(this,input,...args), url=String(input?.url??input);
            if(url.includes('graphics-settings.json')){
                const settings=await response.json();settings.width=${hd ? 1920 : 960};settings.height=${hd ? 1080 : 540};settings.targetFPS=15;
                return new Response(JSON.stringify(settings),{headers:{'Content-Type':'application/json'}});
            }
            if(url.includes('maps/pine-loop.json')){
                const map=await response.json(),view=views.get(new URLSearchParams(location.search).get('reviewView'));
                if(view)Object.assign(map.playerStart,{x:view[0],y:view[1],directionX:view[2],directionY:view[3]});
                return new Response(JSON.stringify(map),{headers:{'Content-Type':'application/json'}});
            }
            return response;
        };
        const write=GPUQueue.prototype.writeBuffer;
        GPUQueue.prototype.writeBuffer=function(buffer,offset,data,...args){
            const raw=data.buffer??data,start=data.byteOffset??0;
            if(buffer.label==='WolfNET Camera'){
                window.__camera=Array.from(new Float32Array(raw,start,12));
                window.__frames.push(performance.now());if(window.__frames.length>20)window.__frames.shift();
            }
            if(buffer.label==='WolfNET Map Info'&&offset===0)window.__mapInfo=Array.from(new Uint32Array(raw,start,4));
            if(buffer.label==='WolfNET Map Info'&&offset===8&&window.__mapInfo)window.__mapInfo[2]=new Uint32Array(raw,start,1)[0];
            if(buffer.label==='WolfNET Sprites'){
                const u=new Uint32Array(raw,start,data.byteLength/4),f=new Float32Array(raw,start,data.byteLength/4);window.__buffers=[];window.__sprites=[];
                for(let i=0;i<u.length;i+=8){
                    const sprite={x:f[i],y:f[i+1],layer:u[i+2],sx:f[i+4],sy:f[i+5],z:f[i+6],kind:u[i+7]};
                    window.__sprites.push(sprite);
                    if(sprite.layer>=130&&sprite.layer<=140){
                        const key=sprite.x+','+sprite.y,event=window.__events[key]??={layers:[],geometry:JSON.stringify([sprite.x,sprite.y,sprite.sx,sprite.sy,sprite.z,sprite.kind]),stable:true};
                        if(!event.layers.includes(sprite.layer))event.layers.push(sprite.layer);
                        event.stable&&=event.geometry===JSON.stringify([sprite.x,sprite.y,sprite.sx,sprite.sy,sprite.z,sprite.kind]);
                    }
                    if(u[i+2]===110)window.__buffers.push(sprite);
                }
            }
            return write.call(this,buffer,offset,data,...args);
        };
    `});
    fs.mkdirSync('artifacts/pine-loop-grand', {recursive: true});
    for (const [name, view] of views) {
        await send('Page.navigate', {url: base + '/?map=pine-loop&reviewView=' + encodeURIComponent(name)});
        await send('Page.bringToFront');
        await waitFor('window.__camera && window.__mapInfo && !document.querySelector(".loading-overlay") && window.__frames.length >= 4');
        if (fixture.eventPlacements && (name === 'start' || name === 'spectator-plaza')) {
            await waitFor(`Object.values(window.__events).filter(event=>event.layers[0]>=130&&event.layers[0]<=135).length>=2&&Object.values(window.__events).filter(event=>event.layers[0]>=130&&event.layers[0]<=135).every(event=>event.layers.length===3)`);
            const events = await evaluate('Object.values(window.__events)');
            assert(events.some(event => event.layers.every(layer => layer >= 130 && layer <= 132)), 'No animated flag reached the GPU');
            assert(events.some(event => event.layers.every(layer => layer >= 133 && layer <= 135)), 'No animated crowd reached the GPU');
            assert(events.every(event => event.stable), 'Animation changed a prop position, scale, elevation, or plane');
            assert(events.filter(event => event.layers[0] >= 136).every(event => event.layers.length === 1), 'Static signal or billboard animated');
            console.log(`PASS ${name} animation: each nearby flag and crowd cycled through all three frames with stable geometry.`);
        }
        const state = await evaluate('({camera:window.__camera,map:window.__mapInfo,buffers:window.__buffers,sprites:window.__sprites,frames:window.__frames})');
        assert.deepEqual(state.map.slice(0, 2), [256, 512]);
        assert(state.map[2] < 4500, 'Unexpectedly large sprite population');
        assert.equal(state.map[2], state.sprites.length, 'Draw count includes culled sprites or spare capacity');
        assert.equal(state.camera[10], renderDistance, 'Configured render distance did not reach the GPU');
        assert(Math.abs(state.camera[11] - fogStartDistance) < .0001, 'Configured fog start did not reach the GPU');
        for (const sprite of state.sprites)
            assert(Math.hypot(sprite.x - state.camera[0], sprite.y - state.camera[1]) <= renderDistance + sprite.sx / 2 + .001,
                'Scenery outside the visible distance was uploaded');
        const length = Math.hypot(view[2], view[3]);
        const expectedCamera = [view[0], view[1], view[2] / length, view[3] / length];
        for (let i = 0; i < 4; i++) assert(Math.abs(state.camera[i] - expectedCamera[i]) < .0001, 'Camera did not use the real review spawn');
        for (const sprite of state.buffers) {
            const owner = objects.get(level.worldMap[Math.floor(sprite.x)][Math.floor(sprite.y)]);
            const member = owner.sprites.find(member => bufferDefinitions.some(definition => definition.id === member.spriteId));
            const definition = objects.get(member.spriteId);
            assert.equal(sprite.layer, definition.textureLayer, 'Buffer switched artwork');
            assert(Math.abs(sprite.sx - member.scale.x) < .0001 && Math.abs(sprite.sy - member.scale.y) < .0001,
                'Buffer fixed dimensions changed');
            assert.equal(sprite.kind, 8 | (definition.planeAxis === 'x' ? 1 : 2), 'Buffer plane rotates with the observer');
        }
        if (name === 'buffer-front' || name === 'buffer-side') {
            const [x, y] = fixture.bufferCoordinate;
            assert(state.buffers.some(sprite => sprite.x === x + .5 && sprite.y === y + .5 &&
                sprite.layer === 110), 'Dedicated buffer view was culled or switched artwork');
        }
        assert.deepEqual(errors, []); assert.deepEqual(missing, []);
        const capture = await send('Page.captureScreenshot', {format: 'png'});
        fs.writeFileSync(`artifacts/pine-loop-grand/${name}.png`, Buffer.from(capture.data, 'base64'));
        const intervals = state.frames.slice(1).map((time, i) => time - state.frames[i]).sort((a, b) => a - b);
        console.log(`PASS ${name}: ${state.map[0]}x${state.map[1]}, ${state.map[2]} sprites, median frame interval ${Math.round(intervals[Math.floor(intervals.length / 2)])} ms${hd ? ' at 1080p' : ''}.`);
    }
    console.log('PASS: Pine Loop scenes, actual camera spawns, fixed buffer planes, GPU frame geometry, and no missing assets or GPU errors.');
} finally {
    socket.close();
    for (const task of pending.values()) clearTimeout(task.timer);
}
