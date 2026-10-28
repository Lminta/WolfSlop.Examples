// Start the app and a Chromium browser with WebGPU and remote debugging enabled.
// node Tools/capture-map-screenshots.mjs 9260 http://127.0.0.1:5195 [map-id]
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

const port = process.argv[2] ?? '9260';
const base = process.argv[3] ?? 'http://127.0.0.1:5195';
const output = 'docs/screenshots';
const maps = [
    {id: 'showcase'},
    {id: 'glassworks', view: [12.5, 15.5, 0, -1]},
    {id: 'level1', view: [20.5, 22.5, 0, -1]},
    {id: 'pine-loop'},
].filter(map => !process.argv[4] || map.id === process.argv[4]);
assert(maps.length, 'Unknown map ID');
fs.mkdirSync(output, {recursive: true});

const target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, {method: 'PUT'})).json();
const socket = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
let sequence = 0;
const pending = new Map(), errors = [];
function send(method, params = {}) {
    return new Promise((resolve, reject) => {
        const id = ++sequence;
        const timer = setTimeout(() => { pending.delete(id); reject(Error(`Timed out: ${method}`)); }, 60000);
        pending.set(id, {resolve, reject, timer});
        socket.send(JSON.stringify({id, method, params}));
    });
}
socket.onmessage = event => {
    const message = JSON.parse(event.data);
    if (message.id) {
        const request = pending.get(message.id);
        if (!request) return;
        pending.delete(message.id);
        clearTimeout(request.timer);
        message.error ? request.reject(Error(JSON.stringify(message.error))) : request.resolve(message.result);
    } else if (message.method === 'Runtime.exceptionThrown') errors.push(JSON.stringify(message.params));
    else if (message.method === 'Runtime.consoleAPICalled') {
        const line = message.params.args.map(arg => arg.value ?? arg.description ?? '').join(' ');
        if (/Initialization failed|Frame failed|Uncaptured error/.test(line)) errors.push(line);
    }
};
async function evaluate(expression) {
    const response = await send('Runtime.evaluate', {expression, returnByValue: true, awaitPromise: true});
    if (response.exceptionDetails) throw Error(JSON.stringify(response.exceptionDetails));
    return response.result.value;
}
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
async function waitFor(expression) {
    const deadline = Date.now() + 60000;
    while (Date.now() < deadline) {
        assert.deepEqual(errors, []);
        const error = await evaluate('document.querySelector(".game-error")?.textContent');
        assert(!error, error);
        if (await evaluate(expression)) return;
        await delay(200);
    }
    throw Error(`Timed out waiting for ${expression}`);
}

try {
    await send('Runtime.enable');
    await send('Page.enable');
    await send('Emulation.setDeviceMetricsOverride', {width: 1280, height: 720, deviceScaleFactor: 1, mobile: false});
    // Choose a viewpoint without changing the shipped map files. Keep the world
    // and in-game HUD intact; hide only the page's help and navigation panels.
    await send('Page.addScriptToEvaluateOnNewDocument', {source: `
        window.__screenshotFrames = 0;
        const submit = GPUQueue.prototype.submit;
        GPUQueue.prototype.submit = function(...args) {
            const result = submit.apply(this, args);
            window.__screenshotFrames++;
            return result;
        };
        const views = ${JSON.stringify(Object.fromEntries(maps.filter(map => map.view).map(map => [map.id, map.view])))};
        const originalFetch = window.fetch;
        window.fetch = async function(input, ...args) {
            const response = await originalFetch.call(this, input, ...args);
            const url = new URL(input?.url ?? input, location.href);
            const map = url.pathname.match(/\\/maps\\/([^/]+)\\.json$/)?.[1];
            if (map && views[map]) {
                const definition = await response.json();
                const [x, y, directionX, directionY] = views[map];
                Object.assign(definition.spawn, {x, y, directionX, directionY});
                return new Response(JSON.stringify(definition), {headers: {'Content-Type': 'application/json'}});
            }
            return response;
        };
    `});
    for (const map of maps) {
        errors.length = 0;
        await send('Page.navigate', {url: `${base}/?map=${map.id}`});
        await send('Page.bringToFront');
        await waitFor('window.__screenshotFrames > 10 && !document.querySelector(".loading-overlay") && !!document.querySelector(".wolf-ui-canvas")');
        await evaluate(`(() => {
            const style = document.createElement('style');
            style.textContent = '.map-switcher, .controls-hint { display: none !important; }';
            document.head.append(style);
        })()`);
        await send('Input.dispatchMouseEvent', {type: 'mousePressed', x: 640, y: 360, button: 'left', clickCount: 1});
        await send('Input.dispatchMouseEvent', {type: 'mouseReleased', x: 640, y: 360, button: 'left', clickCount: 1});
        await waitFor('!!document.pointerLockElement');
        if (map.id === 'pine-loop') {
            await waitFor('document.querySelector(".wolf-ui-canvas").__uiFrame?.nodes.some(node => node.id === "countdown-value")');
            await waitFor('!document.querySelector(".wolf-ui-canvas").__uiFrame?.nodes.some(node => node.id === "countdown-value")');
        }
        await delay(500);
        assert.deepEqual(errors, []);
        const capture = await send('Page.captureScreenshot', {format: 'png'});
        const filename = path.join(output, `${map.id}.png`);
        fs.writeFileSync(filename, Buffer.from(capture.data, 'base64'));
        console.log(`Captured ${map.id}: ${filename}`);
    }
} finally {
    await send('Page.close').catch(() => {});
    socket.close();
    for (const request of pending.values()) clearTimeout(request.timer);
}
