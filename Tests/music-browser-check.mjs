// Run against a WebGPU-capable Chromium instance with remote debugging enabled:
// node Tests/music-browser-check.mjs 9387 http://127.0.0.1:5025
import assert from 'node:assert/strict';
import fs from 'node:fs';

const musicDefinition = JSON.parse(fs.readFileSync('WolfNET.Game/wwwroot/maps/pine-loop.json', 'utf8')).music;
const playlist = musicDefinition.tracks;
const soundSettings = JSON.parse(fs.readFileSync('WolfNET.Game/wwwroot/audio-settings.json', 'utf8'));
const musicVolume = soundSettings.volume * (musicDefinition.volume ?? soundSettings.musicVolume);

const port = process.argv[2] ?? '9387';
const base = process.argv[3] ?? 'http://127.0.0.1:5025';
const target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, {method: 'PUT'})).json();
const socket = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
let id = 0;
const pending = new Map(), errors = [], responses = [];
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
        if (/Initialization failed|Frame failed|Music unavailable|\[AUDIO\]/.test(text)) errors.push(text);
    } else if (message.method === 'Network.responseReceived' && /\.mp3(?:\?|$)/.test(message.params.response.url)) {
        responses.push(message.params.response);
    }
};
function send(method, params = {}) {
    return new Promise((resolve, reject) => {
        const requestId = ++id;
        const timer = setTimeout(() => reject(Error('CDP timeout: ' + method)), 30000);
        pending.set(requestId, {resolve, reject, timer});
        socket.send(JSON.stringify({id: requestId, method, params}));
    });
}
async function evaluate(expression) {
    const result = await send('Runtime.evaluate', {expression, awaitPromise: true, returnByValue: true});
    if (result.exceptionDetails) throw Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
}
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
async function waitFor(expression, timeout = 45000) {
    const end = Date.now() + timeout;
    while (Date.now() < end) {
        assert.deepEqual(errors, []);
        if (await evaluate(expression)) return;
        await delay(100);
    }
    throw Error('Timed out: ' + expression + '\n' + JSON.stringify(await evaluate(`({
        focus: document.hasFocus(), lock: !!document.pointerLockElement,
        hint: document.querySelector('.controls-hint')?.textContent,
        countdown: document.querySelector('.race-countdown')?.textContent,
        music: window.__music && {playing: __music.playing, state: __music.context.state}
    })`)));
}
async function clickCanvas() {
    const point = await evaluate(`(() => {
        const r = document.querySelector('canvas').getBoundingClientRect();
        return {x: r.x + r.width / 2, y: r.y + r.height / 2};
    })()`);
    await send('Input.dispatchMouseEvent', {type: 'mousePressed', ...point, button: 'left', clickCount: 1});
    await send('Input.dispatchMouseEvent', {type: 'mouseReleased', ...point, button: 'left', clickCount: 1});
}

