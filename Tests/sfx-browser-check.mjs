// node Tests/sfx-browser-check.mjs <Chromium debug port> <game URL>
// Default: temporary test WAVs through CDP. Add --real to load the actual game sounds.
import assert from 'node:assert/strict';
import fs from 'node:fs';

const port = process.argv[2] ?? '9387', base = process.argv[3] ?? 'http://127.0.0.1:5037';
const settings = JSON.parse(fs.readFileSync('WolfNET.Game/wwwroot/audio-settings.json', 'utf8'));
const clips = Object.entries(settings.sfx.clips);
const realSounds = process.argv.includes('--real');
const mudCheck = process.argv.includes('--mud');
function wave(seconds, frequency) {
    const rate = 24000, samples = Math.round(seconds * rate), data = Buffer.alloc(44 + samples * 2);
    data.write('RIFF'); data.writeUInt32LE(data.length - 8, 4); data.write('WAVEfmt ', 8);
    data.writeUInt32LE(16, 16); data.writeUInt16LE(1, 20); data.writeUInt16LE(1, 22);
    data.writeUInt32LE(rate, 24); data.writeUInt32LE(rate * 2, 28); data.writeUInt16LE(2, 32);
    data.writeUInt16LE(16, 34); data.write('data', 36); data.writeUInt32LE(samples * 2, 40);
    for (let i = 0; i < samples; i++) data.writeInt16LE(Math.round(1500 * Math.sin(i * frequency * Math.PI * 2 / rate)), 44 + i * 2);
    return data;
}
function hash(data) { let value = 2166136261; for (const byte of data) value = Math.imul(value ^ byte, 16777619); return value >>> 0; }
const fixtures = new Map(), cueByHash = {};
for (const [index, [cue, paths]] of clips.entries()) for (const [variant, path] of paths.entries()) {
    const bytes = realSounds ? fs.readFileSync('WolfNET.Game/wwwroot/' + path) : wave(.6 + index * .05, 200 + index * 30 + variant * 5);
    fixtures.set(path, bytes);
    cueByHash[hash(bytes)] = cue;
}
const target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, {method: 'PUT'})).json();
const socket = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
let id = 0, mute = false;
const pending = new Map(), errors = [], requests = [];
function send(method, params = {}) {
    return new Promise((resolve, reject) => {
        const requestId = ++id;
        const timer = setTimeout(() => reject(Error('CDP timeout: ' + method)), 30000);
        pending.set(requestId, {resolve, reject, timer});
        socket.send(JSON.stringify({id: requestId, method, params}));
    });
}
socket.onmessage = async event => {
    const message = JSON.parse(event.data);
    if (message.id) {
        const task = pending.get(message.id);
        if (!task) return;
        pending.delete(message.id); clearTimeout(task.timer);
        message.error ? task.reject(Error(JSON.stringify(message.error))) : task.resolve(message.result);
    } else if (message.method === 'Runtime.exceptionThrown') errors.push(JSON.stringify(message.params));
    else if (message.method === 'Runtime.consoleAPICalled') {
        const text = message.params.args.map(arg => arg.value ?? arg.description ?? '').join(' ');
        if (/Initialization failed|Frame failed|Effects unavailable|Clip unavailable|Audio activation|Audio close/.test(text)) errors.push(text);
    } else if (message.method === 'Fetch.requestPaused') {
        const {requestId, request} = message.params;
        const path = decodeURIComponent(new URL(request.url).pathname).slice(1);
        requests.push(path);
        if (mudCheck && path.startsWith('objects/raceway/passage-')) {
            const passage = JSON.parse(fs.readFileSync('WolfNET.Game/wwwroot/' + path, 'utf8'));
            if (passage.surface === 'asphalt') passage.surface = 'mud';
            await send('Fetch.fulfillRequest', {requestId, responseCode:200,
                responseHeaders:[{name:'Content-Type',value:'application/json'}],
                body:Buffer.from(JSON.stringify(passage)).toString('base64')});
            return;
        }
        if (realSounds && path.startsWith('sounds/')) { await send('Fetch.continueRequest', {requestId}); return; }
        const body = path === 'audio-settings.json'
            ? Buffer.from(JSON.stringify(mute ? {...settings, volume: 0} : settings)).toString('base64') : fixtures.get(path)?.toString('base64');
        await send('Fetch.fulfillRequest', {requestId, responseCode: body ? 200 : 404,
            responseHeaders: [{name: 'Content-Type', value: path.endsWith('.json') ? 'application/json' : 'audio/wav'}], body: body ?? ''});
    }
};
async function evaluate(expression) {
    const result = await send('Runtime.evaluate', {expression, awaitPromise: true, returnByValue: true});
    if (result.exceptionDetails) throw Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
}
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
async function waitFor(expression) {
    for (let attempt = 0; attempt < 450; attempt++) {
        assert.deepEqual(errors, []);
        if (await evaluate(expression)) return;
        await delay(100);
    }
    throw Error('Timed out: ' + expression);
}
async function clickCanvas() {
    const point = await evaluate(`(() => { const r = document.querySelector('canvas').getBoundingClientRect();
        return {x:r.x+r.width/2,y:r.y+r.height/2}; })()`);
    await send('Input.dispatchMouseEvent', {type:'mousePressed', ...point, button:'left', clickCount:1});
    await send('Input.dispatchMouseEvent', {type:'mouseReleased', ...point, button:'left', clickCount:1});
}
async function key(code, key, virtual, down) {
    await send('Input.dispatchKeyEvent', {type: down ? 'keyDown' : 'keyUp', code, key, windowsVirtualKeyCode: virtual});
}
try {
    await send('Page.enable'); await send('Runtime.enable');
    await send('Emulation.setFocusEmulationEnabled', {enabled:true}); await send('Page.bringToFront');
    await send('Fetch.enable', {patterns:[{urlPattern:'*/sounds/*.wav'}, {urlPattern:'*/audio-settings.json'},
        ...(mudCheck ? [{urlPattern:'*/objects/raceway/passage-*.json'}] : [])]});
    await send('Page.addScriptToEvaluateOnNewDocument', {source: `
        window.__contexts = [];
        window.AudioContext = new Proxy(window.AudioContext, {construct(target,args) {
            const context = Reflect.construct(target,args); __contexts.push(context);
            context.__decoded = []; context.__voices = []; context.__gains = [];
            const decode = context.decodeAudioData.bind(context);
            context.decodeAudioData = (data,...args) => {
                let signature = 2166136261;
                for (const byte of new Uint8Array(data)) signature = Math.imul(signature ^ byte,16777619);
                const cue = ${JSON.stringify(cueByHash)}[signature >>> 0];
                return decode(data,...args).then(buffer => { buffer.__cue=cue; context.__decoded.push(buffer); return buffer; });
            };
            const gain = context.createGain.bind(context);
            context.createGain = () => { const node = gain(); context.__gains.push(node); return node; };
            const createSource = context.createBufferSource.bind(context);
            context.createBufferSource = () => {
                const source = createSource(), voice = {source,started:false,stopped:false}; context.__voices.push(voice);
                const connect = source.connect.bind(source);
                source.connect = gain => { voice.gain = gain; const next = gain.connect.bind(gain);
                    gain.connect = panner => { voice.panner = panner; return next(panner); }; return connect(gain); };
                const start = source.start.bind(source), stop = source.stop.bind(source);
                source.start = (...args) => { voice.started = true; return start(...args); };
                source.stop = (...args) => { voice.stopped = true; return stop(...args); };
                return source;
            };
            return context;
        }});
        Object.defineProperty(window,'__sfx',{get:()=>__contexts.findLast(context=>context.__decoded.length)});
        window.__cueVoices = (cue,active=false) => {
            return (__sfx?.__voices ?? []).filter(voice => voice.source.buffer.__cue === cue &&
                (!active || voice.started && !voice.stopped));
        };
    `});
    await send('Page.navigate', {url: base + '/?map=pine-loop'});
    await waitFor(`window.__sfx && !document.querySelector('.loading-overlay') && __sfx.__decoded.length === ${fixtures.size}`);
    assert.equal(await evaluate('__sfx.__voices.length'), 0);
    await clickCanvas();
    await waitFor(`document.pointerLockElement && __cueVoices('countdownTick').length === 1 && __cueVoices('engineIdle',true).length === 3`);
    assert(await evaluate(`new Set(__cueVoices('engineIdle',true).map(voice=>voice.source.buffer)).size === 3`));
    assert(await evaluate(`__cueVoices('engineIdle',true).every(voice=>voice.gain.channelCount === 1 && voice.gain.channelCountMode === 'explicit')`));
    assert(await evaluate(`__cueVoices('engineIdle',true).some(voice=>voice.panner.pan.value<-.1) &&
        __cueVoices('engineIdle',true).some(voice=>voice.panner.pan.value>.1)`));
    await waitFor(`__cueVoices('countdownTick').length === 3`);
    await evaluate('document.exitPointerLock()');
    await waitFor(`__sfx.state === 'suspended'`);
    const pausedClock = await evaluate('__sfx.currentTime'), beepCount = await evaluate(`__cueVoices('countdownTick').length`);
    await delay(400);
    assert.equal(await evaluate('__sfx.currentTime'), pausedClock);
    await clickCanvas();
    await waitFor(`__sfx.state === 'running'`);
    assert.equal(await evaluate(`__cueVoices('countdownTick').length`), beepCount);
    await waitFor(`document.querySelector('.race-go') && __cueVoices('countdownGo').length === 1`);
    assert.equal(await evaluate(`__cueVoices('countdownTick').length`), 5);
    await key('KeyW','w',87,true);
    await delay(1200);
    await waitFor(`__cueVoices('engineLoad',true).length > 0 && __cueVoices('engineIdle',true)[0].source.playbackRate.value > .8`);
    if (mudCheck) {
        await waitFor(`__cueVoices('mudSlow',true).length > 0`);
        await waitFor(`__cueVoices('mudMid').length > 0`);
        await waitFor(`__cueVoices('mudHigh').length > 0`);
    }
    await key('Space',' ',32,true);
    await waitFor(`__cueVoices('brakes').length > 0`);
    await key('Space',' ',32,false); await key('KeyW','w',87,false);
    const firstBrake = await evaluate(`(__sfx.__firstBrake = __cueVoices('brakes').at(-1).source.buffer, true)`);
    assert(firstBrake);
    await key('KeyW','w',87,true); await delay(500);
    const previousBrakes = await evaluate(`__cueVoices('brakes').length`);
    await key('Space',' ',32,true);
    await waitFor(`__cueVoices('brakes').length > ${previousBrakes}`);
    assert(await evaluate(`__cueVoices('brakes').at(-1).source.buffer !== __sfx.__firstBrake`));
    await key('Space',' ',32,false); await key('KeyW','w',87,false);
    await evaluate(`window.__oldSfx=__sfx; document.exitPointerLock();
        [...document.querySelectorAll('.map-switcher button')].find(button=>button.textContent.includes('Showcase')).click()`);
    await waitFor(`__oldSfx.state === 'closed' && !document.querySelector('.loading-overlay')`);
    assert(await evaluate('__oldSfx.__voices.every(voice=>voice.stopped)'));

    mute = true;
    const previousRequests = requests.length;
    await send('Page.navigate', {url: base + '/?map=pine-loop&muted-check=1'});
    await waitFor(`document.querySelector('.map-switcher') && !document.querySelector('.loading-overlay')`);
    assert.equal(await evaluate('__contexts.length'), 0);
    assert(!requests.slice(previousRequests).some(path=>path.startsWith('sounds/')));
    assert.deepEqual(errors, []);
    console.log(`PASS: C# SFX backend, WAV decoding, distinct engines per car, randomized braking, ${mudCheck ? 'mud rolling, ' : ''}mono downmix/stereo placement, countdown/GO, pause/resume, cleanup, and zero-volume bypass (${realSounds ? 'actual generated sounds' : 'temporary test tones'}).`);
} finally {
    socket.close();
    await fetch(`http://127.0.0.1:${port}/json/close/${target.id}`);
}
