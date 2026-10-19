// Start the game and Chromium with WebGPU and remote debugging enabled.
// node Tests/atlas-browser-check.mjs 9258 http://127.0.0.1:5214
import assert from 'node:assert/strict';
import fs from 'node:fs';
const showcase=JSON.parse(fs.readFileSync('WolfNET.Game/wwwroot/maps/showcase.json','utf8'));
const carX=showcase.worldMap.findIndex(column=>column.includes(213));
const carY=showcase.worldMap[carX].indexOf(213);
const group=JSON.parse(fs.readFileSync('WolfNET.Game/wwwroot/objects/sprite-groups/group-showcase-directional-car.json','utf8'));
const member=group.sprites[0];
const port = process.argv[2] ?? '9258', base = process.argv[3] ?? 'http://127.0.0.1:5214';
const target = await (await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, {method:'PUT'})).json();
const socket = new WebSocket(target.webSocketDebuggerUrl);
await new Promise((resolve,reject) => {socket.onopen=resolve;socket.onerror=reject;});
let id=0;
const pending=new Map(), errors=[];
function send(method,params={}) {
    return new Promise((resolve,reject) => {const requestId=++id;pending.set(requestId,{resolve,reject});socket.send(JSON.stringify({id:requestId,method,params}));});
}
socket.onmessage=event=>{
    const message=JSON.parse(event.data);
    if(message.id) {
        const request=pending.get(message.id);pending.delete(message.id);
        message.error?request.reject(Error(JSON.stringify(message.error))):request.resolve(message.result);
    } else if(message.method==='Runtime.consoleAPICalled') {
        const text=message.params.args.map(arg=>arg.value??arg.description??'').join(' ');
        if(/Initialization failed|Frame failed|Uncaptured error/.test(text))errors.push(text);
    } else if(message.method==='Runtime.exceptionThrown')errors.push(JSON.stringify(message.params));
};
async function evaluate(expression) {
    const result=await send('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true});
    if(result.exceptionDetails)throw Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
}
async function waitFor(expression) {
    for(let i=0;i<600;i++) {
        assert.equal(errors.length,0,errors.join('\n'));
        if(await evaluate(expression))return;
        await new Promise(resolve=>setTimeout(resolve,100));
    }
    throw Error('Timed out: '+expression);
}
try {
    await send('Runtime.enable');await send('Page.enable');
    await send('Page.addScriptToEvaluateOnNewDocument',{source:`
        window.__atlasCrops=[];window.__car=null;
        const fetchOriginal=window.fetch;
        window.fetch=async function(input,...args){
            const response=await fetchOriginal.call(this,input,...args);
            if(String(input?.url??input).includes('graphics-settings.json')){
                const settings=await response.json();settings.width=640;settings.height=360;
                return new Response(JSON.stringify(settings),{headers:{'Content-Type':'application/json'}});
            }
            if(String(input?.url??input).includes('maps/showcase.json')){
                const map=await response.json();
                Object.assign(map.playerStart,{x:${carX+.5},y:${carY+2.5},directionX:0,directionY:-1});
                return new Response(JSON.stringify(map),{headers:{'Content-Type':'application/json'}});
            }
            return response;
        };
        const bitmap=window.createImageBitmap;
        window.createImageBitmap=function(...args){
            if(args[0]?.src?.endsWith('/textures/race/car.png'))window.__atlasCrops.push(args.slice(1,5));
            return bitmap.apply(this,args);
        };
        const write=GPUQueue.prototype.writeBuffer;
        GPUQueue.prototype.writeBuffer=function(buffer,offset,data,...rest){
            const raw=data.buffer??data, start=data.byteOffset??0;
            if(buffer.label==='WolfNET Camera')window.__camera=Array.from(new Float32Array(raw,start,12));
            if(buffer.label==='WolfNET Sprites'){
                const ints=new Uint32Array(raw,start,data.byteLength/4);
                const floats=new Float32Array(raw,start,data.byteLength/4);
                for(let i=0;i<ints.length;i+=8)if(ints[i+2]>=32&&ints[i+2]<40)
                    window.__car={x:floats[i],y:floats[i+1],layer:ints[i+2],sx:floats[i+4],sy:floats[i+5],z:floats[i+6]};
            }
            return write.call(this,buffer,offset,data,...rest);
        };
    `});
    await send('Emulation.setDeviceMetricsOverride',{width:1280,height:720,deviceScaleFactor:1,mobile:false});
    await send('Page.navigate',{url:base+'/?map=showcase'});await send('Page.bringToFront');
    await waitFor('!!window.__car');
    const definition=JSON.parse(fs.readFileSync('WolfNET.Game/wwwroot/objects/sprites/sprite-directional-car.json','utf8'));
    const crops=await evaluate('window.__atlasCrops');
    for(const frame of definition.atlas.frames)
        assert(crops.some(c=>c.every((v,i)=>v===[frame.x,frame.y,frame.width,frame.height][i])), 'Atlas rectangle was not cropped');
    const clipped=await evaluate(`(async()=>{
        const image=new Image();image.src='textures/race/car.png';await image.decode();
        return ${JSON.stringify(definition.atlas.frames)}.map(frame=>{
            const canvas=new OffscreenCanvas(frame.width,frame.height),context=canvas.getContext('2d');
            context.drawImage(image,frame.x,frame.y,frame.width,frame.height,0,0,frame.width,frame.height);
            const rgba=context.getImageData(0,0,frame.width,frame.height).data;
            let visible=0,edge=0;
            for(let y=0;y<frame.height;y++)for(let x=0;x<frame.width;x++){
                if(rgba[(y*frame.width+x)*4+3]>=64){
                    visible++;
                    if(x===0||y===0||x===frame.width-1||y===frame.height-1)edge++;
                }
            }
            return visible===0||edge>0;
        });
    })()`);
    assert(clipped.every(value=>!value),'Atlas slice is empty or cuts visible pixels at its edges');
    let car=await evaluate('window.__car');
    assert.equal(car.layer,36,'South observer must see rear view');
    const rear=definition.atlas.frames[4];
    assert(Math.abs(car.sx-member.scale.x*rear.width/rear.height)<.00001 && Math.abs(car.sy-member.scale.y)<.00001,'Atlas aspect/group scale incorrect');
    assert(Math.abs(car.z-member.offset.z)<.00001,'Group elevation lost');
    const rect=await evaluate('(()=>{const r=document.querySelector("canvas").getBoundingClientRect();return{x:r.x+r.width/2,y:r.y+r.height/2}})()');
    await send('Input.dispatchMouseEvent',{type:'mousePressed',...rect,button:'left',clickCount:1});
    await send('Input.dispatchMouseEvent',{type:'mouseReleased',...rect,button:'left',clickCount:1});
    await waitFor('document.pointerLockElement===document.querySelector("canvas")');
    await evaluate('window.dispatchEvent(new KeyboardEvent("keydown",{code:"KeyD",bubbles:true}))');
    await waitFor(`Math.abs(window.__camera[0]-${carX+.5})>1.4`);
    await evaluate('window.dispatchEvent(new KeyboardEvent("keyup",{code:"KeyD",bubbles:true}))');
    await new Promise(resolve=>setTimeout(resolve,100));
    const camera=await evaluate('window.__camera');car=await evaluate('window.__car');
    const angle=Math.atan2(camera[1]-(carY+.5),camera[0]-(carX+.5))+Math.PI/2;
    const view=((Math.floor(angle/(Math.PI*2)*8+.5)%8)+8)%8;
    assert.equal(car.layer,32+view,'Movement did not select the expected directional layer');
    assert.notEqual(car.layer,36,'Strafing did not change the view');
    await evaluate('window.dispatchEvent(new MouseEvent("mousemove",{movementX:40,bubbles:true}))');
    await new Promise(resolve=>setTimeout(resolve,100));
    assert.equal((await evaluate('window.__car')).layer,car.layer,'Mouse look changed the view without player translation');
    if(process.env.WOLFNET_SCREENSHOT){const shot=await send('Page.captureScreenshot',{format:'png'});fs.writeFileSync(process.env.WOLFNET_SCREENSHOT,Buffer.from(shot.data,'base64'));}
    assert.equal(errors.length,0,errors.join('\n'));
    console.log('PASS: live atlas crops/mips, aspect ratio, group scale/elevation, rear view, movement-driven view selection, and stable view during mouse look.');
} finally {
    await send('Page.close').catch(()=>{});socket.close();
}
