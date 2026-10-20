// Start the game and a Chromium browser with WebGPU + remote debugging enabled.
// node Tests/third-person-browser-check.mjs 9261 http://127.0.0.1:5214
import assert from 'node:assert/strict';
import fs from 'node:fs';
const definition=JSON.parse(fs.readFileSync('WolfNET.Game/wwwroot/player-character.json','utf8'));
const port=process.argv[2]??'9261',base=process.argv[3]??'http://127.0.0.1:5214';
const target=await(await fetch(`http://127.0.0.1:${port}/json/new?about:blank`,{method:'PUT'})).json();
const socket=new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve,reject)=>{socket.onopen=resolve;socket.onerror=reject;});
let id=0;const pending=new Map(),errors=[],logs=[];
function send(method,params={}){return new Promise((resolve,reject)=>{const requestId=++id;pending.set(requestId,{resolve,reject});socket.send(JSON.stringify({id:requestId,method,params}));});}
socket.onmessage=event=>{
    const message=JSON.parse(event.data);
    if(message.id){const task=pending.get(message.id);pending.delete(message.id);message.error?task.reject(Error(JSON.stringify(message.error))):task.resolve(message.result);}
    else if(message.method==='Runtime.consoleAPICalled'){
        const text=message.params.args.map(a=>a.value??a.description??'').join(' ');logs.push(text);
        if(/Initialization failed|Frame failed|Uncaptured error/.test(text))errors.push(text);
    }else if(message.method==='Runtime.exceptionThrown')errors.push(JSON.stringify(message.params));
};
async function evaluate(expression){const r=await send('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true});if(r.exceptionDetails)throw Error(JSON.stringify(r.exceptionDetails));return r.result.value;}
const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
async function waitFor(expression){for(let i=0;i<600;i++){assert.equal(errors.length,0,errors.join('\n'));if(await evaluate(expression))return;await delay(100);}throw Error('Timed out: '+expression+'\n'+logs.slice(-20).join('\n'));}
async function key(code,down=true,repeat=false){await evaluate(`window.dispatchEvent(new KeyboardEvent('${down?'keydown':'keyup'}',{code:'${code}',repeat:${repeat},bubbles:true,cancelable:true}))`);}
async function idle(){await waitFor('window.__avatar?.frame===14');}
try{
    await send('Page.enable');await send('Runtime.enable');
    await send('Page.addScriptToEvaluateOnNewDocument',{source:`
        window.__avatar=null;window.__layers={};window.__seen=[];
        const frames=${JSON.stringify(definition.atlas.frames)};
        const originalFetch=window.fetch;
        window.fetch=async function(input,...args){
            const response=await originalFetch.call(this,input,...args);
            const url=String(input?.url??input);
            if(url.includes('graphics-settings.json')){const settings=await response.json();settings.width=640;settings.height=360;return new Response(JSON.stringify(settings),{headers:{'Content-Type':'application/json'}});}
            if(url.includes('maps/showcase.json')){const map=await response.json();Object.assign(map.playerStart,{x:20.5,y:20.5,directionX:0,directionY:-1});return new Response(JSON.stringify(map),{headers:{'Content-Type':'application/json'}});}
            return response;
        };
        const bitmap=window.createImageBitmap;
        window.createImageBitmap=function(...args){
            const result=bitmap.apply(this,args);
            if(args[0]?.src?.endsWith('/${definition.texture}'))return result.then(value=>{value.__frame=frames.findIndex(f=>f.x===args[1]&&f.y===args[2]&&f.width===args[3]&&f.height===args[4]);return value;});
            return result;
        };
        const copy=GPUQueue.prototype.copyExternalImageToTexture;
        GPUQueue.prototype.copyExternalImageToTexture=function(source,dest,...args){if(source.source.__frame!==undefined)window.__layers[dest.origin.z]=source.source.__frame;return copy.call(this,source,dest,...args);};
        const write=GPUQueue.prototype.writeBuffer;
        GPUQueue.prototype.writeBuffer=function(buffer,offset,data,...args){
            const raw=data.buffer??data,start=data.byteOffset??0;
            if(buffer.label==='WolfNET Camera')window.__camera=Array.from(new Float32Array(raw,start,12));
            if(buffer.label==='WolfNET Sprites'){
                const ints=new Uint32Array(raw,start,data.byteLength/4), floats=new Float32Array(raw,start,data.byteLength/4);window.__avatar=null;
                for(let i=0;i<ints.length;i+=8)if(ints[i+3]===0){
                    const frame=window.__layers[ints[i+2]];
                    window.__avatar={frame,x:floats[i],y:floats[i+1],sx:floats[i+4],sy:floats[i+5],z:floats[i+6]};
                    if(!window.__seen.includes(frame))window.__seen.push(frame);
                }
            }
            return write.call(this,buffer,offset,data,...args);
        };
    `});
    await send('Emulation.setDeviceMetricsOverride',{width:1280,height:720,deviceScaleFactor:1,mobile:false});
    await send('Page.navigate',{url:base+'/?map=showcase'});await send('Page.bringToFront');
    await waitFor('!!window.__camera');
    assert.equal(await evaluate('window.__avatar'),null,'Character appeared in first person');
    await key('KeyV');await key('KeyV',false);await delay(100);
    assert.equal(await evaluate('window.__avatar'),null,'V worked without mouse capture');
    const rect=await evaluate('(()=>{const r=document.querySelector("canvas").getBoundingClientRect();return{x:r.x+r.width/2,y:r.y+r.height/2}})()');
    await send('Input.dispatchMouseEvent',{type:'mousePressed',...rect,button:'left',clickCount:1});
    await send('Input.dispatchMouseEvent',{type:'mouseReleased',...rect,button:'left',clickCount:1});
    await waitFor('document.pointerLockElement===document.querySelector("canvas")');
    await key('KeyV');await key('KeyV',true,true);await key('KeyV',false);await idle();
    let avatar=await evaluate('window.__avatar'),camera=await evaluate('window.__camera');
    assert(Math.abs(avatar.x-20.5)<.001&&Math.abs(avatar.y-20.5)<.001,'Toggle moved the player');
    assert(Math.abs(camera[0]-20.5)<.001&&Math.abs(camera[1]-22)<.001,'Follow camera did not stay centered behind the player');
    const relative=[avatar.x-camera[0],avatar.y-camera[1]];
    const inverse=1/(camera[4]*camera[3]-camera[2]*camera[5]);
    const side=inverse*(camera[3]*relative[0]-camera[2]*relative[1]);
    const depth=inverse*(-camera[5]*relative[0]+camera[4]*relative[1]);
    const projectedX=.5*(1+side/depth);
    const projectedY=.5+(640/(2*Math.hypot(camera[4],camera[5]))/360)*(Math.tan(camera[8])+(camera[9]-avatar.z)/depth);
    assert(Math.abs(projectedX-.5)<.001&&projectedY>.65,'Character did not stay horizontally centered and low in the screen');
    const start=camera;
    await evaluate('document.dispatchEvent(new MouseEvent("mousemove",{movementX:80,bubbles:true}))');
    await waitFor('Math.abs(window.__camera[2])>.001');
    camera=await evaluate('window.__camera');avatar=await evaluate('window.__avatar');
    assert(Math.hypot(avatar.x-20.5,avatar.y-20.5)<.001&&Math.abs(camera[0]-start[0])>.001,'Mouse yaw did not orbit stationary player');
    await evaluate('document.dispatchEvent(new MouseEvent("mousemove",{movementY:50,bubbles:true}))');
    await waitFor('window.__camera[8]<-.001');
    assert((await evaluate('window.__camera'))[9]>.15,'Vertical look did not move camera height');
    await evaluate('document.dispatchEvent(new MouseEvent("mousemove",{movementY:-50,bubbles:true}))');
    const beforeSteer=await evaluate('window.__avatar');
    await evaluate('window.dispatchEvent(new KeyboardEvent("keydown",{code:"KeyW",bubbles:true}));document.dispatchEvent(new MouseEvent("mousemove",{movementX:20,bubbles:true}))');
    await waitFor(`Math.hypot(window.__avatar.x-${beforeSteer.x},window.__avatar.y-${beforeSteer.y})>.02`);
    await key('KeyW',false);await idle();
    const afterSteer=await evaluate('window.__avatar'),steerCamera=await evaluate('window.__camera');
    const steerDelta=[afterSteer.x-beforeSteer.x,afterSteer.y-beforeSteer.y];
    assert(Math.abs(steerDelta[0]*steerCamera[3]-steerDelta[1]*steerCamera[2])<.0001,'Third-person forward motion slipped sideways during a mouse turn');
    await key('KeyW');await waitFor('window.__seen.includes(0)&&window.__seen.includes(1)');await key('KeyW',false);await idle();
    for(const [codes,frames] of [[['KeyA'],[6,7]],[['KeyD'],[8,9]],[['KeyW','KeyA'],[2,3]],[['KeyW','KeyD'],[4,5]]]){
        for(const code of codes)await key(code);
        await waitFor(`window.__avatar&&${JSON.stringify(frames)}.includes(window.__avatar.frame)`);
        for(const code of codes)await key(code,false);
        await idle();
    }
    await key('AltLeft');await waitFor('window.__avatar?.frame===10');
    avatar=await evaluate('window.__avatar');assert(avatar.sy<.85,'Crouching art was enlarged to standing height');
    await key('KeyW');await waitFor('window.__avatar?.frame===11');await key('KeyW',false);
    await key('AltLeft',false);await idle();
    await key('Space');await waitFor('window.__seen.includes(12)&&window.__seen.includes(13)');await key('Space',false);await idle();
    if(process.env.WOLFNET_SCREENSHOT){const shot=await send('Page.captureScreenshot',{format:'png'});fs.writeFileSync(process.env.WOLFNET_SCREENSHOT,Buffer.from(shot.data,'base64'));}
    avatar=await evaluate('window.__avatar');
    await key('KeyV');await key('KeyV',false);await waitFor('window.__avatar===null');
    camera=await evaluate('window.__camera');assert(Math.hypot(camera[0]-avatar.x,camera[1]-avatar.y)<.001,'First-person camera did not return to player');
    assert.equal(errors.length,0,errors.join('\n'));
    console.log('PASS: V capture/repeat handling, first/third-person switch, stationary yaw/pitch orbit, straight movement during steering, idle, alternating walk legs, both strafes/diagonals, crouch, jump, and return to player position.');
}finally{await send('Page.close').catch(()=>{});socket.close();}
