// Launch the app and a Chromium browser with a remote-debugging port first.
// node Tests/browser-check.mjs 9243 http://127.0.0.1:5194
import fs from "node:fs";
import {checkGpu} from "./gpu-check.mjs";

const port = process.argv[2] ?? "9243";
const appUrl = process.argv[3] ?? "http://127.0.0.1:5194";
const missingTextures = process.argv.includes("--missing-textures");
const target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`,{method:"PUT"})).json();
const socket = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve,reject)=>{socket.onopen=resolve;socket.onerror=reject;});
let id=0;
const pending=new Map(), logs=[];
const objectRequests=new Set();
let heldObjectRequest;
let failedTextures=0;
const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
function send(method,params={}) {
    return new Promise((resolve,reject)=>{
        const requestId=++id;
        pending.set(requestId,{resolve,reject});
        socket.send(JSON.stringify({id:requestId,method,params}));
    });
}
socket.onmessage=event=>{
    const message=JSON.parse(event.data);
    if(message.id) {
        const request=pending.get(message.id); pending.delete(message.id);
        message.error?request.reject(message.error):request.resolve(message.result);
    } else if(message.method==="Fetch.requestPaused") {
        const {requestId,request}=message.params;
        if(request.url.includes("/pics/")) {
            failedTextures++;
            void send("Fetch.fulfillRequest",{requestId,responseCode:404,body:""});
        } else {
            objectRequests.add(request.url);
            if(request.url.endsWith("/objects/203.json")) heldObjectRequest=requestId;
            else void send("Fetch.continueRequest",{requestId});
        }
    } else if(message.method==="Runtime.consoleAPICalled") {
        const line=message.params.args.map(arg=>arg.value??arg.description??"").join(" ");
        logs.push(line);
        if(/\[GPU\]|\[GAME\]|failed/i.test(line)) console.log(line);
    }
};
async function evaluate(expression) {
    const result=await send("Runtime.evaluate",{expression,returnByValue:true,awaitPromise:true});
    if(result.exceptionDetails) throw Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
}
async function waitFor(expression) {
    for(let i=0;i<200;i++) {
        if(logs.some(line=>/Initialization failed|Frame failed|Uncaptured error/.test(line))) throw Error(logs.join("\n"));
        if(await evaluate(expression)) return;
        await delay(100);
    }
    throw Error("Timed out: "+expression);
}
function assert(condition,message) { if(!condition) throw Error(message); }

try {
    await send("Runtime.enable"); await send("Page.enable");
    await send("Page.addScriptToEvaluateOnNewDocument",{source:`
        const write=GPUQueue.prototype.writeBuffer;
        const copy=GPUQueue.prototype.copyExternalImageToTexture;
        window.__copiedTextures=0;
        GPUQueue.prototype.copyExternalImageToTexture=function(...args) {
            window.__copiedTextures++; return copy.apply(this,args);
        };
        GPUQueue.prototype.writeBuffer=function(buffer,offset,data,...rest) {
            if(buffer.label==='WolfNET Camera') window.__camera=Array.from(new Float32Array(data.buffer??data,data.byteOffset??0,8));
            return write.call(this,buffer,offset,data,...rest);
        };
    `});
    await send("Fetch.enable",{patterns:[{urlPattern:"*objects/*.json"},
        ...(missingTextures?[{urlPattern:"*pics/*"}]:[])]});
    await send("Emulation.setDeviceMetricsOverride",{width:1920,height:1080,deviceScaleFactor:1,mobile:false});
    await send("Page.navigate",{url:appUrl}); await send("Page.bringToFront");
    for(let i=0;i<200&&!heldObjectRequest;i++) await delay(100);
    assert(heldObjectRequest,"Last object was never requested");
    assert(!await evaluate("!!window.__camera"),"Game rendered before all definitions loaded");
    await send("Fetch.continueRequest",{requestId:heldObjectRequest});
    await waitFor("!!window.__camera");
    assert(objectRequests.size===17,"Not all 16 object definitions and the manifest were loaded");
    if(missingTextures) {
        assert(failedTextures===11,"Not all texture failures were exercised");
        assert(await evaluate("window.__copiedTextures")===0,"Missing image unexpectedly copied");
        console.log("PASS: all 11 unavailable textures use pink and the game still starts.");
    }
    await send("Fetch.disable");
    assert(logs.some(line=>line.includes("revision=1, cells=576")),"Initial compiled revision missing");
    assert(logs.some(line=>line.includes("19 sprites")),"Migrated sprites missing");
    assert(await evaluate("document.querySelectorAll('canvas').length")===1,"Unexpected extra canvas");
    const start=await evaluate("window.__camera");
    assert(start[0]===22&&start[1]===11.5&&start[2]===-1&&start[3]===0,"Migrated spawn incorrect");
    async function key(type,code) {
        await send("Input.dispatchKeyEvent",{type,code,key:code.startsWith("Key")?code.slice(3).toLowerCase():code,
            windowsVirtualKeyCode:code==="Escape"?27:code.charCodeAt(3)});
    }
    await key("keyDown","KeyQ"); await delay(100); await key("keyUp","KeyQ"); await delay(50);
    const left=await evaluate("window.__camera");
    assert(left[1]<start[1]&&left[0]===start[0]&&left[2]===start[2],"Navigation-map strafing failed");
    await delay(100);
    assert((await evaluate("window.__camera"))[1]===left[1],"Movement continued after key release");
    await send("Input.dispatchMouseEvent",{type:"mousePressed",x:960,y:540,button:"left",clickCount:1});
    await send("Input.dispatchMouseEvent",{type:"mouseReleased",x:960,y:540,button:"left",clickCount:1});
    await waitFor("document.pointerLockElement===document.querySelector('canvas')"); await delay(100);
    await evaluate("document.dispatchEvent(new MouseEvent('mousemove',{movementX:100}))"); await delay(100);
    const turned=await evaluate("window.__camera");
    let angle=Math.atan2(turned[3],turned[2])-Math.atan2(left[3],left[2]);
    angle=Math.atan2(Math.sin(angle),Math.cos(angle));
    assert(Math.abs(angle+.25)<.0001,"Mouse turning changed");
    await key("keyDown","Escape"); await key("keyUp","Escape");
    await waitFor("document.pointerLockElement===null");
    const sources=Object.fromEntries(["raycast","walls","sprites"].map(name=>
        [name,fs.readFileSync(`wwwroot/shaders/${name}.wgsl`,"utf8").replace(/^\uFEFF/,"")]));
    console.log("GPU CHECK",await evaluate(`(${checkGpu.toString()})(${JSON.stringify(sources)})`));
    if(process.env.WOLFNET_SCREENSHOT) {
        const screenshot=await send("Page.captureScreenshot",{format:"png"});
        fs.writeFileSync(process.env.WOLFNET_SCREENSHOT,Buffer.from(screenshot.data,"base64"));
    }
    console.log("PASS: browser startup, migrated scene, navigation movement, mouse capture, and Esc release.");
} finally {
    socket.close();
}
