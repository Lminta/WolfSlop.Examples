// Start the game and Chromium with WebGPU and remote debugging enabled first.
// node Tests/scene-lifecycle-browser-check.mjs 9254 http://127.0.0.1:5195
import assert from 'node:assert/strict';

const port = process.argv[2] ?? '9254', base = process.argv[3] ?? 'http://127.0.0.1:5195';
const target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: 'PUT' })).json();
const socket = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
let id = 0;
const pending = new Map(), errors = [];
socket.onmessage = event => {
    const message = JSON.parse(event.data);
    if (message.id) {
        const request = pending.get(message.id);
        pending.delete(message.id);
        message.error ? request.reject(Error(JSON.stringify(message.error))) : request.resolve(message.result);
    } else if (message.method === 'Runtime.consoleAPICalled') {
        const line = message.params.args.map(arg => arg.value ?? arg.description ?? '').join(' ');
        if (/Frame failed|Uncaptured error|Scene update failed/.test(line)) errors.push(line);
    } else if (message.method === 'Runtime.exceptionThrown') errors.push(JSON.stringify(message.params));
};
function send(method, params = {}) {
    return new Promise((resolve, reject) => {
        const requestId = ++id;
        pending.set(requestId, { resolve, reject });
        socket.send(JSON.stringify({ id: requestId, method, params }));
    });
}
async function evaluate(expression) {
    const result = await send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (result.exceptionDetails) throw Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
}
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
async function waitFor(expression) {
    for (let attempt = 0; attempt < 600; attempt++) {
        assert.equal(errors.length, 0, errors.join('\n'));
        if (await evaluate(expression)) return;
        await delay(100);
    }
    throw Error('Timed out: ' + expression);
}

try {
    await send('Page.enable');
    await send('Runtime.enable');
    await send('Page.addScriptToEvaluateOnNewDocument', { source: `
        window.__textures = new Set(); window.__buffers = new Set(); window.__frames = 0;
        window.__failSky = true; window.__holdBitmap = false;
        const imageDecode = HTMLImageElement.prototype.decode;
        HTMLImageElement.prototype.decode = function(...args) {
            if (window.__failSky && this.src.endsWith('/textures/skyboxes/skybox.jpg'))
                return Promise.reject(Error('Fixture: sky unavailable'));
            return imageDecode.apply(this, args);
        };
        const texture = GPUDevice.prototype.createTexture, buffer = GPUDevice.prototype.createBuffer;
        GPUDevice.prototype.createTexture = function(descriptor) {
            const result = texture.call(this, descriptor);
            if (descriptor.label === 'WolfNET Scene Textures' || descriptor.label === 'WolfNET Sky Panorama')
                window.__textures.add(result);
            return result;
        };
        GPUDevice.prototype.createBuffer = function(descriptor) {
            const result = buffer.call(this, descriptor);
            if (['WolfNET Sky Settings', 'WolfNET Wall Materials', 'WolfNET Sprites'].includes(descriptor.label))
                window.__buffers.add(result);
            return result;
        };
        const destroyTexture = GPUTexture.prototype.destroy, destroyBuffer = GPUBuffer.prototype.destroy;
        GPUTexture.prototype.destroy = function() { window.__textures.delete(this); return destroyTexture.call(this); };
        GPUBuffer.prototype.destroy = function() { window.__buffers.delete(this); return destroyBuffer.call(this); };
        const write = GPUQueue.prototype.writeBuffer;
        GPUQueue.prototype.writeBuffer = function(buffer, ...args) {
            if (buffer.label === 'WolfNET Camera') window.__frames++;
            return write.call(this, buffer, ...args);
        };
        const bitmap = window.createImageBitmap;
        window.createImageBitmap = async function(...args) {
            const result = await bitmap.apply(this, args);
            if (window.__holdBitmap) {
                window.__holdBitmap = false;
                await new Promise(resolve => window.__releaseBitmap = resolve);
            }
            return result;
        };
    ` });
    await send('Page.navigate', { url: base + '/?map=showcase' });
    await send('Page.bringToFront');
    await waitFor('!!document.querySelector(".game-error")');
    assert(await evaluate('window.__buffers.size === 0 && window.__textures.size === 0 && window.__frames === 0'),
        'Failed scene preparation retained GPU resources or started simulation');

    // A preparation failure must leave the renderer usable for another scene.
    await evaluate(`window.__failSky = false;
        Array.from(document.querySelectorAll('.map-switcher button')).find(b => b.textContent === 'Original').click()`);
    await waitFor('window.__frames > 0 && !document.querySelector(".loading-overlay")');
    assert.equal(await evaluate('document.querySelector(".game-error")'), null);

    // Dispose Home while image upload is awaiting an asynchronous browser operation.
    await evaluate(`window.__holdBitmap = true;
        Array.from(document.querySelectorAll('.map-switcher button')).find(b => b.textContent === 'Showcase').click()`);
    await waitFor('typeof window.__releaseBitmap === "function"');
    const before = await evaluate('window.__frames');
    await evaluate(`const link = document.createElement('a'); link.href = '/not-found';
        document.body.appendChild(link); link.click()`);
    await waitFor('!document.querySelector("canvas")');
    await evaluate('window.__releaseBitmap()');
    await waitFor('window.__textures.size === 0 && window.__buffers.size === 0');
    await delay(200);
    assert.equal(await evaluate('window.__frames'), before, 'Canceled scene restarted frame submission');
    assert.equal(errors.length, 0, errors.join('\n'));
    console.log('PASS: failed GPU preparation cleans up and recovers; navigation cancels partial uploads without restarting frames.');
} finally {
    await send('Page.close').catch(() => {});
    socket.close();
}
