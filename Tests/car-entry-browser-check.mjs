// node Tests/car-entry-browser-check.mjs 9256 http://127.0.0.1:53720
import assert from 'node:assert/strict';
const port = process.argv[2] ?? '9256', base = process.argv[3] ?? 'http://127.0.0.1:53720';
const target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, {method: 'PUT'})).json();
const socket = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
let id = 0;
const pending = new Map(), errors = [];
function send(method, params = {}) {
    return new Promise((resolve, reject) => {
        const requestId = ++id;
        const timer = setTimeout(() => { pending.delete(requestId); reject(Error('CDP timeout: ' + method)); }, 60000);
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
        if (/Initialization failed|Frame failed|Uncaptured error|Could not load map/.test(line)) errors.push(line);
    }
};
async function evaluate(expression) {
    const result = await send('Runtime.evaluate', {expression, awaitPromise: true, returnByValue: true});
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
async function action() {
    await evaluate(`for(const type of ['keydown','keyup'])window.dispatchEvent(new KeyboardEvent(type,{code:'KeyF',bubbles:true,cancelable:true}))`);
}
async function capture() {
    const point = await evaluate(`(()=>{const r=document.querySelector('canvas').getBoundingClientRect();return{x:r.x+r.width/2,y:r.y+r.height/2};})()`);
    await send('Input.dispatchMouseEvent', {type: 'mousePressed', ...point, button: 'left', clickCount: 1});
    await send('Input.dispatchMouseEvent', {type: 'mouseReleased', ...point, button: 'left', clickCount: 1});
    await waitFor('document.pointerLockElement===document.querySelector("canvas")');
}
try {
    await send('Page.enable'); await send('Runtime.enable');
    await send('Emulation.setDeviceMetricsOverride', {width: 1280, height: 850, deviceScaleFactor: 1, mobile: false});
    await send('Page.addScriptToEvaluateOnNewDocument', {source: `
        window.__raceLoads=0;window.__info=null;window.__camera=null;window.__textures=new Set();
        const fetchOriginal=window.fetch;
        window.fetch=async function(input,...args){
            const response=await fetchOriginal.call(this,input,...args),url=String(input?.url??input);
            if(url.includes('graphics-settings.json')){const s=await response.json();s.width=640;s.height=360;s.targetFPS=15;return new Response(JSON.stringify(s),{headers:{'Content-Type':'application/json'}});}
            if(url.includes('maps/glassworks.json')||url.includes('maps/showcase.json')){
                const map=await response.json(),glass=url.includes('glassworks');
                Object.assign(map.playerStart,glass?{x:19.5,y:20.5,directionX:1,directionY:0}:{x:29.5,y:18.5,directionX:1,directionY:0});
                return new Response(JSON.stringify(map),{headers:{'Content-Type':'application/json'}});
            }
            if(url.includes('maps/pine-loop.json'))window.__raceLoads++;
            return response;
        };
        const create=GPUDevice.prototype.createTexture,destroy=GPUTexture.prototype.destroy;
        GPUDevice.prototype.createTexture=function(d){const t=create.call(this,d);if(['WolfNET Scene Textures','WolfNET Sky Panorama'].includes(d.label))window.__textures.add(t);return t;};
        GPUTexture.prototype.destroy=function(){window.__textures.delete(this);return destroy.call(this);};
        const write=GPUQueue.prototype.writeBuffer;
        GPUQueue.prototype.writeBuffer=function(buffer,offset,data,...args){
            if(buffer.label==='WolfNET Map Info'&&offset===0)window.__info=Array.from(new Uint32Array(data.buffer??data,data.byteOffset??0,4));
            if(buffer.label==='WolfNET Camera')window.__camera=Array.from(new Float32Array(data.buffer??data,data.byteOffset??0,12));
            return write.call(this,buffer,offset,data,...args);
        };
    `});
    await send('Page.navigate', {url: base + '/'}); await send('Page.bringToFront');
    await waitFor('window.__camera?.[0]===19.5&&!document.querySelector(".loading-overlay")');
    assert.equal(await evaluate('document.querySelectorAll(".race-minimap").length'), 0, 'Race minimap appeared on exploration map');
    assert.deepEqual(await evaluate(`Array.from(document.querySelectorAll('.map-switcher button')).map(button=>button.textContent.trim())`),
        ['The Glassworks', 'The Brickyard', 'Showcase']);
    assert(await evaluate(`document.querySelector('.controls-hint').textContent.includes('Press "F" to interact with door or car.')`));
    await action(); await delay(200);
    assert.equal(await evaluate('window.__raceLoads'), 0, 'F entered the race without mouse capture');
    for (const [index, source] of ['Glassworks', 'Showcase'].entries()) {
        if (index) {
            await evaluate(`Array.from(document.querySelectorAll('.map-switcher button')).find(button=>button.textContent.trim()==='Showcase').click()`);
            await waitFor('window.__info?.[0]===34&&window.__camera?.[0]===29.5&&!document.querySelector(".loading-overlay")');
            assert.equal(await evaluate('document.querySelectorAll(".race-minimap").length'), 0, 'Race minimap survived leaving the race map');
        }
        await capture(); await action();
        await waitFor(`window.__raceLoads===${index + 1}&&window.__info?.[0]===256&&window.__info?.[1]===512&&window.__camera?.[0]===25.5&&!document.querySelector('.loading-overlay')`);
        assert.equal(await evaluate('document.querySelector(".game-error")?.textContent??null'), null);
        assert.equal(await evaluate('document.querySelectorAll(".map-switcher button[aria-pressed=true]").length'), 0,
            'Source map remains selected after car transition');
        assert.equal(await evaluate('window.__textures.size'), 2, 'Car transition retained old scene textures');
        assert.equal(await evaluate('document.querySelectorAll(".race-minimap").length'), 1, 'Car transition lost or duplicated the race minimap');
        await delay(500);
        assert.equal(await evaluate('window.__raceLoads'), index + 1, 'One F press triggered repeated map loads');
        console.log(`PASS ${source} car: F loads Pine Loop, updates map UI, and releases source scene resources.`);
    }
    assert.deepEqual(errors, []);
    console.log('PASS: hidden race selector, controls hint, input capture, car entry from two maps, single loads, and scene cleanup.');
} finally {
    await send('Page.close').catch(() => {}); socket.close();
    for (const task of pending.values()) clearTimeout(task.timer);
}
