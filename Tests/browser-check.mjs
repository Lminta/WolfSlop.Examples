// Launch the app and a Chromium browser with a remote-debugging port first.
// node Tests/browser-check.mjs 9243 http://127.0.0.1:5194
import fs from "node:fs";
import {checkGpu} from "./gpu-check.mjs";

const port = process.argv[2] ?? "9243";
const appUrl = process.argv[3] ?? "http://127.0.0.1:5194";
const missingTextures = process.argv.includes("--missing-textures");
const manifest = Object.values(JSON.parse(fs.readFileSync("WolfNET.Game/wwwroot/maps/level1.json", "utf8")).objects);
const expectedImages = new Set(manifest.flatMap(file => {
    const object = JSON.parse(fs.readFileSync(`WolfNET.Game/wwwroot/maps/${file}`, "utf8"));
    return [object.texture, object.floorTexture, object.ceilingTexture, ...Object.values(object.textures ?? {})].filter(Boolean);
}));
const target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`,{method:"PUT"})).json();
const socket = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve,reject)=>{socket.onopen=resolve;socket.onerror=reject;});
let id=0;
const pending=new Map(), logs=[];
const objectRequests=new Set();
let heldObjectRequest;
let failedTextures=0;
let failNextShowcase=false;
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
        if(failNextShowcase&&request.url.endsWith("/maps/showcase.json")) {
            failNextShowcase=false;
            void send("Fetch.fulfillRequest",{requestId,responseCode:404,body:""});
        } else if(request.url.includes("/textures/")) {
            failedTextures++;
            void send("Fetch.fulfillRequest",{requestId,responseCode:404,body:""});
        } else {
            objectRequests.add(request.url);
            if(request.url.endsWith("/sprite-groups/group-barrels-north-room.json")) heldObjectRequest=requestId;
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
        window.__cameraWrites=0;
        window.__duplicateFrames=0;
        window.__sceneTextures=new Set();
        window.__sceneBuffers=new Set();
        window.__destroyedSceneTextures=0;
        const createTexture=GPUDevice.prototype.createTexture;
        GPUDevice.prototype.createTexture=function(descriptor) {
            const texture=createTexture.call(this,descriptor);
            if(descriptor.label==='WolfNET Scene Textures') window.__sceneTextures.add(texture);
            return texture;
        };
        const destroyTexture=GPUTexture.prototype.destroy;
        GPUTexture.prototype.destroy=function() {
            if(window.__sceneTextures.delete(this)) window.__destroyedSceneTextures++;
            return destroyTexture.call(this);
        };
        const createBuffer=GPUDevice.prototype.createBuffer;
        GPUDevice.prototype.createBuffer=function(descriptor) {
            const buffer=createBuffer.call(this,descriptor);
            if(['WolfNET Wall Materials','WolfNET Sprites'].includes(descriptor.label))window.__sceneBuffers.add(buffer);
            return buffer;
        };
        const destroyBuffer=GPUBuffer.prototype.destroy;
        GPUBuffer.prototype.destroy=function() {
            window.__sceneBuffers.delete(this);
            return destroyBuffer.call(this);
        };
        const raf=window.requestAnimationFrame;
        window.requestAnimationFrame=function(callback) {
            return raf.call(this,timestamp=>{window.__frameStamp=timestamp;return callback(timestamp);});
        };
        GPUQueue.prototype.copyExternalImageToTexture=function(...args) {
            window.__copiedTextures++; return copy.apply(this,args);
        };
        GPUQueue.prototype.writeBuffer=function(buffer,offset,data,...rest) {
            if(buffer.label==='WolfNET Camera') {
                window.__camera=Array.from(new Float32Array(data.buffer??data,data.byteOffset??0,12));
                window.__cameraWrites++;
                if(window.__frameStamp!==undefined&&window.__lastCameraStamp===window.__frameStamp)window.__duplicateFrames++;
                window.__lastCameraStamp=window.__frameStamp;
            }
            return write.call(this,buffer,offset,data,...rest);
        };
    `});
    await send("Fetch.enable",{patterns:[{urlPattern:"*objects/*.json"},
        ...(missingTextures?[{urlPattern:"*textures/*"}]:[])]});
    await send("Emulation.setDeviceMetricsOverride",{width:1920,height:1080,deviceScaleFactor:1,mobile:false});
    await send("Page.navigate",{url:appUrl}); await send("Page.bringToFront");
    for(let i=0;i<200&&!heldObjectRequest;i++) await delay(100);
    assert(heldObjectRequest,"Last object was never requested");
    assert(!await evaluate("!!window.__camera"),"Game rendered before all definitions loaded");
    await send("Fetch.continueRequest",{requestId:heldObjectRequest});
    await waitFor("!!window.__camera");
    assert(objectRequests.size===manifest.length,"Not all map object definitions were loaded");
    assert([...objectRequests].every(url=>manifest.some(path=>new URL(path,appUrl+'/maps/level1.json').href===url)),"An object outside the selected map index was loaded");
    if(missingTextures) {
        assert(failedTextures===expectedImages.size,"Not all texture failures were exercised");
        assert(await evaluate("window.__copiedTextures")===0,"Missing image unexpectedly copied");
        console.log(`PASS: all ${failedTextures} unavailable textures use pink and the game still starts.`);
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
    async function captureMouse() {
        await send("Input.dispatchMouseEvent",{type:"mousePressed",x:960,y:540,button:"left",clickCount:1});
        await send("Input.dispatchMouseEvent",{type:"mouseReleased",x:960,y:540,button:"left",clickCount:1});
        await waitFor("document.pointerLockElement===document.querySelector('canvas')");
        await delay(75);
    }
    async function assertKeyboardInactive(label) {
        const before=await evaluate("window.__camera");
        const cancelled=await evaluate(`(()=>{
            const events=['KeyW','KeyA','KeyD'].map(code=>new KeyboardEvent('keydown',{code,bubbles:true,cancelable:true}));
            events.forEach(event=>window.dispatchEvent(event));
            return events.some(event=>event.defaultPrevented);
        })()`);
        assert(!cancelled,"Unlocked keyboard events were prevented");
        await delay(150);
        const after=await evaluate("window.__camera");
        assert(after.slice(0,6).every((value,i)=>value===before[i]),label);
        const cancelledUp=await evaluate(`['KeyW','KeyA','KeyD'].map(code=>{
            const event=new KeyboardEvent('keyup',{code,bubbles:true,cancelable:true});
            window.dispatchEvent(event);return event.defaultPrevented;
        }).some(Boolean)`);
        assert(!cancelledUp,"Unlocked keyup events were prevented");
    }
    await assertKeyboardInactive("Keyboard moved the player before capture");
    await captureMouse();
    await key("keyDown","KeyA");
    await waitFor(`window.__camera[1]<${start[1]}`);
    await key("keyUp","KeyA"); await delay(50);
    const left=await evaluate("window.__camera");
    assert(left[1]<start[1]&&left[0]===start[0]&&left[2]===start[2],"Navigation-map strafing failed");
    await delay(100);
    assert((await evaluate("window.__camera"))[1]===left[1],"Movement continued after key release");
    await key("keyDown","KeyD");
    await waitFor(`window.__camera[1]>${left[1]}`);
    await key("keyUp","KeyD"); await delay(50);
    const right=await evaluate("window.__camera");
    assert(right[1]>left[1]&&right[0]===left[0],"D did not strafe right");
    assert(right.slice(2,6).every((value,i)=>value===left[i+2]),"A/D strafing changed the viewing direction");
    await send("Input.dispatchMouseEvent",{type:"mousePressed",x:960,y:540,button:"left",clickCount:1});
    await send("Input.dispatchMouseEvent",{type:"mouseReleased",x:960,y:540,button:"left",clickCount:1});
    await waitFor("document.pointerLockElement===document.querySelector('canvas')"); await delay(100);
    // A small delta stays below the per-frame turn cap. Large-delta limits are
    // checked deterministically with the real controller in the .NET checks.
    const beforeTurn=await evaluate("window.__camera.slice()");
    await evaluate("document.dispatchEvent(new MouseEvent('mousemove',{movementX:1}))");
    await waitFor(`window.__camera[2]!==${beforeTurn[2]}||window.__camera[3]!==${beforeTurn[3]}`);
    const turned=await evaluate("window.__camera");
    let angle=Math.atan2(turned[3],turned[2])-Math.atan2(beforeTurn[3],beforeTurn[2]);
    angle=Math.atan2(Math.sin(angle),Math.cos(angle));
    assert(Math.abs(angle+.0025)<.0001,"Mouse turning changed below the configured rate limit");
    const beforePitch=await evaluate("window.__camera.slice()");
    await evaluate("document.dispatchEvent(new MouseEvent('mousemove',{movementY:-1}))");
    await waitFor(`window.__camera[8]>${beforePitch[8]}`);
    const pitched=await evaluate("window.__camera.slice()");
    assert(Math.abs(pitched[8]-beforePitch[8]-.0025)<.0001,"Moving the mouse up did not look up");
    assert(pitched.slice(0,6).every((value,i)=>value===beforePitch[i]),"Pitch changed yaw, position, or camera plane");
    await delay(150);
    assert((await evaluate("window.__camera[8]"))===pitched[8],"Pitch drifted after mouse movement stopped");
    await evaluate("document.dispatchEvent(new MouseEvent('mousemove',{movementY:1}))");
    await waitFor(`Math.abs(window.__camera[8]-${beforePitch[8]})<.0001`);
    const unlockRace=await evaluate(`(async()=>{
        const before=window.__camera.slice(2,9);
        document.dispatchEvent(new MouseEvent('mousemove',{movementX:50,movementY:50}));
        // Model the native lock disappearing before pointerlockchange reaches C#.
        Object.defineProperty(document,'pointerLockElement',{configurable:true,get:()=>null});
        try {
            document.dispatchEvent(new MouseEvent('mousemove',{movementX:1000,movementY:1000}));
            for(let frame=0;frame<3;frame++)await new Promise(requestAnimationFrame);
            return {before,after:window.__camera.slice(2,9)};
        } finally { delete document.pointerLockElement; }
    })()`);
    assert(unlockRace.after.every((value,i)=>value===unlockRace.before[i]),"Cursor restoration or queued delta changed direction during unlock race");
    async function assertStopped(label) {
        await delay(75);
        const stopped=await evaluate("window.__camera");
        // Simulate held-key repeats after the browser has swallowed a keyup.
        await evaluate(`['KeyW','KeyA','KeyD'].forEach(code=>window.dispatchEvent(new KeyboardEvent('keydown',{code,repeat:true,bubbles:true})))`);
        await delay(150);
        const after=await evaluate("window.__camera");
        assert(after.slice(0,6).every((value,i)=>value===stopped[i]),label);
        for(const code of ['KeyW','KeyA','KeyD']) await key('keyUp',code);
    }
    for(const code of ['KeyW','KeyA','KeyD']) await key('keyDown',code);
    await delay(75);
    await evaluate("document.exitPointerLock()");
    await waitFor("document.pointerLockElement===null");
    await assertStopped("Movement or turning stuck after pointer lock loss");
    await send("Input.dispatchMouseEvent",{type:"mousePressed",x:960,y:540,button:"left",clickCount:1});
    await send("Input.dispatchMouseEvent",{type:"mouseReleased",x:960,y:540,button:"left",clickCount:1});
    await waitFor("document.pointerLockElement===document.querySelector('canvas')");
    for(const code of ['KeyW','KeyA']) await key('keyDown',code);
    await delay(75);
    const beforeEscape=await evaluate(`(()=>{
        const direction=window.__camera.slice(2,6);
        document.dispatchEvent(new MouseEvent('mousemove',{movementX:50}));
        window.dispatchEvent(new KeyboardEvent('keydown',{code:'Escape',bubbles:true}));
        document.dispatchEvent(new MouseEvent('mousemove',{movementX:1000}));
        return direction;
    })()`);
    await key("keyUp","Escape");
    await waitFor("document.pointerLockElement===null");
    await assertStopped("Movement or turning stuck after Escape");
    assert((await evaluate("window.__camera.slice(2,6)")).every((value,i)=>value===beforeEscape[i]),"Escape changed the viewing direction while walking");
    await assertKeyboardInactive("Keyboard moved the player after Escape");
    // Programmatic release avoids the browser's Escape recapture cooldown in this check.
    await captureMouse();
    const resumedFrom=await evaluate("window.__camera");
    await key('keyDown','KeyW'); await delay(100); await key('keyUp','KeyW'); await delay(50);
    const resumed=await evaluate("window.__camera");
    assert(resumed[0]!==resumedFrom[0]||resumed[1]!==resumedFrom[1],"Fresh key press did not resume movement after recapture");
    await evaluate("document.exitPointerLock()");
    await waitFor("document.pointerLockElement===null");
    await assertKeyboardInactive("Keyboard moved the player after pointer lock loss");
    console.log("PASS: keyboard controls require mouse capture; Escape clears input and recapture restores controls.");
    // Exercise repeated captures, including event-only browser implementations.
    for(let cycle=0;cycle<6;cycle++) {
        if(cycle===3) await evaluate(`(()=>{
            const canvas=document.querySelector('canvas');
            const native=canvas.requestPointerLock;
            canvas.requestPointerLock=function(){native.call(this)?.catch(()=>{});};
        })()`);
        await captureMouse();
        await evaluate("document.exitPointerLock()");
        await waitFor("document.pointerLockElement===null");
        await delay(75);
        if(cycle===3) await evaluate("delete document.querySelector('canvas').requestPointerLock");
    }
    await captureMouse();
    const beforeStaleError=await evaluate("window.__camera");
    await evaluate(`document.dispatchEvent(new Event('pointerlockerror'));document.dispatchEvent(new MouseEvent('mousemove',{movementX:40}))`);
    await waitFor(`window.__camera[2]!==${beforeStaleError[2]}||window.__camera[3]!==${beforeStaleError[3]}`);
    const afterStaleError=await evaluate("window.__camera");
    assert(afterStaleError[2]!==beforeStaleError[2]||afterStaleError[3]!==beforeStaleError[3],"Delayed pointerlockerror disabled an active capture");
    await evaluate("document.exitPointerLock()");
    await waitFor("document.pointerLockElement===null"); await delay(75);
    await evaluate(`(()=>{
        const canvas=document.querySelector('canvas');
        const native=canvas.requestPointerLock;
        canvas.requestPointerLock=function(){return native.call(this).then(()=>new Promise(resolve=>window.__settleOldCapture=resolve));};
    })()`);
    await captureMouse();
    await evaluate("document.exitPointerLock()");
    await waitFor("document.pointerLockElement===null"); await delay(75);
    await evaluate("delete document.querySelector('canvas').requestPointerLock");
    await captureMouse();
    await evaluate("window.__settleOldCapture()"); await delay(75);
    assert(await evaluate("document.pointerLockElement===document.querySelector('canvas')"),"Old request completion released the new capture");
    await evaluate("document.exitPointerLock()");
    await waitFor("document.pointerLockElement===null"); await delay(75);
    await evaluate(`(()=>{
        window.__unhandledCaptureErrors=[];
        window.addEventListener('unhandledrejection',event=>window.__unhandledCaptureErrors.push(String(event.reason)));
        window.__captureRequestCount=0;
        const canvas=document.querySelector('canvas');
        canvas.requestPointerLock=function(){
            window.__captureRequestCount++;
            return new Promise((resolve,reject)=>window.__rejectCapture=reject);
        };
        canvas.click();canvas.click();
    })()`);
    assert(await evaluate("window.__captureRequestCount")===1,"Overlapping capture requests were sent");
    await evaluate("window.__rejectCapture(new DOMException('Simulated browser cooldown','NotAllowedError'))");
    await waitFor("document.querySelector('[role=status]').textContent.includes('Mouse capture was blocked')");
    assert(await evaluate("window.__unhandledCaptureErrors.length")===0,"Capture promise rejection was unhandled");
    await evaluate("delete document.querySelector('canvas').requestPointerLock");
    await captureMouse();
    assert(!await evaluate("document.querySelector('[role=status]').textContent.includes('Mouse capture was blocked')"),"Successful retry did not clear capture error");
    await evaluate("document.exitPointerLock()");
    await waitFor("document.pointerLockElement===null"); await delay(75);
    console.log("PASS: repeated recapture, event-only requests, stale errors, overlapping-click protection, and rejected-request recovery.");
    async function selectMap(name,spawn,keyboard=false) {
        const buttonExpression=`Array.from(document.querySelectorAll('.map-switcher button')).find(button=>button.textContent==='${name}')`;
        assert(!await evaluate(`(${buttonExpression}).disabled`),`${name} button unavailable`);
        const before=await evaluate("window.__cameraWrites");
        const destroyedBefore=await evaluate("window.__destroyedSceneTextures");
        const hadScene=await evaluate("window.__sceneTextures.size>0");
        if(keyboard) {
            await evaluate(`(${buttonExpression}).focus()`);
            await send("Input.dispatchKeyEvent",{type:"keyDown",key:"Enter",code:"Enter",windowsVirtualKeyCode:13,text:"\r",unmodifiedText:"\r"});
            await send("Input.dispatchKeyEvent",{type:"keyUp",key:"Enter",code:"Enter",windowsVirtualKeyCode:13});
        } else {
            const rect=await evaluate(`(()=>{const r=(${buttonExpression}).getBoundingClientRect();return{x:r.x+r.width/2,y:r.y+r.height/2};})()`);
            await send("Input.dispatchMouseEvent",{type:"mousePressed",...rect,button:"left",clickCount:1});
            await send("Input.dispatchMouseEvent",{type:"mouseReleased",...rect,button:"left",clickCount:1});
        }
        await waitFor(`window.__cameraWrites>${before}&&window.__camera[0]===${spawn[0]}&&window.__camera[1]===${spawn[1]}&&(${buttonExpression}).getAttribute('aria-pressed')==='true'`);
        assert(await evaluate("document.pointerLockElement===null"),"Map switch did not release the mouse");
        assert(await evaluate("window.__sceneTextures.size===1&&window.__sceneBuffers.size===2"),"Old scene GPU resources retained");
        if(hadScene) assert(await evaluate("window.__destroyedSceneTextures")>destroyedBefore,"Old textures were not destroyed");
    }
    await selectMap("Showcase",[6.5,10.5]);
    assert(logs.some(line=>line.includes("34x25")&&line.includes("27 sprites")),"Showcase was not prepared");
    assert(await evaluate("!!document.querySelector('.showcase-guide')"),"Showcase guide missing");
    if(process.env.WOLFNET_SCREENSHOT) {
        const screenshot=await send("Page.captureScreenshot",{format:"png"});
        fs.writeFileSync(process.env.WOLFNET_SCREENSHOT,Buffer.from(screenshot.data,"base64"));
    }
    await selectMap("Original",[22,11.5],true);
    await selectMap("Showcase",[6.5,10.5]);
    await selectMap("Original",[22,11.5]);
    await delay(150);
    assert(await evaluate("window.__duplicateFrames")===0,"Repeated switches duplicated animation loops");
    // A failed request must leave the controls available for retrying the old map.
    await send("Fetch.enable",{patterns:[{urlPattern:"*maps/showcase.json"}]});
    failNextShowcase=true;
    await evaluate("Array.from(document.querySelectorAll('.map-switcher button')).find(button=>button.textContent==='Showcase').click()");
    await waitFor("!!document.querySelector('.game-error')&&!Array.from(document.querySelectorAll('.map-switcher button')).some(button=>button.disabled)");
    assert(await evaluate("window.__sceneTextures.size===0&&window.__sceneBuffers.size===0"),"Failed switch retained scene resources");
    await send("Fetch.disable");
    await selectMap("Original",[22,11.5]);
    assert(!await evaluate("!!document.querySelector('.game-error')"),"Map retry did not clear error");
    console.log("PASS: map buttons, keyboard activation, repeated switches, frame cleanup, and failed-load recovery.");
    const sources=Object.fromEntries(["raycast","walls","sprites"].map(name=>
        [name,fs.readFileSync(`WolfNET.Game/wwwroot/shaders/${name}.wgsl`,"utf8").replace(/^\uFEFF/,"")]));
    console.log("GPU CHECK",await evaluate(`(${checkGpu.toString()})(${JSON.stringify(sources)})`));
    console.log("PASS: browser startup, migrated scene, navigation movement, mouse capture, and Esc release.");
} finally {
    socket.close();
}
