// node Tests/framework-browser-check.mjs 9244 http://127.0.0.1:5194
import assert from 'node:assert/strict';
import fs from 'node:fs';
const port = process.argv[2] ?? '9244', base = process.argv[3] ?? 'http://127.0.0.1:5194';
const target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, {method: 'PUT'})).json();
const socket = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve, reject) => {socket.onopen = resolve; socket.onerror = reject;});
let sequence = 0;
const pending = new Map(), errors = [];
socket.onmessage = event => {
    const message = JSON.parse(event.data);
    if (message.id) {
        const request = pending.get(message.id); pending.delete(message.id);
        message.error ? request.reject(Error(JSON.stringify(message.error))) : request.resolve(message.result);
    } else if (message.method === 'Runtime.exceptionThrown') errors.push(JSON.stringify(message.params));
    else if (message.method === 'Runtime.consoleAPICalled') {
        const text = message.params.args.map(arg => arg.value ?? arg.description ?? '').join(' ');
        if (/Initialization failed|Frame failed|Uncaptured error/.test(text)) errors.push(text);
    }
};
function send(method, params = {}) {
    return new Promise((resolve, reject) => { const id = ++sequence; pending.set(id, {resolve, reject});
        socket.send(JSON.stringify({id, method, params})); });
}
async function evaluate(expression) {
    const result = await send('Runtime.evaluate', {expression, returnByValue: true, awaitPromise: true});
    if (result.exceptionDetails) throw Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
}
const delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
async function waitFor(expression) {
    for (let attempt = 0; attempt < 300; attempt++) {
        assert.deepEqual(errors, []);
        if (await evaluate(expression)) return;
        await delay(100);
    }
    throw Error('Timed out: ' + expression + '; ' + await evaluate('document.body.innerText'));
}
try {
    await send('Page.enable'); await send('Runtime.enable');
    await send('Page.addScriptToEvaluateOnNewDocument', {source: `
        window.__framework={textures:new Set(),buffers:new Set(),frames:0,overlays:0};
        const state=window.__framework,createTexture=GPUDevice.prototype.createTexture,destroyTexture=GPUTexture.prototype.destroy;
        GPUDevice.prototype.createTexture=function(desc){const value=createTexture.call(this,desc);state.textures.add(value);return value;};
        GPUTexture.prototype.destroy=function(){state.textures.delete(this);return destroyTexture.call(this);};
        const createBuffer=GPUDevice.prototype.createBuffer,destroyBuffer=GPUBuffer.prototype.destroy;
        GPUDevice.prototype.createBuffer=function(desc){const value=createBuffer.call(this,desc);state.buffers.add(value);return value;};
        GPUBuffer.prototype.destroy=function(){state.buffers.delete(this);return destroyBuffer.call(this);};
        const write=GPUQueue.prototype.writeBuffer;
        GPUQueue.prototype.writeBuffer=function(buffer,offset,data,...args){
            if(buffer.label==='WolfNET Camera'){state.camera=Array.from(new Float32Array(data.buffer??data,data.byteOffset??0,12));state.frames++;}
            if(buffer.label==='WolfNET Sprites')state.sprites=data.byteLength/32;
            return write.call(this,buffer,offset,data,...args);
        };
    `});
    await send('Page.navigate', {url: base + '/viewer'}); await send('Page.bringToFront');
    await waitFor(`window.__framework.frames>5 && document.querySelector('.wolf-ui-canvas')?.__uiFrame?.nodes.filter(node=>node.kind==='image').length===6`);
    const before = await evaluate('window.__framework.camera.slice(0,2)');
    await delay(200);
    assert.notDeepEqual(await evaluate('window.__framework.camera.slice(0,2)'), before);
    assert((await evaluate('window.__framework.sprites')) >= 12, 'Viewer did not upload its moving sprites');
    await evaluate(`document.querySelector('button').click()`);
    const paused = await evaluate('window.__framework.camera');
    await delay(200);
    assert.deepEqual(await evaluate('window.__framework.camera'), paused, 'Viewer pause advanced its camera');
    const snapshot = await send('Page.captureScreenshot', {format: 'png'});
    fs.mkdirSync('artifacts/framework', {recursive: true});
    fs.writeFileSync('artifacts/framework/viewer.png', Buffer.from(snapshot.data, 'base64'));
    const assets = await evaluate(`Promise.all(['sprite-sort.js','texture-upload.js','sfx-mixer.js','ui.js','shaders/raycast.wgsl','shaders/walls.wgsl','shaders/sprites.wgsl','shaders/overlays.wgsl'].map(async name=>{
        const response=await fetch('_content/WolfNET.Engine.Browser/'+name);return {name,status:response.status,type:response.headers.get('content-type')};
    }))`);
    assert(assets.every(asset => asset.status === 200 && !asset.type.includes('text/html')), JSON.stringify(assets));
    await evaluate(`document.querySelector('a').click()`);
    await waitFor(`!document.querySelector('.loading-overlay') && !!document.querySelector('.fps-counter')`);
    const firstGame = await evaluate('({textures:window.__framework.textures.size,buffers:window.__framework.buffers.size})');
    assert(await evaluate(`!!document.querySelector('.showcase-guide') && !document.querySelector('.race-timing')`),
        'Examples must open Showcase by default');
    await evaluate(`document.querySelector('a[href="viewer"]').click()`);
    await waitFor(`location.pathname==='/viewer' && document.querySelector('.wolf-ui-canvas')?.__uiFrame?.nodes.filter(node=>node.kind==='image').length===6`);
    const viewer = await evaluate('({textures:window.__framework.textures.size,buffers:window.__framework.buffers.size})');
    await evaluate(`document.querySelector('a').click()`);
    await waitFor(`!document.querySelector('.loading-overlay') && !!document.querySelector('.fps-counter')`);
    assert.deepEqual(await evaluate('({textures:window.__framework.textures.size,buffers:window.__framework.buffers.size})'),
        firstGame, 'Navigating between examples retained GPU resources');
    assert(viewer.textures > 0 && viewer.buffers > 0);
    assert.deepEqual(errors, []);
    console.log('PASS: viewer camera, pause, twelve moving sprites, six UI images, library static assets, navigation and resource cleanup.');
} finally {
    await send('Page.close').catch(() => {}); socket.close();
}
