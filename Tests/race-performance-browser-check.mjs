// Start the development server and hardware-WebGPU Edge with remote debugging first.
// node Tests/race-performance-browser-check.mjs 9252 http://127.0.0.1:5195 --output=artifacts/performance/after.json
// Optional: --seconds=5 --warmup=2 --views=start,tires --trace --screenshots --min-fps=58 --drive --chase --no-sfx
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';

const port = process.argv[2] ?? '9252', base = process.argv[3] ?? 'http://127.0.0.1:5195';
const option = (name, fallback) => process.argv.find(arg => arg.startsWith(`--${name}=`))?.split('=').slice(1).join('=') ?? fallback;
const seconds = Number(option('seconds', '5')), warmup = Number(option('warmup', '2'));
const output = option('output', 'artifacts/performance/race.json');
const requested = option('views', '').split(',').filter(Boolean);
const fixture = JSON.parse(fs.readFileSync('Tests/Fixtures/pine-loop-lap.json'));
let views = fixture.views.filter(([name]) => !requested.length || requested.includes(name));
assert(views.length && seconds > 0 && warmup >= 0);
fs.mkdirSync(path.dirname(output), {recursive: true});
const target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, {method: 'PUT'})).json();
const socket = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
let id = 0;
const pending = new Map(), errors = [], profiles = [], logs = [];
let traceComplete;
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
        logs.push(line);
        if (/Initialization failed|Frame failed|Uncaptured error/.test(line)) errors.push(line);
        if (line.startsWith('[PROFILE] ')) profiles.push(JSON.parse(line.slice(10)));
    } else if (message.method === 'Tracing.tracingComplete') traceComplete?.(message.params.stream);
};
async function evaluate(expression) {
    const result = await send('Runtime.evaluate', {expression, returnByValue: true, awaitPromise: true});
    if (result.exceptionDetails) throw Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
}
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
async function waitFor(expression) {
    for (let attempt = 0; attempt < 600; attempt++) {
        assert.equal(errors.length, 0, logs.slice(-35).join('\n'));
        if (await evaluate(expression)) return;
        await delay(100);
    }
    throw Error('Timed out: ' + expression + '\n' + logs.slice(-12).join('\n'));
}
const percentile = (values, p) => [...values].sort((a, b) => a - b)[Math.min(values.length - 1, Math.floor(values.length * p))] ?? 0;
const report = {browser: await (await fetch(`http://127.0.0.1:${port}/json/version`)).json(), settings: JSON.parse(fs.readFileSync('WolfNET.Game/wwwroot/graphics-settings.json')), views: []};
report.sfxEnabled = !process.argv.includes('--no-sfx');
if (process.argv.includes('--resume') && fs.existsSync(output)) {
    const previous = JSON.parse(fs.readFileSync(output));
    assert.deepEqual(previous.settings, report.settings, 'Cannot resume with different graphics settings');
    assert.equal(previous.sfxEnabled ?? true, report.sfxEnabled, 'Cannot mix SFX-enabled and disabled samples');
    assert.equal(previous.browser.Browser, report.browser.Browser, 'Cannot resume with a different browser version');
    assert(previous.views.every(view => !!view.driving === process.argv.includes('--drive')), 'Cannot mix stationary and driving runs');
    report.views.push(...previous.views);
    views = views.filter(([name]) => !report.views.some(view => view.name === name));
}
let traceStarted = false;
try {
    await send('Runtime.enable'); await send('Page.enable');
    await send('Emulation.setDeviceMetricsOverride', {width: 1920, height: 1080, deviceScaleFactor: 1, mobile: false});
    await send('Page.addScriptToEvaluateOnNewDocument', {source: `
        window.__racePerf={frames:[],rafFrames:[],cpu:[],bytes:[],counts:[],active:false,submitted:0,uploadBytes:0,spriteCount:0,adapter:null};
        const perf=window.__racePerf,views=new Map(${JSON.stringify(fixture.views)}),originalFetch=window.fetch;
        window.fetch=async function(input,...args){
            const response=await originalFetch.call(this,input,...args),url=String(input?.url??input);
            if(url.includes('audio-settings.json') && ${process.argv.includes('--no-sfx')}) {
                const settings=await response.json(); settings.sfx.volume=0;
                return new Response(JSON.stringify(settings),{headers:{'Content-Type':'application/json'}});
            }
            if(url.includes('maps/pine-loop.json')){
                const map=await response.json(),view=views.get(new URLSearchParams(location.search).get('reviewView'));
                if(view)Object.assign(map.playerStart,{x:view[0],y:view[1],directionX:view[2],directionY:view[3]});
                return new Response(JSON.stringify(map),{headers:{'Content-Type':'application/json'}});
            }
            ${process.argv.includes('--screenshots') ? `if(url.includes('/objects/')&&url.endsWith('.json')){
                const object=await response.json();if(object.animation)object.animation.framesPerSecond=0.000001;
                return new Response(JSON.stringify(object),{headers:{'Content-Type':'application/json'}});
            }` : ''}
            return response;
        };
        const request=GPU.prototype.requestAdapter;
        GPU.prototype.requestAdapter=async function(options){
            const adapter=await request.call(this,options);
            if(adapter)perf.adapter={preference:options?.powerPreference??'default',vendor:adapter.info?.vendor,architecture:adapter.info?.architecture,device:adapter.info?.device,description:adapter.info?.description};
            return adapter;
        };
        const write=GPUQueue.prototype.writeBuffer;
        GPUQueue.prototype.writeBuffer=function(buffer,offset,data,dataOffset=0,size){
            if(buffer.label==='WolfNET First Person')perf.firstPerson=new Float32Array(data.buffer??data,data.byteOffset??0,3)[2];
            if(buffer.label==='WolfNET Camera'){
                const camera=new Float32Array(data.buffer??data,data.byteOffset??0,2);perf.camera=[camera[0],camera[1]];
            }
            if(buffer.label==='WolfNET Sprites')perf.uploadBytes+=(size??(data.byteLength/(data.BYTES_PER_ELEMENT??1)-dataOffset))*(data.BYTES_PER_ELEMENT??1);
            if(buffer.label==='WolfNET Map Info'){
                const words=new Uint32Array(data.buffer??data,(data.byteOffset??0)+dataOffset*(data.BYTES_PER_ELEMENT??1));
                if(offset===0)perf.spriteCount=words[2];else if(offset===8)perf.spriteCount=words[0];
            }
            return write.call(this,buffer,offset,data,dataOffset,size);
        };
        const submit=GPUQueue.prototype.submit;
        GPUQueue.prototype.submit=function(...args){
            perf.submitted++;
            if(perf.active){perf.frames.push(performance.now());perf.rafFrames.push(perf.rafStamp);perf.bytes.push(perf.uploadBytes);perf.counts.push(perf.spriteCount);}
            perf.uploadBytes=0;
            return submit.apply(this,args);
        };
        const raf=window.requestAnimationFrame;
        window.requestAnimationFrame=function(callback){return raf.call(this,timestamp=>{
            perf.rafStamp=timestamp;
            const started=performance.now(),count=perf.submitted,result=callback(timestamp);
            if(perf.active&&count!==perf.submitted)perf.cpu.push(performance.now()-started);
            return result;
        });};
    `});
    if (process.argv.includes('--trace')) {
        await send('Tracing.start', {categories: 'devtools.timeline,v8,blink,cc,gpu', transferMode: 'ReturnAsStream'});
        traceStarted = true;
    }
    for (const [name] of views) {
        profiles.length = 0;
        await send('Page.navigate', {url: `${base}/?map=pine-loop&profile=1&reviewView=${encodeURIComponent(name)}`});
        await send('Page.bringToFront');
        await waitFor('window.__racePerf?.submitted>5&&!document.querySelector(".loading-overlay")');
        if (process.argv.includes('--drive')) {
            const point = await evaluate('(()=>{const r=document.querySelector("canvas").getBoundingClientRect();return{x:r.x+r.width/2,y:r.y+r.height/2};})()');
            await send('Input.dispatchMouseEvent', {type: 'mousePressed', ...point, button: 'left', clickCount: 1});
            await send('Input.dispatchMouseEvent', {type: 'mouseReleased', ...point, button: 'left', clickCount: 1});
            await waitFor('!!document.pointerLockElement&&!document.querySelector(".car-paused")');
            if (process.argv.includes('--chase')) {
                await send('Input.dispatchKeyEvent', {type: 'keyDown', code: 'KeyV', key: 'v', windowsVirtualKeyCode: 86});
                await send('Input.dispatchKeyEvent', {type: 'keyUp', code: 'KeyV', key: 'v', windowsVirtualKeyCode: 86});
                await waitFor('window.__racePerf.firstPerson===0');
            }
            await send('Input.dispatchKeyEvent', {type: 'keyDown', code: 'KeyW', key: 'w', windowsVirtualKeyCode: 87});
        }
        await delay(warmup * 1000);
        const cameraStart = await evaluate('window.__racePerf.camera');
        await evaluate('window.__racePerf.active=true');
        await delay(seconds * 1000);
        const data = await evaluate('window.__racePerf.active=false;window.__racePerf');
        assert.deepEqual(errors, []);
        const intervals = data.frames.slice(1).map((time, i) => time - data.frames[i]);
        const rafIntervals = data.rafFrames.slice(1).map((time, i) => time - data.rafFrames[i]);
        const result = {name, fps: (data.frames.length - 1) * 1000 / (data.frames.at(-1) - data.frames[0]),
            frameMs: {median: percentile(intervals, .5), p95: percentile(intervals, .95), p99: percentile(intervals, .99)},
            rafMs: {median: percentile(rafIntervals, .5), p95: percentile(rafIntervals, .95), p99: percentile(rafIntervals, .99)},
            callbackMs: {median: percentile(data.cpu, .5), p95: percentile(data.cpu, .95)},
            uploadedSprites: percentile(data.counts, .5), uploadBytes: percentile(data.bytes, .5), adapter: data.adapter, profiles: [...profiles]};
        if (process.argv.includes('--drive')) {
            result.driving = {thirdPerson: process.argv.includes('--chase'), start: cameraStart, end: data.camera};
            assert(Math.hypot(data.camera[0] - cameraStart[0], data.camera[1] - cameraStart[1]) > .1, 'Driving benchmark did not move the car');
            await send('Input.dispatchKeyEvent', {type: 'keyUp', code: 'KeyW', key: 'w', windowsVirtualKeyCode: 87});
        }
        report.views.push(result);
        console.log(`${name}: ${result.fps.toFixed(1)} FPS; submit p95 ${result.frameMs.p95.toFixed(1)} ms; RAF p95 ${result.rafMs.p95.toFixed(1)} ms; CPU p95 ${result.callbackMs.p95.toFixed(1)} ms; ${result.uploadedSprites} sprites, ${result.uploadBytes} bytes; ${JSON.stringify(data.adapter)}`);
        if (process.argv.includes('--screenshots')) {
            const capture = await send('Page.captureScreenshot', {format: 'png'});
            fs.writeFileSync(output.replace(/\.json$/, '') + `-${name}.png`, Buffer.from(capture.data, 'base64'));
        }
        fs.writeFileSync(output, JSON.stringify(report, null, 2));
    }
    if (option('min-fps', '')) {
        const minimum = Number(option('min-fps', '58'));
        assert(report.views.every(view => view.fps >= minimum && view.frameMs.p95 <= 20), '60 FPS acceptance target was not met; see ' + output);
    }
} finally {
    if (traceStarted) {
        const finished = new Promise(resolve => { traceComplete = resolve; });
        await send('Tracing.end');
        const handle = await finished;
        const file = fs.openSync(output.replace(/\.json$/, '') + '.trace.json', 'w');
        try {
            for (;;) {
                const chunk = await send('IO.read', {handle, size: 1024 * 1024});
                fs.writeSync(file, chunk.base64Encoded ? Buffer.from(chunk.data, 'base64') : chunk.data);
                if (chunk.eof) break;
            }
        } finally { fs.closeSync(file); await send('IO.close', {handle}); }
    }
    await send('Page.close').catch(() => {});
    socket.close();
    for (const task of pending.values()) clearTimeout(task.timer);
}
