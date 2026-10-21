// node Tests/car-driving-browser-check.mjs 9260 http://127.0.0.1:53727
import assert from 'node:assert/strict';
import fs from 'node:fs';

const port = process.argv[2] ?? '9260';
const base = process.argv[3] ?? 'http://127.0.0.1:53727';
const carDefinition = JSON.parse(fs.readFileSync('WolfNET.Game/wwwroot/objects/sprites/sprite-directional-car.json', 'utf8'));
const target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, {method: 'PUT'})).json();
const socket = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
let id = 0;
const pending = new Map(), errors = [], missing = [], requests = new Set();

function send(method, params = {}) {
    return new Promise((resolve, reject) => {
        const requestId = ++id;
        const timer = setTimeout(() => {
            pending.delete(requestId);
            reject(Error('CDP timeout: ' + method));
        }, 60000);
        pending.set(requestId, {resolve, reject, timer});
        socket.send(JSON.stringify({id: requestId, method, params}));
    });
}

socket.onmessage = event => {
    const message = JSON.parse(event.data);
    if (message.id) {
        const task = pending.get(message.id);
        if (!task) return;
        pending.delete(message.id);
        clearTimeout(task.timer);
        message.error ? task.reject(Error(JSON.stringify(message.error))) : task.resolve(message.result);
    } else if (message.method === 'Runtime.exceptionThrown') {
        errors.push(JSON.stringify(message.params));
    } else if (message.method === 'Runtime.consoleAPICalled') {
        const text = message.params.args.map(arg => arg.value ?? arg.description ?? '').join(' ');
        if (/Initialization failed|Frame failed|Uncaptured error|Could not load map/.test(text)) errors.push(text);
    } else if (message.method === 'Network.requestWillBeSent') {
        requests.add(new URL(message.params.request.url).pathname);
    } else if (message.method === 'Network.responseReceived' && message.params.response.status >= 400) {
        missing.push(message.params.response.url);
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

const specialKeys = {Space: [' ', 32], Escape: ['Escape', 27], AltLeft: ['Alt', 18]};
async function key(code, down = true) {
    const [value, virtual] = specialKeys[code] ?? [code.slice(3).toLowerCase(), code.charCodeAt(3)];
    await send('Input.dispatchKeyEvent', {
        type: down ? 'keyDown' : 'keyUp', code, key: value, windowsVirtualKeyCode: virtual,
    });
}
async function tap(code) { await key(code); await key(code, false); }
async function capture() {
    const point = await evaluate(`(() => {
        const r = document.querySelector('canvas').getBoundingClientRect();
        return {x: r.x + r.width / 2, y: r.y + r.height / 2};
    })()`);
    await send('Input.dispatchMouseEvent', {type: 'mousePressed', ...point, button: 'left', clickCount: 1});
    await send('Input.dispatchMouseEvent', {type: 'mouseReleased', ...point, button: 'left', clickCount: 1});
    await waitFor(`document.pointerLockElement === document.querySelector('canvas') && !document.querySelector('.car-paused')`);
}
async function camera() { return evaluate('window.__camera'); }
function distance(a, b) { return Math.hypot(a[0] - b[0], a[1] - b[1]); }
async function snapshot(name) {
    const capture = await send('Page.captureScreenshot', {format: 'png'});
    fs.writeFileSync(`artifacts/car-driving/${name}.png`, Buffer.from(capture.data, 'base64'));
}

try {
    await send('Page.enable');
    await send('Runtime.enable');
    await send('Network.enable');
    await send('Emulation.setDeviceMetricsOverride', {width: 1280, height: 850, deviceScaleFactor: 1, mobile: false});
    await send('Page.addScriptToEvaluateOnNewDocument', {source: `
        window.__camera = null; window.__avatar = null; window.__carLayers = {}; window.__mapInfo = null;
        const frames = ${JSON.stringify(carDefinition.atlas.frames)};
        const fetchOriginal = window.fetch;
        window.fetch = async function(input, ...args) {
            const response = await fetchOriginal.call(this, input, ...args);
            if (String(input?.url ?? input).includes('graphics-settings.json')) {
                const settings = await response.json();
                Object.assign(settings, {width: 640, height: 360, targetFPS: 30});
                return new Response(JSON.stringify(settings), {headers: {'Content-Type': 'application/json'}});
            }
            return response;
        };
        const bitmap = window.createImageBitmap;
        window.createImageBitmap = function(...args) {
            const result = bitmap.apply(this, args);
            if (args[0]?.src?.endsWith('/${carDefinition.texture}')) return result.then(value => {
                value.__carFrame = frames.findIndex(frame => frame.x === args[1] && frame.y === args[2] && frame.width === args[3] && frame.height === args[4]);
                return value;
            });
            return result;
        };
        const copy = GPUQueue.prototype.copyExternalImageToTexture;
        GPUQueue.prototype.copyExternalImageToTexture = function(source, destination, ...args) {
            if (source.source.__carFrame !== undefined) window.__carLayers[destination.origin.z] = source.source.__carFrame;
            return copy.call(this, source, destination, ...args);
        };
        const write = GPUQueue.prototype.writeBuffer;
        GPUQueue.prototype.writeBuffer = function(buffer, offset, data, ...args) {
            const raw = data.buffer ?? data, start = data.byteOffset ?? 0;
            if (buffer.label === 'WolfNET Camera') window.__camera = Array.from(new Float32Array(raw, start, 12));
            if (buffer.label === 'WolfNET Map Info' && offset === 0) window.__mapInfo = Array.from(new Uint32Array(raw, start, 4));
            if (buffer.label === 'WolfNET Sprites') {
                const ints = new Uint32Array(raw, start, data.byteLength / 4), floats = new Float32Array(raw, start, data.byteLength / 4);
                window.__avatar = null;
                for (let i = 0; i < ints.length; i += 8) if (ints[i + 3] === 0) {
                    window.__avatar = {x: floats[i], y: floats[i + 1], frame: window.__carLayers[ints[i + 2]], width: floats[i + 4], height: floats[i + 5], elevation: floats[i + 6]};
                }
            }
            return write.call(this, buffer, offset, data, ...args);
        };
    `});
    await send('Page.navigate', {url: base + '/?map=pine-loop'});
    await send('Page.bringToFront');
    await waitFor(`window.__camera && !document.querySelector('.loading-overlay') && document.querySelector('.car-cockpit') && document.querySelector('.car-paused')`);
    await waitFor(`Array.from(document.querySelectorAll('.car-cockpit img')).every(image => image.complete && image.naturalWidth > 0)`);
    fs.mkdirSync('artifacts/car-driving', {recursive: true});
    assert.equal(await evaluate('window.__avatar'), null, 'Race first person uploaded a world avatar');
    assert(await evaluate(`document.querySelector('.controls-hint').textContent.includes('W accelerate')`));
    assert(![...requests].some(url => /player-character|main-character-atlas/.test(url)), 'Race map loaded the human character');
    for (const asset of ['/textures/race/cockpit.png', '/textures/race/steering-wheel.png'])
        assert(requests.has(asset), 'Cockpit asset was never requested: ' + asset);

    const alpha = await evaluate(`Array.from(document.querySelectorAll('.car-cockpit img')).map(image => {
        const canvas = document.createElement('canvas'); canvas.width = image.naturalWidth; canvas.height = image.naturalHeight;
        const context = canvas.getContext('2d'); context.drawImage(image, 0, 0);
        const pixels = context.getImageData(0, 0, canvas.width, canvas.height).data;
        let transparent = 0, opaque = 0, count = 0;
        for (let i = 3; i < pixels.length; i += 32) { transparent += pixels[i] <= 2; opaque += pixels[i] >= 250; count++; }
        return {src: image.getAttribute('src'), transparent: transparent / count, opaque: opaque / count};
    })`);
    assert(alpha.every(image => image.transparent > .1 && image.opaque > .05), 'Cockpit or wheel lacks a transparent cutout');
    for (const [width, height] of [[1280, 850], [1920, 720], [390, 850]]) {
        await send('Emulation.setDeviceMetricsOverride', {width, height, deviceScaleFactor: 1, mobile: false});
        await delay(100);
        const layout = await evaluate(`(() => {
            const canvas = document.querySelector('canvas').getBoundingClientRect();
            const cockpit = document.querySelector('.car-cockpit'), bounds = cockpit.getBoundingClientRect();
            const wheel = document.querySelector('.car-cockpit-wheel').getBoundingClientRect();
            return {canvas: [canvas.x, canvas.y, canvas.width, canvas.height], cockpit: [bounds.x, bounds.y, bounds.width, bounds.height],
                pointer: getComputedStyle(cockpit).pointerEvents, overflow: getComputedStyle(cockpit).overflow,
                wheelCenter: [(wheel.x + wheel.width / 2 - canvas.x) / canvas.width, (wheel.y + wheel.height / 2 - canvas.y) / canvas.height]};
        })()`);
        assert.deepEqual(layout.cockpit, layout.canvas, 'Cockpit left the canvas bounds at ' + width + 'x' + height);
        assert.equal(layout.pointer, 'none');
        assert.equal(layout.overflow, 'hidden');
        assert(Math.abs(layout.wheelCenter[0] - .42) < .005 && Math.abs(layout.wheelCenter[1] - .86) < .005,
            'Wheel alignment changed at ' + width + 'x' + height);
    }
    await snapshot('narrow-paused');
    await send('Emulation.setDeviceMetricsOverride', {width: 1280, height: 850, deviceScaleFactor: 1, mobile: false});
    const parked = await camera();
    await key('KeyW'); await delay(250); await key('KeyW', false);
    assert.deepEqual(await camera(), parked, 'The race moved before mouse capture');
    await capture();

    await key('KeyA');
    await waitFor(`parseFloat(document.querySelector('.car-cockpit-wheel').style.getPropertyValue('--wheel-rotation')) < -100`);
    assert(distance(await camera(), parked) < .001, 'Steering at rest moved the car');
    assert(Math.abs((await camera())[2]) < .001, 'Steering at rest rotated the car');
    await snapshot('cockpit-steering');
    await key('KeyA', false);
    await waitFor(`Math.abs(parseFloat(document.querySelector('.car-cockpit-wheel').style.getPropertyValue('--wheel-rotation'))) < .01`);
    await key('Space'); await key('AltLeft'); await delay(250);
    assert.equal((await camera())[9], 0, 'Car Space or Alt changed camera height');
    await key('Space', false); await key('AltLeft', false);

    const heading = await evaluate(`(() => {const m = document.querySelector('.race-minimap-course').transform.baseVal.consolidate().matrix; return [m.b, m.d];})()`);
    await evaluate(`document.dispatchEvent(new MouseEvent('mousemove', {movementX: 100, movementY: -20, bubbles: true}))`);
    await waitFor('Math.abs(window.__camera[2]) > .1 && window.__camera[8] > .01');
    assert.deepEqual(await evaluate(`(() => {const m = document.querySelector('.race-minimap-course').transform.baseVal.consolidate().matrix; return [m.b, m.d];})()`), heading,
        'Mouse free look changed the car body heading');
    assert(distance(await camera(), parked) < .001, 'Mouse free look moved the car');
    await waitFor('Math.abs(window.__camera[2]) < .001 && Math.abs(window.__camera[8]) < .001');
    console.log('PASS cockpit: real transparent assets, responsive canvas alignment, steering wheel, ignored jump/crouch, and independent recentering free look.');

    // Compare equal throttle windows; the car must gain speed instead of moving at its top speed immediately.
    const start = await camera();
    await key('KeyW'); await delay(300); const early = await camera();
    await delay(300); const middle = await camera();
    await delay(300); const late = await camera();
    await key('KeyW', false);
    const firstTravel = distance(start, early), laterTravel = distance(middle, late);
    assert(firstTravel > .05 && laterTravel > firstTravel * 1.4, 'Throttle did not accelerate over time');
    await delay(400); const coast = await camera();
    assert(distance(late, coast) > .7, 'Car stopped instead of coasting after throttle release');
    assert(Math.abs(coast[0] - start[0]) < .01, 'Straight acceleration changed the body heading');
    await key('Space'); await delay(1100); const stopped = await camera();
    await delay(250);
    assert(distance(stopped, await camera()) < .002, 'Space did not bring the car to rest');
    assert.equal((await camera())[9], 0, 'Braking made the car jump');
    await key('Space', false);
    await key('KeyS'); await delay(650); await key('KeyS', false);
    const reversed = await camera();
    assert(reversed[1] > stopped[1] + .15 && distance(reversed, stopped) < 2, 'S at rest did not accelerate into a slower reverse');
    await key('Space'); await delay(650); await key('Space', false);
    console.log('PASS driving: gradual acceleration, continued coasting, braking without a jump, and accelerated reverse.');

    await tap('KeyV');
    await waitFor(`!document.querySelector('.car-cockpit') && window.__avatar?.frame === 4`);
    const avatar = await evaluate('window.__avatar');
    assert(Math.abs(avatar.width - .7) < .001 && Math.abs(avatar.height - .45) < .001, 'Chase view used the wrong car sprite dimensions');
    const chase = await camera();
    assert(Math.abs(Math.hypot(chase[0] - avatar.x, chase[1] - avatar.y) - 1.5) < .01, 'Chase view lost its follow distance');
    await snapshot('third-person');
    await key('KeyW'); await delay(800); await key('KeyW', false);
    const rolling = await camera(); await delay(250); const beforePause = await camera();
    const rollingSpeed = distance(rolling, beforePause) / .25;
    assert(rollingSpeed > 1, 'Pause test failed to establish rolling momentum');
    await tap('Escape');
    await waitFor(`!document.pointerLockElement && !!document.querySelector('.car-paused')`);
    const paused = await camera();
    await key('KeyW'); await key('KeyA'); await key('Space');
    await evaluate(`document.dispatchEvent(new MouseEvent('mousemove', {movementX: 200, movementY: 100, bubbles: true}))`);
    await delay(650);
    assert.deepEqual(await camera(), paused, 'Escape did not freeze the complete race camera pose');
    assert.equal(await evaluate('document.querySelectorAll(".car-cockpit").length'), 0, 'Paused chase view displayed the cockpit');
    await key('KeyW', false); await key('KeyA', false); await key('Space', false);
    await snapshot('third-person-paused');
    await capture();
    const resumed = await camera(); await delay(350); const afterResume = await camera();
    assert(distance(resumed, afterResume) > rollingSpeed * .15, 'Resuming lost the car momentum');
    assert(distance(resumed, afterResume) < rollingSpeed * .6 + .3, 'Paused time fast-forwarded the car on resume');
    assert(Math.abs(afterResume[2] - paused[2]) < .001 && Math.abs(afterResume[8] - paused[8]) < .001,
        'Inactive mouse or steering input leaked into the resumed race');
    await tap('KeyV');
    await waitFor(`!!document.querySelector('.car-cockpit') && window.__avatar === null`);
    assert.deepEqual(missing, [], 'Race map requested a missing asset');
    console.log('PASS camera and pause: eight-view chase car, V switching, Esc pose freeze, inactive-input rejection, and preserved momentum on recapture.');

    await evaluate('document.exitPointerLock()');
    await waitFor('!document.pointerLockElement');
    await evaluate(`Array.from(document.querySelectorAll('.map-switcher button')).find(button => button.textContent.trim() === 'Showcase').click()`);
    await waitFor(`window.__mapInfo?.[0] === 34 && !document.querySelector('.loading-overlay') && document.querySelector('.controls-hint').textContent.includes('Space jump')`);
    assert.equal(await evaluate('document.querySelectorAll(".car-cockpit, .car-paused, .race-minimap").length'), 0,
        'Race overlays survived leaving the race map');
    assert([...requests].some(url => /main-character-atlas/.test(url)), 'Exploration map did not restore the human character');
    await capture();
    await key('Space'); await waitFor('window.__camera[9] > .05'); await key('Space', false);
    await waitFor('window.__camera[9] === 0');
    assert.deepEqual(errors, []);
    // Showcase deliberately includes four missing textures to exercise the pink fallback.
    const showcaseFallbacks = new Set(['sprite', 'floor', 'ceiling', 'wall'].map(kind => `/textures/showcase-missing-${kind}.png`));
    assert.deepEqual(missing.filter(url => !showcaseFallbacks.has(new URL(url).pathname)), []);
    console.log('PASS scene isolation: leaving removes race overlays and controls, restores human textures, and restores jumping.');
} finally {
    await send('Page.close').catch(() => {});
    socket.close();
    for (const task of pending.values()) clearTimeout(task.timer);
}