try {
    await send('Page.enable');
    await send('Runtime.enable');
    await send('Network.enable');
    await send('Emulation.setFocusEmulationEnabled', {enabled: true});
    await send('Page.bringToFront');
    // Observe browser primitives used by the C# backend. No application JS module
    // or test hooks are needed, and playback/autoplay behavior is unchanged.
    await send('Page.addScriptToEvaluateOnNewDocument', {source: `
        const contexts = [];
        window.AudioContext = new Proxy(window.AudioContext, {
            construct(target, args) {
                const context = Reflect.construct(target, args);
                const streams = [], gains = [];
                const createGain = context.createGain.bind(context);
                context.createGain = () => {
                    const gain = createGain();
                    gains.push(gain);
                    return gain;
                };
                const createSource = context.createMediaElementSource.bind(context);
                context.createMediaElementSource = audio => {
                    const source = createSource(audio);
                    const stream = {audio, source};
                    streams.push(stream);
                    const connect = source.connect.bind(source);
                    source.connect = destination => { stream.gain = destination; return connect(destination); };
                    return source;
                };
                const active = () => streams.filter(stream => stream.audio.getAttribute('src'));
                contexts.push({context,
                    get streamCount() { return streams.length; },
                    get current() { return active()[0] ?? null; },
                    get next() { return active()[1] ?? null; },
                    get output() { return gains[0]; },
                    get playing() { return gains[0]?.gain.value > 0; },
                    get fade() { return active().length === 2 && active().every(stream => !stream.audio.paused); },
                    get disposed() { return context.state === 'closed'; }
                });
                return context;
            }
        });
        Object.defineProperty(window, '__music', {get: () => contexts.findLast(context => context.streamCount > 0)});
    `});
    await send('Page.navigate', {url: base + '/?map=pine-loop'});
    await waitFor(`window.__music && !document.querySelector('.loading-overlay')`);
    await waitFor(`Number.isFinite(__music.current.audio.duration) && Number.isFinite(__music.next.audio.duration)`);
    assert(await evaluate(`!__music.playing && __music.current.audio.paused && __music.current.audio.currentTime === 0`));
    await clickCanvas();
    await waitFor(`document.pointerLockElement && document.querySelector('.race-countdown-number')`);
    await waitFor(`__music.playing && !__music.current.audio.paused && __music.current.audio.currentTime > 0.1`);
    const quietVolume = await evaluate(`__music.output.gain.value`);
    assert(quietVolume > 0 && quietVolume < musicVolume * .4);
    await delay(800);
    const louderVolume = await evaluate(`__music.output.gain.value`);
    assert(louderVolume > quietVolume && louderVolume < musicVolume);
    // Pause the countdown itself: music time and ramp progress must both freeze.
    await evaluate(`document.exitPointerLock()`);
    await waitFor(`!__music.playing && __music.current.audio.paused`);
    const countdownPosition = await evaluate(`__music.current.audio.currentTime`);
    const countdownNumber = await evaluate(`document.querySelector('.race-countdown-number').textContent`);
    await delay(400);
    assert(Math.abs(await evaluate(`__music.current.audio.currentTime`) - countdownPosition) < 0.02);
    assert.equal(await evaluate(`document.querySelector('.race-countdown-number').textContent`), countdownNumber);
    await clickCanvas();
    await waitFor(`__music.playing && !__music.current.audio.paused`);
    assert(Math.abs(await evaluate(`__music.output.gain.value`) - louderVolume) < musicVolume * .1);
    await waitFor(`document.querySelector('.race-go')`, 15000);
    assert(Math.abs(await evaluate(`__music.output.gain.value`) - musicVolume) < .001);
    assert(await evaluate(`__music.context.state === 'running'`));

    await evaluate(`document.exitPointerLock()`);
    await waitFor(`!__music.playing && __music.current.audio.paused`);
    const pausedAt = await evaluate(`__music.current.audio.currentTime`);
    await delay(400);
    assert(Math.abs(await evaluate(`__music.current.audio.currentTime`) - pausedAt) < 0.02);
    await clickCanvas();
    await waitFor(`__music.playing && !__music.current.audio.paused`);
    assert(await evaluate(`__music.current.audio.currentTime`) >= pausedAt);

    // Seek near each real MP3's end to exercise all transitions without waiting
    // for complete songs. Keep actual decoding, streaming, and gain mixing.
    const order = [];
    for (let track = 0; track < playlist.length; track++) {
        await waitFor(`__music.next.audio.readyState >= 3`);
        order.push(await evaluate(`decodeURIComponent(__music.current.audio.src.split('/').pop())`));
        const outgoing = await evaluate(`__music.current.audio.src`);
        await evaluate(`__music.current.audio.currentTime = __music.current.audio.duration - 2.8`);
        await waitFor(`__music.fade && !__music.current.audio.paused && !__music.next.audio.paused`);
        await delay(600);
        assert(await evaluate(`__music.current.gain.gain.value > 0 && __music.current.gain.gain.value < 1 &&
            __music.next.gain.gain.value > 0 && __music.next.gain.gain.value < 1`));
        await waitFor(`__music.current.audio.src !== ${JSON.stringify(outgoing)}`);
    }
    assert.deepEqual(order, playlist.map(path => path.split('/').pop()));
    assert.equal(await evaluate(`decodeURIComponent(__music.current.audio.src.split('/').pop())`), order[0]);
    assert(responses.some(response => response.status === 206), 'Expected streamed byte-range MP3 responses');

    await evaluate(`document.exitPointerLock(); window.__oldMusic = __music;
        [...document.querySelectorAll('.map-switcher button')].find(button => button.textContent.includes('Showcase')).click()`);
    await waitFor(`__oldMusic.disposed && !document.querySelector('.loading-overlay')`);
    assert(await evaluate(`__oldMusic.current === null && __oldMusic.next === null && __oldMusic.context.state === 'closed'`));
    assert.deepEqual(errors, []);
    console.log('PASS: real MP3 streaming, quiet countdown start, smooth ramp to GO, countdown pause/resume, all crossfades, queue wrap, and map cleanup.');
} finally {
    socket.close();
    await fetch(`http://127.0.0.1:${port}/json/close/${target.id}`);
}
