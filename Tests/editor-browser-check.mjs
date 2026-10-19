// Start the editor and a headless Chromium instance with remote debugging first.
// node Tests/editor-browser-check.mjs 9246 http://127.0.0.1:5080 [screenshot-path]
import fs from "node:fs";
import assert from "node:assert/strict";

const port = process.argv[2] ?? "9246", base = process.argv[3] ?? "http://127.0.0.1:5080";
const target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: "PUT" })).json();
const socket = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
let id = 0;
const pending = new Map(), errors = [];
function send(method, params = {}) {
    return new Promise((resolve, reject) => {
        const requestId = ++id;
        pending.set(requestId, { resolve, reject });
        socket.send(JSON.stringify({ id: requestId, method, params }));
    });
}
socket.onmessage = event => {
    const result = JSON.parse(event.data);
    if (result.id) {
        const request = pending.get(result.id); pending.delete(result.id);
        result.error ? request.reject(Error(JSON.stringify(result.error))) : request.resolve(result.result);
    } else if (result.method === "Runtime.exceptionThrown") errors.push(result.params.exceptionDetails);
    else if (result.method === "Page.javascriptDialogOpening") void send("Page.handleJavaScriptDialog", { accept: true });
};
async function evaluate(expression) {
    const result = await send("Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true });
    if (result.exceptionDetails) throw Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
}
async function waitFor(expression) {
    for (let i = 0; i < 100; i++) {
        if (await evaluate(expression)) return;
        await new Promise(resolve => setTimeout(resolve, 100));
    }
    throw Error(`Timed out: ${expression}\n${await evaluate("document.body.innerText")}\n${JSON.stringify(errors)}`);
}
async function click(id) { await evaluate(`document.getElementById(${JSON.stringify(id)}).click()`); }
async function cell(x, y, button = "left", tool = "brush") {
    await evaluate(`document.querySelector('[data-tool="${tool}"]').click(); document.getElementById('map-canvas').scrollIntoView({block:'center',inline:'center'})`);
    const point = await evaluate(`(() => { const c=document.getElementById('map-canvas'), b=c.getBoundingClientRect(); return {x:b.left+(${x}+.5)*parseFloat(c.style.width)/25,y:b.top+(${y}+.5)*parseFloat(c.style.height)/25}; })()`);
    await send("Input.dispatchMouseEvent", { type: "mousePressed", button, buttons: button === "left" ? 1 : 2, clickCount: 1, ...point });
    await send("Input.dispatchMouseEvent", { type: "mouseReleased", button, buttons: 0, clickCount: 1, ...point });
}
try {
    await send("Runtime.enable"); await send("Page.enable");
    await send("Emulation.setDeviceMetricsOverride", { width: 1500, height: 1000, deviceScaleFactor: 1, mobile: false });
    await send("Page.navigate", { url: base });
    await waitFor("document.getElementById('document-title')?.textContent==='showcase' && document.querySelectorAll('.palette-item').length>20");
    assert.equal(await evaluate("document.getElementById('map-canvas').width>0"), true);
    await click("validate");
    await waitFor("document.getElementById('validation').classList.contains('success')");
    await evaluate(`window.__latest=null; const realFetch=window.fetch; window.fetch=async (...args)=>{if(args[0]==='/api/validate')window.__latest=JSON.parse(args[1].body);return realFetch(...args);};`);
    await evaluate(`document.querySelector('.palette-item[data-id="11"]').click(); document.getElementById('texture-target').value='floorTexture'; document.querySelector('#texture-options button[title="textures/wood.png"]').click();`);
    await click("apply-object"); await click("validate");
    await waitFor("window.__latest?.objects[11].floorTexture==='textures/wood.png'");
    await click("undo");
    await evaluate("document.querySelector('.palette-item[data-id=\"1\"]').click()");
    await cell(10, 10, "left", "fill"); await click("validate");
    await waitFor("window.__latest?.map.worldMap[10][10]===1");
    assert.equal(await evaluate("window.__latest.map.worldMap[11][10]"), 1, "Fill failed to cover connected floor");
    await click("undo");
    await evaluate("document.getElementById('new-type').value='passage'");
    await click("add-object");
    const createdId = await evaluate("JSON.parse(document.getElementById('object-json').value).id");
    await cell(10, 10); await click("validate");
    await waitFor(`window.__latest?.map.worldMap[10][10]===${createdId}`);
    assert.equal(await evaluate(`window.__latest.objects[${createdId}].type`), "passage");
    await click("undo"); await click("undo");
    await evaluate("document.getElementById('map-width').value='26'; document.getElementById('map-height').value='27'");
    await click("resize");
    assert.equal(await evaluate("document.getElementById('map-dimensions').textContent"), "26 × 27");
    await click("undo");
    await click("fit");

    // Observe outgoing documents without writing the user's engine maps.
    await evaluate(`window.__documents=[]; const originalFetch=window.fetch; window.fetch=async (...args)=>{ if(args[0]==='/api/validate')window.__documents.push(JSON.parse(args[1].body)); return originalFetch(...args); };`);
    await evaluate("document.querySelector('.palette-item[data-id=\"1\"]').click()");
    await cell(10, 10);
    await click("validate");
    await waitFor("window.__documents.length===1");
    assert.equal(await evaluate("window.__documents.at(-1).map.worldMap[10][10]"), 1, "Brush missed grid coordinate");
    await click("undo"); await click("validate");
    await waitFor("window.__documents.length===2");
    assert.equal(await evaluate("window.__documents.at(-1).map.worldMap[10][10]"), 0, "Undo did not restore the cell");
    await click("redo");
    await cell(10, 10, "right");
    assert.equal(await evaluate("document.getElementById('selected-id').textContent"), "#1", "Right-click did not pick the object");
    await click("undo");
    await cell(10, 10, "left", "spawn");
    assert.equal(await evaluate("Number(document.getElementById('spawn-x').value)"), 10.5);
    assert.equal(await evaluate("Number(document.getElementById('spawn-y').value)"), 10.5);
    await click("undo");

    // Object edits affect only this browser document until Save is explicitly pressed.
    await evaluate(`document.querySelector('.palette-item[data-id="11"]').click(); const field=document.getElementById('object-json'); const d=JSON.parse(field.value); d.floorTexture='textures/mossy.png'; field.value=JSON.stringify(d); field.dispatchEvent(new Event('input'));`);
    await click("apply-object"); await click("validate");
    await waitFor("window.__documents.length===3");
    assert.equal(await evaluate("window.__documents.at(-1).objects[11].floorTexture"), "textures/mossy.png");
    await click("undo");
    await evaluate("document.getElementById('show-collision').click(); document.getElementById('show-rays').click(); document.getElementById('show-ids').click()");
    await click("fit");
    await new Promise(resolve => setTimeout(resolve, 700));
    assert.equal(await evaluate("!!localStorage.getItem('wolfnet-editor-draft-v1')"), true, "Draft not persisted");
    await send("Page.reload");
    await waitFor("document.getElementById('document-title')?.textContent==='showcase' && !document.getElementById('draft-banner').hidden");
    await click("restore-draft");
    assert.equal(await evaluate("document.getElementById('dirty-dot').hidden"), false, "Restored draft lost dirty status");
    await click("dismiss-draft");
    await click("validate");
    await waitFor("document.getElementById('validation').classList.contains('success')");
    if (process.argv[4]) {
        const result = await send("Page.captureScreenshot", { format: "png" });
        fs.writeFileSync(process.argv[4], Buffer.from(result.data, "base64"));
    }
    // Exercise browser decoding/conversion without adding files to the user's texture library.
    await evaluate(`(async()=>{window.__textureUpload=null; const textureFetch=window.fetch; window.fetch=async (...args)=>{
        if(args[0]==='/api/textures'){window.__textureUpload=JSON.parse(args[1].body);return new Response(JSON.stringify({path:'textures/mossy.png',width:64,height:64}),{headers:{'Content-Type':'application/json'}});}
        return textureFetch(...args);
    };
    document.querySelector('.palette-item[data-id="11"]').click();
    document.getElementById('texture-target').value='floorTexture';
    const source=document.createElement('canvas');source.width=32;source.height=16;
    const context=source.getContext('2d');context.fillStyle='#ff0000';context.fillRect(0,0,16,16);
    const blob=await new Promise(resolve=>source.toBlob(resolve,'image/png'));
    const transfer=new DataTransfer();transfer.items.add(new File([blob],'custom-texture.png',{type:'image/png'}));
    const input=document.getElementById('texture-file');input.files=transfer.files;input.dispatchEvent(new Event('change'));})()`);
    await waitFor("window.__textureUpload && !document.getElementById('import-texture').disabled");
    assert.equal(await evaluate("JSON.parse(document.getElementById('object-json').value).floorTexture"), "textures/mossy.png");
    const pngInfo = await evaluate(`(async()=>{
        const bytes=Uint8Array.from(atob(window.__textureUpload.png),c=>c.charCodeAt(0)),data=new DataView(bytes.buffer);
        const bitmap=await createImageBitmap(new Blob([bytes],{type:'image/png'}));
        const output=document.createElement('canvas');output.width=output.height=64;const context=output.getContext('2d');context.drawImage(bitmap,0,0);bitmap.close();
        return {width:data.getUint32(16),height:data.getUint32(20),alpha:context.getImageData(63,0,1,1).data[3]};
    })()`);
    assert.deepEqual(pngInfo, { width: 64, height: 64, alpha: 0 }, "Texture import must resize and preserve transparency");
    await click("apply-object"); await click("undo");
    assert.equal(await evaluate("document.getElementById('map-name').closest('section').id"), "map-settings");
    assert.equal(await evaluate("document.getElementById('object-json').closest('section').id"), "object-settings");
    await evaluate("document.querySelector('.palette-item[data-id=\"11\"]').click()");
    await click("delete-object");
    assert.equal(await evaluate("document.getElementById('delete-replacement').hidden"), false, "Referenced passage needs a replacement");
    await evaluate("document.getElementById('delete-replacement').value='0'");
    await click("confirm-delete");
    assert.equal(await evaluate("!!document.querySelector('.palette-item[data-id=\"11\"]')"), false);
    await click("validate");
    await waitFor("document.getElementById('validation').classList.contains('success')");
    await click("undo");
    assert.equal(await evaluate("!!document.querySelector('.palette-item[data-id=\"11\"]')"), true);
    await evaluate("document.querySelector('.palette-item[data-id=\"26\"]').click()");
    await click("delete-object");
    assert.equal(await evaluate("document.getElementById('delete-replacement').hidden"), true, "Unused object needs no replacement");
    await click("confirm-delete");
    assert.equal(await evaluate("!!document.querySelector('.palette-item[data-id=\"26\"]')"), false);
    await click("undo");
    assert.equal(await evaluate("!!document.querySelector('.palette-item[data-id=\"26\"]')"), true);
    await click("redo");
    assert.equal(await evaluate("!!document.querySelector('.palette-item[data-id=\"26\"]')"), false);
    await waitFor("JSON.parse(localStorage.getItem('wolfnet-editor-draft-v1') ?? 'null')?.document.deletedObjectIds?.includes(26)");
    await send("Page.reload");
    await waitFor("document.getElementById('document-title')?.textContent==='showcase' && !document.getElementById('draft-banner').hidden");
    await click("restore-draft");
    assert.equal(await evaluate("!!document.querySelector('.palette-item[data-id=\"26\"]')"), false, "Draft restore resurrected deleted library object");
    await click("dismiss-draft");
    assert.equal(errors.length, 0, JSON.stringify(errors));
    console.log("PASS: editor browser controls, separate settings panels, referenced/unused object deletion, undo/redo, and draft restoration.");
} finally {
    await send("Page.close").catch(() => {});
    socket.close();
}
