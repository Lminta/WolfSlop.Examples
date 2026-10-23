// node Tests/car-driving-browser-check.mjs 9260 http://127.0.0.1:53727
import assert from 'node:assert/strict';
import fs from 'node:fs';

const port = process.argv[2] ?? '9260';
const base = process.argv[3] ?? 'http://127.0.0.1:53727';
const carDefinition = JSON.parse(fs.readFileSync('WolfNET.Game/wwwroot/objects/sprites/sprite-directional-car.json', 'utf8'));
const carSettings = JSON.parse(fs.readFileSync('WolfNET.Game/wwwroot/gameplay-settings.json', 'utf8')).car;
const target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, {method: 'PUT'})).json();
const socket = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
let id = 0;
const pending = new Map(), errors = [], missing = [], requests = new Set(), logs = [];

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
        logs.push(text);
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
        const error = await evaluate(`document.querySelector('.game-error')?.textContent`);
        if (error) throw Error(error);
        if (await evaluate(expression)) return;
        await delay(100);
    }
    fs.mkdirSync('artifacts/car-driving', {recursive: true});
    fs.writeFileSync('artifacts/car-driving/failure.json', JSON.stringify({expression, logs,
        state: await evaluate(`({text:document.body.innerText, locked:!!document.pointerLockElement,
            options:document.querySelector('.race-difficulty')?.outerHTML})`)}, null, 2));
    await snapshot('failure');
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
async function selectDifficulty(label) {
    await evaluate(`Array.from(document.querySelectorAll('.difficulty-options button')).find(button => button.textContent.trim() === ${JSON.stringify(label)}).click()`);
    await waitFor(`!document.querySelector('.loading-overlay') && !!document.querySelector('.car-paused') &&
        document.querySelector('.difficulty-options button[aria-pressed="true"]')?.textContent.trim() === ${JSON.stringify(label)}`);
    assert.equal(await evaluate(`document.querySelector('.race-total').textContent.trim()`), '0:00.00', 'Difficulty change did not reset the race');
}
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
        window.__camera = null; window.__avatar = null; window.__opponent = null; window.__greenOpponent = null; window.__carLayers = {}; window.__mapInfo = null;
        window.__lightLayers = [];
        const frames = ${JSON.stringify(carDefinition.atlas.frames)};
        const fetchOriginal = window.fetch;
        window.fetch = async function(input, ...args) {
            const response = await fetchOriginal.call(this, input, ...args);
            if (String(input?.url ?? input).includes('graphics-settings.json')) {
                const settings = await response.json();
                Object.assign(settings, {width: 640, height: 360, targetFPS: 30});
                return new Response(JSON.stringify(settings), {headers: {'Content-Type': 'application/json'}});
            }
            if (location.search.includes('short-race') && String(input?.url ?? input).includes('gameplay-settings.json')) {
                const settings = await response.json();
                settings.race = {lapCount: 2};
                return new Response(JSON.stringify(settings), {headers: {'Content-Type': 'application/json'}});
            }
            if (location.search.includes('short-race') && String(input?.url ?? input).includes('maps/pine-loop.json')) {
                const map = await response.json();
                const gate = y => ({center: [25.5, y], direction: [0, -1], halfWidth: 3.5});
                map.race = {startFinish: gate(444.8), checkpoints: [gate(444.4), gate(444)]};
                return new Response(JSON.stringify(map), {headers: {'Content-Type': 'application/json'}});
            }
            return response;
        };
        window.__viewTextures = new Set();
        const createViewTexture = GPUDevice.prototype.createTexture, destroyViewTexture = GPUTexture.prototype.destroy;
        GPUDevice.prototype.createTexture = function(descriptor) {
            const texture = createViewTexture.call(this, descriptor);
            if (descriptor.label?.startsWith('WolfNET First Person Texture:')) window.__viewTextures.add(texture);
            return texture;
        };
        GPUTexture.prototype.destroy = function() { window.__viewTextures.delete(this); return destroyViewTexture.call(this); };
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
            const unit = data.BYTES_PER_ELEMENT ?? 1, raw = data.buffer ?? data, start = (data.byteOffset ?? 0) + (args[0] ?? 0) * unit;
            const words = (args[1] ?? (data.byteLength / unit - (args[0] ?? 0))) * unit / 4;
            if (buffer.label === 'WolfNET Camera') window.__camera = Array.from(new Float32Array(raw, start, 12));
            if (buffer.label === 'WolfNET First Person') window.__firstPerson = Array.from(new Float32Array(raw, start, 12));
            if (buffer.label === 'WolfNET Map Info' && offset === 0) window.__mapInfo = Array.from(new Uint32Array(raw, start, 4));
            if (buffer.label === 'WolfNET Sprites') {
                const ints = new Uint32Array(raw, start, words), floats = new Float32Array(raw, start, words);
                window.__avatar = null;
                window.__opponent = null; window.__greenOpponent = null;
                window.__lightLayers = [];
                for (let i = 0; i < ints.length; i += 8) if (ints[i + 2] === 180 || ints[i + 2] === 181)
                    window.__lightLayers.push(ints[i + 2]);
                for (let i = 0; i < ints.length; i += 8) if (ints[i + 3] === 0) {
                    const actor = {x: floats[i], y: floats[i + 1], frame: window.__carLayers[ints[i + 2]], width: floats[i + 4], height: floats[i + 5], elevation: floats[i + 6], kind: ints[i + 7]};
                    if (ints[i + 7] & 32) window.__opponent = actor; else if (ints[i + 7] & 64) window.__greenOpponent = actor; else window.__avatar = actor;
                }
            }
            return write.call(this, buffer, offset, data, ...args);
        };
    `});
    await send('Page.navigate', {url: base + '/?map=pine-loop'});
    await send('Page.bringToFront');
    await waitFor(`window.__camera && !document.querySelector('.loading-overlay') && window.__firstPerson?.[2] === 2 && document.querySelector('.car-paused')`);
    assert.equal(await evaluate(`document.querySelectorAll('.car-cockpit, .player-hands').length`), 0, 'First-person images still use DOM overlays');
    assert.equal(await evaluate('window.__viewTextures.size'), 2, 'Car did not upload its two first-person textures');
    fs.mkdirSync('artifacts/car-driving', {recursive: true});
    assert.equal(await evaluate('window.__avatar'), null, 'Race first person uploaded a world avatar');
    await waitFor(`document.querySelector('.race-timing')`);
    assert.equal(await evaluate(`document.querySelector('.race-laps').textContent.trim()`), '0/3');
    assert.equal(await evaluate(`document.querySelector('.race-total').textContent.trim()`), '0:00.00');
    assert.equal(await evaluate(`document.querySelector('.difficulty-options button[aria-pressed="true"]').textContent.trim()`), 'Normal');
    assert(await evaluate(`(() => {
        const options = document.querySelector('.race-difficulty');
        const description = options.previousElementSibling;
        return description.tagName === 'P' && options.getBoundingClientRect().top >= description.getBoundingClientRect().bottom;
    })()`), 'Difficulty buttons are not beneath the map description');
    await selectDifficulty('Easy');
    await snapshot('difficulty-easy');
    await selectDifficulty('Normal');
    const gridOpponents = await evaluate(`Array.from(document.querySelectorAll('.race-minimap-opponents circle'), c => [c.getAttribute('cx'), c.getAttribute('cy')])`);
    console.log('PASS difficulty: normal default, buttons below map description, and switching either way resets the race.');
    assert(await evaluate(`window.__lightLayers.includes(180) && !window.__lightLayers.includes(181)`), 'Starting signal did not render red');
    assert(await evaluate(`document.querySelector('.controls-hint').textContent.includes('W accelerate')`));
    assert(![...requests].some(url => /player-character|main-character-atlas|first-person-hands/.test(url)), 'Race map loaded human textures');
    for (const asset of ['/textures/race/cockpit.png', '/textures/race/steering-wheel.png'])
        assert(requests.has(asset), 'Cockpit asset was never requested: ' + asset);

    const alpha = await evaluate(`Promise.all(['textures/race/cockpit.png', 'textures/race/steering-wheel.png'].map(async src => {
        const image = new Image(); image.src = src; await image.decode();
        const canvas = document.createElement('canvas'); canvas.width = image.naturalWidth; canvas.height = image.naturalHeight;
        const context = canvas.getContext('2d'); context.drawImage(image, 0, 0);
        const pixels = context.getImageData(0, 0, canvas.width, canvas.height).data;
        let transparent = 0, opaque = 0, count = 0;
        for (let i = 3; i < pixels.length; i += 32) { transparent += pixels[i] <= 2; opaque += pixels[i] >= 250; count++; }
        return {src: image.getAttribute('src'), transparent: transparent / count, opaque: opaque / count};
    }))`);
    assert(alpha.every(image => image.transparent > .1 && image.opaque > .05), 'Cockpit or wheel lacks a transparent cutout');
    for (const [width, height] of [[1280, 850], [1920, 720], [390, 850]]) {
        await send('Emulation.setDeviceMetricsOverride', {width, height, deviceScaleFactor: 1, mobile: false});
        await delay(100);
        assert.equal(await evaluate(`window.__firstPerson[2]`), 2, 'Resize hid the shader cockpit');
        const timing = await evaluate(`(() => {
            const map=document.querySelector('.race-minimap-viewport').getBoundingClientRect();
            const hud=document.querySelector('.race-timing').getBoundingClientRect();
            const style=getComputedStyle(document.querySelector('.race-timing'));
            return {below:hud.top-map.bottom,left:hud.left-map.left,right:hud.right,viewportWidth:innerWidth,
                fontSize:style.fontSize,color:style.color,background:style.backgroundColor,stroke:style.webkitTextStrokeWidth};
        })()`);
        assert(timing.below >= 7 && Math.abs(timing.left) < .1 && timing.right <= timing.viewportWidth,
            'Race timers do not align beneath the minimap at ' + width + 'x' + height);
        assert.equal(timing.fontSize, '44px');
        assert.equal(timing.color, 'rgb(255, 255, 255)');
        assert.equal(timing.background, 'rgba(0, 0, 0, 0)');
        assert.equal(timing.stroke, '3px');
    }
    await snapshot('narrow-paused');
    await send('Emulation.setDeviceMetricsOverride', {width: 1280, height: 850, deviceScaleFactor: 1, mobile: false});
    const parked = await camera();
    await key('KeyW'); await delay(250); await key('KeyW', false);
    assert.deepEqual(await camera(), parked, 'The race moved before mouse capture');
    await capture();

    await waitFor(`document.querySelector('.race-countdown-number')?.textContent.trim() === '5'`);
    await key('KeyW'); await key('KeyA'); await tap('KeyV');
    await delay(300);
    await waitFor(`window.__firstPerson?.[2] === 0 && !!window.__avatar`);
    const countdownCar = await evaluate('window.__avatar');
    assert(distance([countdownCar.x, countdownCar.y], parked) < .001, 'Countdown camera switch allowed driving');
    await tap('KeyV');
    await waitFor(`window.__firstPerson?.[2] === 2`);
    assert.deepEqual(await camera(), parked, 'Countdown camera switch changed the car pose');
    assert.equal(await evaluate(`document.querySelector('.race-total').textContent.trim()`), '0:00.00');
    await key('KeyW', false); await key('KeyA', false);
    await snapshot('countdown');
    await tap('Escape');
    await waitFor(`!document.pointerLockElement && !!document.querySelector('.car-paused')`);
    const countdown = await evaluate(`document.querySelector('.race-countdown-number').textContent`);
    await waitFor(`getComputedStyle(document.querySelector('.race-countdown-number')).animationPlayState === 'paused'`);
    await delay(1100);
    assert.equal(await evaluate(`document.querySelector('.race-countdown-number').textContent`), countdown, 'Paused countdown advanced');
    await capture();
    await waitFor(`document.querySelector('.race-go')?.textContent.trim() === 'GO'`);
    assert(await evaluate(`window.__lightLayers.includes(181) && !window.__lightLayers.includes(180)`), 'GO did not render green starting lights');
    assert.deepEqual(await camera(), parked, 'Car moved before GO');
    await snapshot('go');
    await waitFor(`!document.querySelector('.race-countdown')`);
    await waitFor(`!!window.__opponent`);
    assert.equal((await evaluate('window.__opponent')).kind & 32, 32, 'Opponent did not request red/blue channel swapping');
    await waitFor(`!!window.__greenOpponent`);
    assert.equal((await evaluate('window.__greenOpponent')).kind & 64, 64, 'Green rival did not request red/green channel swapping');
    await snapshot('opponents');
    const gapBefore = await evaluate(`document.querySelector('.race-gap').textContent.trim()`);
    await delay(250);
    assert.equal(gapBefore, '—', 'Gap appeared before the first completed lap');
    assert.equal(await evaluate(`document.querySelector('.race-gap').textContent.trim()`), gapBefore, 'Gap changed before either racer completed a lap');
    console.log('PASS start: click-to-start countdown, camera switching with driving locked, frozen timers, pause/resume, animated GO.');

    await key('KeyA');
    await waitFor(`window.__firstPerson[3] < -100 * Math.PI / 180`);
    assert(distance(await camera(), parked) < .001, 'Steering at rest moved the car');
    assert(Math.abs((await camera())[2]) < .001, 'Steering at rest rotated the car');
    await snapshot('cockpit-steering');
    await key('KeyA', false);
    await waitFor(`Math.abs(window.__firstPerson[3]) < .01 * Math.PI / 180`);
    await key('Space'); await key('AltLeft'); await delay(250);
    assert.equal((await camera())[9], 0, 'Car Space or Alt changed camera height');
    await key('Space', false); await key('AltLeft', false);

    const heading = await evaluate(`(() => {const m = document.querySelector('.race-minimap-course').transform.baseVal.consolidate().matrix; return [m.b, m.d];})()`);
    await evaluate(`document.dispatchEvent(new MouseEvent('mousemove', {movementX: 100, movementY: -20, bubbles: true}))`);
    await delay(250);
    assert.deepEqual(await camera(), parked, 'Cockpit mouse input changed the camera');
    assert.deepEqual(await evaluate(`(() => {const m = document.querySelector('.race-minimap-course').transform.baseVal.consolidate().matrix; return [m.b, m.d];})()`), heading,
        'Cockpit mouse input changed the car body heading');
    console.log('PASS cockpit: transparent assets, responsive canvas alignment, steering wheel, ignored jump/crouch, and locked mouse camera.');

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
    await waitFor(`document.querySelector('.race-total').textContent.trim() !== '0:00.00'`);
    assert.equal(await evaluate(`document.querySelector('.race-laps').textContent.trim()`), '0/3',
        'Initial start-line crossing counted a complete lap');
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
    await waitFor(`window.__firstPerson?.[2] === 0 && window.__avatar?.frame === 4`);
    const avatar = await evaluate('window.__avatar');
    assert(Math.abs(avatar.width - carSettings.width) < .001 && Math.abs(avatar.height - carSettings.height) < .001, 'Chase view used the wrong car sprite dimensions');
    assert.equal(avatar.kind & 24, 24, 'Car character is still a camera-facing billboard');
    const chase = await camera();
    assert(Math.abs(Math.hypot(chase[0] - avatar.x, chase[1] - avatar.y) - 1.5) < .01, 'Chase view lost its follow distance');
    await snapshot('third-person');
    await evaluate(`document.dispatchEvent(new MouseEvent('mousemove', {movementX: -40, movementY: -20, bubbles: true}))`);
    await waitFor('window.__camera[2] > .09 && window.__camera[8] > .01');
    const offsetAvatar = await evaluate('window.__avatar');
    assert.equal(offsetAvatar.frame, 4);
    assert(Math.abs(offsetAvatar.width - avatar.width) < .001 && offsetAvatar.kind === avatar.kind,
        'Small camera orbit stretched the car or rotated its fixed plane');
    await snapshot('third-person-small-orbit');
    await evaluate(`document.dispatchEvent(new MouseEvent('mousemove', {movementX: ${-(Math.PI - .1) / .0025}, movementY: 20, bubbles: true}))`);
    await waitFor('window.__avatar?.frame === 0 && window.__camera[3] > .99');
    assert.deepEqual(await evaluate(`(() => {const m = document.querySelector('.race-minimap-course').transform.baseVal.consolidate().matrix; return [m.b, m.d];})()`), heading,
        'Complete chase orbit steered the car');
    await snapshot('third-person-front');
    await evaluate(`document.dispatchEvent(new MouseEvent('mousemove', {movementX: ${-Math.PI / .0025}, movementY: 0, bubbles: true}))`);
    await waitFor('window.__avatar?.frame === 4 && window.__camera[3] < -.99');
    await waitFor('Math.abs(window.__camera[2]) < .001 && Math.abs(window.__camera[8]) < .001');
    console.log('PASS orbit: full revolution with backward view, independent body heading, fixed directional sprite planes, and delayed recentering.');
    await key('KeyW'); await delay(800); await key('KeyW', false);
    const rolling = await camera(); await delay(250); const beforePause = await camera();
    const rollingSpeed = distance(rolling, beforePause) / .25;
    assert(rollingSpeed > 1, 'Pause test failed to establish rolling momentum');
    await tap('Escape');
    await waitFor(`!document.pointerLockElement && !!document.querySelector('.car-paused')`);
    const paused = await camera();
    await delay(150); // Let the throttled HUD publish the final pre-pause time.
    const pausedRaceTime = await evaluate(`document.querySelector('.race-total').textContent`);
    const pausedGap = await evaluate(`document.querySelector('.race-gap').textContent`);
    await key('KeyW'); await key('KeyA'); await key('Space');
    await evaluate(`document.dispatchEvent(new MouseEvent('mousemove', {movementX: 200, movementY: 100, bubbles: true}))`);
    await delay(650);
    assert.deepEqual(await camera(), paused, 'Escape did not freeze the complete race camera pose');
    assert.equal(await evaluate(`document.querySelector('.race-total').textContent`), pausedRaceTime,
        'Race timer advanced while driving was paused');
    assert.equal(await evaluate(`document.querySelector('.race-gap').textContent`), pausedGap,
        'Opponent gap advanced while driving was paused');
    assert.equal(await evaluate('window.__firstPerson[2]'), 0, 'Paused chase view displayed the cockpit');
    await key('KeyW', false); await key('KeyA', false); await key('Space', false);
    await snapshot('third-person-paused');
    await capture();
    const resumed = await camera(); await delay(350); const afterResume = await camera();
    assert(distance(resumed, afterResume) > rollingSpeed * .15, 'Resuming lost the car momentum');
    assert(distance(resumed, afterResume) < rollingSpeed * .6 + .3, 'Paused time fast-forwarded the car on resume');
    await waitFor(`document.querySelector('.race-total').textContent !== ${JSON.stringify(pausedRaceTime)}`);
    assert(Math.abs(afterResume[2] - paused[2]) < .001 && Math.abs(afterResume[8] - paused[8]) < .001,
        'Inactive mouse or steering input leaked into the resumed race');
    await tap('KeyV');
    await waitFor(`window.__firstPerson?.[2] === 2 && window.__avatar === null`);
    assert.deepEqual(missing, [], 'Race map requested a missing asset');
    console.log('PASS camera and pause: eight-view chase car, V switching, Esc pose freeze, inactive-input rejection, and preserved momentum on recapture.');

    await evaluate('document.exitPointerLock()');
    await waitFor('!document.pointerLockElement');
    await evaluate(`document.querySelector('.race-restart').click()`);
    await waitFor(`!document.querySelector('.loading-overlay') && !!document.querySelector('.car-paused') &&
        document.querySelector('.race-total')?.textContent.trim() === '0:00.00'`);
    await waitFor(`window.__camera[0] === ${parked[0]} && window.__camera[1] === ${parked[1]}`);
    assert.deepEqual(await camera(), parked, 'Mid-race restart did not reset the player pose and camera');
    assert.equal(await evaluate(`document.querySelector('.race-laps').textContent.trim()`), '0/3');
    assert.deepEqual(await evaluate(`Array.from(document.querySelectorAll('.race-minimap-opponents circle'), c => [c.getAttribute('cx'), c.getAttribute('cy')])`),
        gridOpponents, 'Mid-race restart did not return both opponents to their grid positions');
    assert.equal(await evaluate(`document.querySelector('.difficulty-options button[aria-pressed="true"]').textContent.trim()`), 'Normal');
    await capture();
    await waitFor(`document.querySelector('.race-countdown-number')?.textContent.trim() === '5'`);
    await snapshot('mid-race-restart');
    console.log('PASS mid-race restart: player, opponents, laps, timers, and countdown reset with difficulty retained.');

    await evaluate('document.exitPointerLock()');
    await waitFor('!document.pointerLockElement');
    await evaluate(`Array.from(document.querySelectorAll('.map-switcher button')).find(button => button.textContent.trim() === 'Showcase').click()`);
    await waitFor(`!document.querySelector('.loading-overlay') && !document.querySelector('.race-timing')`);
    await waitFor(`window.__mapInfo?.[0] === 34 && !document.querySelector('.loading-overlay') && document.querySelector('.controls-hint').textContent.includes('Space jump')`);
    assert.equal(await evaluate('document.querySelectorAll(".car-cockpit, .car-paused, .race-minimap").length'), 0,
        'Race overlays survived leaving the race map');
    assert([...requests].some(url => /main-character-atlas/.test(url)), 'Exploration map did not restore the human character');
    await waitFor(`window.__firstPerson?.[2] === 1`);
    assert.equal(await evaluate('window.__viewTextures.size'), 1, 'Car first-person textures survived switching to the human scene');
    assert(requests.has('/textures/characters/first-person-hands.png'), 'Human hands were not uploaded on returning to exploration');
    await capture();
    await key('Space'); await waitFor('window.__camera[9] > .05'); await key('Space', false);
    await waitFor('window.__camera[9] === 0');
    assert.deepEqual(errors, []);
    // Showcase deliberately includes four missing textures to exercise the pink fallback.
    const showcaseFallbacks = new Set(['sprite', 'floor', 'ceiling', 'wall'].map(kind => `/textures/showcase-missing-${kind}.png`));
    assert.deepEqual(missing.filter(url => !showcaseFallbacks.has(new URL(url).pathname)), []);
    console.log('PASS scene isolation: leaving removes race overlays and controls, restores human textures, and restores jumping.');

    // Keep real map geometry, car physics, rendering and UI, but shorten the
    // invisible gate sequence and configure two laps to verify gameplay settings.
    await send('Page.navigate', {url: base + '/?map=pine-loop&short-race'});
    await waitFor(`!document.querySelector('.loading-overlay') && !!document.querySelector('.race-timing') && !!document.querySelector('.car-paused')`);
    await selectDifficulty('Easy');
    await capture();
    await waitFor(`!!document.querySelector('.race-go')`);
    await key('KeyW');
    await waitFor(`window.__camera[1] < 443.5`);
    await key('KeyW', false); await key('Space'); await delay(1000); await key('Space', false);
    for (let lap = 1; lap <= 2; lap++) {
        await key('KeyS'); await waitFor(`window.__camera[1] > 445.8`);
        await key('KeyS', false); await key('Space'); await delay(600); await key('Space', false);
        await key('KeyW');
        await waitFor(`document.querySelector('.race-laps').textContent.trim() === '${lap}/2'`);
        if (lap < 2) {
            await waitFor(`window.__camera[1] < 443.5`);
            await key('KeyW', false); await key('Space'); await delay(1000); await key('Space', false);
        }
    }
    await waitFor(`!!document.querySelector('.race-results') && !document.pointerLockElement`);
    assert.equal(await evaluate(`document.querySelectorAll('.race-standings tbody tr').length`), 3);
    assert.deepEqual(await evaluate(`Array.from(document.querySelectorAll('.race-standings tbody td:first-child'), c => c.textContent)`), ['1', '2', '3']);
    assert(await evaluate(`document.querySelector('.race-standings').textContent.includes('Green car')`));
    assert.equal(await evaluate(`document.querySelectorAll('.race-lap-results tbody td').length`), 6);
    assert(await evaluate(`!!document.querySelector('.race-gap-ahead')`), 'Winning player did not show zero gap to leader');
    assert.equal(await evaluate(`document.querySelectorAll('.race-lap-results tbody tr').length`), 2);
    assert(await evaluate(`document.activeElement === document.querySelector('.race-results button')`), 'Results did not focus restart');
    const resultPose = await camera();
    const resultTime = await evaluate(`document.querySelector('.race-total').textContent`);
    const opponentTime = await evaluate(`Array.from(document.querySelectorAll('.race-standings tbody tr')).find(row => row.textContent.includes('Blue car')).lastElementChild.textContent`);
    await delay(500); await key('KeyW', false);
    assert.deepEqual(await camera(), resultPose, 'Finished car kept moving');
    assert.equal(await evaluate(`document.querySelector('.race-total').textContent`), resultTime, 'Finished race time kept increasing');
    assert.notEqual(await evaluate(`Array.from(document.querySelectorAll('.race-standings tbody tr')).find(row => row.textContent.includes('Blue car')).lastElementChild.textContent`), opponentTime,
        'Opponent stopped racing when player results opened');
    await snapshot('results');
    await evaluate(`document.querySelector('.race-results button').click()`);
    await waitFor(`!document.querySelector('.loading-overlay') && !document.querySelector('.race-results') &&
        document.querySelector('.race-total')?.textContent.trim() === '0:00.00' && !!document.querySelector('.car-paused')`);
    assert.equal(await evaluate(`document.querySelector('.race-laps').textContent.trim()`), '0/2');
    assert.equal(await evaluate(`document.querySelector('.difficulty-options button[aria-pressed="true"]').textContent.trim()`), 'Easy',
        'Restart did not retain the selected difficulty');
    assert(await evaluate(`window.__lightLayers.includes(180)`), 'Restart did not reset red lights');
    await capture();
    await waitFor(`document.querySelector('.race-countdown-number')?.textContent.trim() === '5'`);
    assert.deepEqual(errors, []);
    console.log('PASS finish: configured lap limit and counter, frozen race and released mouse, individual results, keyboard focus and clean restart.');
} finally {
    await send('Page.close').catch(() => {});
    socket.close();
    for (const task of pending.values()) clearTimeout(task.timer);
}
