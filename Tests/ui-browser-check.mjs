// node Tests/ui-browser-check.mjs 9254 http://127.0.0.1:5196
import assert from 'node:assert/strict';
import fs from 'node:fs';
const port = process.argv[2] ?? '9254', base = process.argv[3] ?? 'http://127.0.0.1:5196';
const target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, {method: 'PUT'})).json();
const socket = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve, reject) => {socket.onopen = resolve; socket.onerror = reject;});
const pending = new Map(), errors = [], logs = [];
let id = 0;
const send = (method, params = {}) => new Promise((resolve, reject) => {
    const request = ++id; pending.set(request, {resolve, reject}); socket.send(JSON.stringify({id: request, method, params}));
});
socket.onmessage = event => {
    const message = JSON.parse(event.data);
    if (message.id) {
        const request = pending.get(message.id); pending.delete(message.id);
        if (request) message.error ? request.reject(Error(JSON.stringify(message.error))) : request.resolve(message.result);
    } else if (message.method === 'Runtime.exceptionThrown') errors.push(JSON.stringify(message.params));
    else if (message.method === 'Runtime.consoleAPICalled') {
        const text = message.params.args.map(arg => arg.value ?? arg.description ?? '').join(' '); logs.push(text);
        if (/Initialization failed|Frame failed|Uncaptured error|Unhandled exception/.test(text)) errors.push(text);
    }
};
async function evaluate(expression) {
    const result = await send('Runtime.evaluate', {expression, returnByValue: true, awaitPromise: true});
    if (result.exceptionDetails) throw Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
}
const delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
async function waitFor(expression, seconds = 40) {
    for (let i = 0; i < seconds * 10; i++) {
        assert.deepEqual(errors, []);
        if (await evaluate(expression)) return;
        const error = await evaluate(`document.querySelector('.game-error,[role="alert"]')?.textContent`);
        if (error) throw Error(error);
        await delay(100);
    }
    throw Error('Timed out: ' + expression + '\n' + await evaluate('document.body.innerText') + '\n' + logs.join('\n'));
}
const ui = `document.querySelector('.wolf-ui-canvas')`;
const frame = `${ui}?.__uiFrame`;
async function clickControl(name, fraction = .5, drag = null) {
    const point = await evaluate(`(() => {
        const frame=${frame}, node=frame.nodes.find(node=>node.id===${JSON.stringify(name)}), canvas=document.querySelector('canvas'), rect=canvas.getBoundingClientRect();
        const x=node.width*${fraction},y=node.height*.5,t=node.transform;
        return {x:rect.x+(t.a*x+t.c*y+t.e)*rect.width/frame.width,y:rect.y+(t.b*x+t.d*y+t.f)*rect.height/frame.height};
    })()`);
    await send('Input.dispatchMouseEvent', {type: 'mouseMoved', ...point});
    await send('Input.dispatchMouseEvent', {type: 'mousePressed', ...point, button: 'left', clickCount: 1});
    const destination = drag == null ? point : await evaluate(`(() => {const node=${frame}.nodes.find(node=>node.id===${JSON.stringify(name)}),rect=document.querySelector('canvas').getBoundingClientRect();return{x:rect.x+(node.transform.e+node.width*${drag})*rect.width/${frame}.width,y:${point.y}};})()`);
    if (drag != null) await send('Input.dispatchMouseEvent', {type: 'mouseMoved', ...destination, button: 'left', buttons: 1});
    await send('Input.dispatchMouseEvent', {type: 'mouseReleased', ...destination, button: 'left', clickCount: 1});
}
async function key(value, code, virtual, shift = false) {
    await send('Input.dispatchKeyEvent', {type: 'keyDown', key: value, code, windowsVirtualKeyCode: virtual, modifiers: shift ? 8 : 0});
    await send('Input.dispatchKeyEvent', {type: 'keyUp', key: value, code, windowsVirtualKeyCode: virtual});
}
async function screenshot(name) {
    fs.mkdirSync('artifacts/ui', {recursive: true});
    const shot = await send('Page.captureScreenshot', {format: 'png'});
    fs.writeFileSync(`artifacts/ui/${name}.png`, Buffer.from(shot.data, 'base64'));
}
try {
    await send('Runtime.enable'); await send('Page.enable');
    await send('Emulation.setDeviceMetricsOverride', {width: 1280, height: 900, deviceScaleFactor: 1, mobile: false});
    await send('Page.navigate', {url: base + '/ui'}); await send('Page.bringToFront');
    await waitFor(`${frame}?.nodes.some(node=>node.id==='increment')`);
    const draws = await evaluate(`${ui}.uiStats.draws`); await delay(150);
    assert.equal(await evaluate(`${ui}.uiStats.draws`), draws, 'Unchanged UI redrew unnecessarily');
    await clickControl('increment');
    await waitFor(`document.querySelector('[data-ui-id="counter"]').textContent==='Count: 1'`);
    assert.equal(await evaluate('!!document.pointerLockElement'), false, 'UI click reached game capture');
    await key('Enter', 'Enter', 13);
    await waitFor(`document.querySelector('[data-ui-id="counter"]').textContent==='Count: 2'`);
    await key('Tab', 'Tab', 9); await key(' ', 'Space', 32);
    await waitFor(`document.querySelector('[data-ui-id="theme"]').getAttribute('aria-pressed')==='true'`);
    await clickControl('amount', .2, .8);
    await waitFor(`${frame}.nodes.find(node=>node.id==='progress').value>.79`);
    await clickControl('display-name');
    await send('Input.insertText', {text: 'Sam 🚀'});
    await waitFor(`document.querySelector('[data-ui-id="gallery-title"]').textContent==='Welcome, Sam 🚀'`);
    assert.equal(await evaluate(`document.querySelector('[data-ui-id="display-name"]').value`), 'Sam 🚀');
    await clickControl('open-dialog');
    await waitFor(`document.activeElement?.dataset.uiId==='close-dialog' && ${frame}.modalId==='dialog'`);
    await key('Tab', 'Tab', 9);
    assert.equal(await evaluate('document.activeElement.dataset.uiId'), 'close-dialog');
    await clickControl('increment');
    assert.equal(await evaluate(`document.querySelector('[data-ui-id="counter"]').textContent`), 'Count: 2', 'Modal input reached a control behind it');
    await key('Enter', 'Enter', 13);
    await waitFor(`${frame}.modalId===null && document.activeElement?.dataset.uiId==='open-dialog'`);
    const scrollPoint = await evaluate(`(() => {const r=document.querySelector('[data-ui-id="scroll-list"]').getBoundingClientRect();return{x:r.x+20,y:r.y+20}})()`);
    const before = await evaluate(`${frame}.nodes.find(node=>node.id==='item-1').transform.f`);
    await send('Input.dispatchMouseEvent', {type: 'mouseWheel', ...scrollPoint, deltaX: 0, deltaY: 100});
    await waitFor(`${frame}.nodes.find(node=>node.id==='item-1').transform.f<${before}`);
    await screenshot('gallery');
    await send('Emulation.setDeviceMetricsOverride', {width: 700, height: 900, deviceScaleFactor: 2, mobile: false});
    await waitFor(`${frame}.width<750 && ${ui}.width>1000`);
    await screenshot('gallery-retina');
    console.log('PASS: UI images/text/vector drawing, pointer capture, keyboard focus, toggles, slider drag, modal isolation, scrolling, resize, DPI and dirty rendering.');

    await send('Emulation.setDeviceMetricsOverride', {width: 1280, height: 850, deviceScaleFactor: 1, mobile: false});
    await send('Page.navigate', {url: base + '/?map=level1'});
    await waitFor(`!document.querySelector('.loading-overlay') && ${frame}?.nodes.filter(node=>node.kind==='image').length===2`);
    assert.equal(await evaluate(`${ui}.uiStats.imageCount`), 1, 'Hands atlas was loaded twice');
    assert.equal(await evaluate(`document.querySelectorAll('canvas').length`), 2);
    await screenshot('walking');
    await send('Page.navigate', {url: base + '/?map=pine-loop'});
    await waitFor(`!document.querySelector('.loading-overlay') && !!document.querySelector('.race-timing') && !!document.querySelector('.car-paused')`);
    assert.equal(await evaluate(`${ui}.uiStats.imageCount`), 2);
    assert.equal(await evaluate(`document.querySelectorAll('svg').length`), 0, 'Race HUD retained its separate SVG implementation');
    assert(await evaluate(`${frame}.nodes.find(node=>node.id==='race-map').drawing.shapes.length>5`));
    const fonts = await evaluate(`${frame}.nodes.filter(node=>node.id==='race-total'||node.id==='race-laps').map(node=>node.font.family)`);
    assert(fonts.every(font => font.includes('monospace')));
    await screenshot('racing');
    await evaluate(`document.querySelector('a[href="ui"]').click()`);
    await waitFor(`location.pathname==='/ui' && ${frame}?.nodes.some(node=>node.id==='increment')`);
    assert.equal(await evaluate(`document.querySelectorAll('.wolf-ui-canvas').length`), 1, 'UI surface leaked across navigation');
    assert.equal(await evaluate(`document.querySelectorAll('[data-wolf-ui-accessibility]').length`), 1, 'Accessibility tree leaked across navigation');
    assert.deepEqual(errors, []);
    console.log('PASS: walking hands, cockpit/wheel, lap timing, vector minimap, accessible HUD and scene/UI cleanup.');
} finally {
    await send('Page.close').catch(() => {}); socket.close();
}
