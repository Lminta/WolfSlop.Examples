// Run shader/pixel fixtures without starting the game's animation loop.
// Chromium: --headless=new --enable-unsafe-webgpu --use-webgpu-adapter=swiftshader --remote-debugging-port=9252
// node Tests/gpu-browser-check.mjs 9252 http://127.0.0.1:5195
import fs from "node:fs";
import { checkGpu } from "./gpu-check.mjs";

const port = process.argv[2] ?? "9252", base = process.argv[3] ?? "http://127.0.0.1:5195";
const target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: "PUT" })).json();
const socket = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
let id = 0;
const pending = new Map();
socket.onmessage = event => {
    const message = JSON.parse(event.data);
    if (!message.id) return;
    const task = pending.get(message.id); pending.delete(message.id);
    message.error ? task.reject(Error(JSON.stringify(message.error))) : task.resolve(message.result);
};
function send(method, params = {}) {
    return new Promise((resolve, reject) => {
        const requestId = ++id; pending.set(requestId, { resolve, reject });
        socket.send(JSON.stringify({ id: requestId, method, params }));
    });
}
async function evaluate(expression) {
    const result = await send("Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true });
    if (result.exceptionDetails) throw Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
}
try {
    await send("Page.enable"); await send("Runtime.enable");
    await send("Page.navigate", { url: base + "/css/app.css" });
    for (let attempt = 0; attempt < 100; attempt++) {
        if (await evaluate("isSecureContext && !!navigator.gpu && document.readyState==='complete'")) break;
        if (attempt === 99) throw Error("WebGPU test page did not load.");
        await new Promise(resolve => setTimeout(resolve, 100));
    }
    const sources = Object.fromEntries(["raycast", "walls", "sprites"].map(name =>
        [name, fs.readFileSync(`WolfNET.Game/wwwroot/shaders/${name}.wgsl`, "utf8").replace(/^\uFEFF/, "")]));
    console.log("GPU CHECK", await evaluate(`(${checkGpu.toString()})(${JSON.stringify(sources)})`));
} finally {
    await send("Page.close").catch(() => {});
    socket.close();
}
