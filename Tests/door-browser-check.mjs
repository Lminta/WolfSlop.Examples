// node Tests/door-browser-check.mjs 9264 http://127.0.0.1:5214
import assert from 'node:assert/strict';
import fs from 'node:fs';
const port=process.argv[2]??'9264',base=process.argv[3]??'http://127.0.0.1:5214';
const target=await(await fetch(`http://127.0.0.1:${port}/json/new?about:blank`,{method:'PUT'})).json();
const socket=new WebSocket(target.webSocketDebuggerUrl);await new Promise((resolve,reject)=>{socket.onopen=resolve;socket.onerror=reject;});
let id=0;const pending=new Map(),errors=[],logs=[];
function send(method,params={}){return new Promise((resolve,reject)=>{const requestId=++id;pending.set(requestId,{resolve,reject});socket.send(JSON.stringify({id:requestId,method,params}));});}
socket.onmessage=event=>{const m=JSON.parse(event.data);if(m.id){const p=pending.get(m.id);pending.delete(m.id);m.error?p.reject(Error(JSON.stringify(m.error))):p.resolve(m.result);}else if(m.method==='Runtime.consoleAPICalled'){const line=m.params.args.map(a=>a.value??a.description??'').join(' ');logs.push(line);if(/Initialization failed|Frame failed|Uncaptured error/.test(line))errors.push(line);}else if(m.method==='Runtime.exceptionThrown')errors.push(JSON.stringify(m.params));};
async function evaluate(expression){const r=await send('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true});if(r.exceptionDetails)throw Error(JSON.stringify(r.exceptionDetails));return r.result.value;}
const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
async function waitFor(expression){for(let i=0;i<600;i++){assert.equal(errors.length,0,errors.join('\n'));if(await evaluate(expression))return;await delay(50);}throw Error('Timed out: '+expression+'\n'+logs.slice(-12).join('\n'));}
async function key(code,down=true,repeat=false){await evaluate(`window.dispatchEvent(new KeyboardEvent('${down?'keydown':'keyup'}',{code:'${code}',repeat:${repeat},bubbles:true,cancelable:true}))`);}
async function action(){await key('KeyF');await key('KeyF',false);}
async function capture(){const r=await evaluate('(()=>{const r=document.querySelector("canvas").getBoundingClientRect();return{x:r.x+r.width/2,y:r.y+r.height/2}})()');await send('Input.dispatchMouseEvent',{type:'mousePressed',...r,button:'left',clickCount:1});await send('Input.dispatchMouseEvent',{type:'mouseReleased',...r,button:'left',clickCount:1});await waitFor('document.pointerLockElement===document.querySelector("canvas")');}
try{
 await send('Page.enable');await send('Runtime.enable');
 await send('Page.addScriptToEvaluateOnNewDocument',{source:`
  window.__doors={};window.__samples=[];window.__sceneUploads=0;
  const originalFetch=window.fetch;
  window.fetch=async function(input,...args){const response=await originalFetch.call(this,input,...args);const url=String(input?.url??input);
   if(url.includes('graphics-settings.json')){const settings=await response.json();settings.width=640;settings.height=360;return new Response(JSON.stringify(settings),{headers:{'Content-Type':'application/json'}});}
   if(url.includes('maps/showcase.json')){const map=await response.json();Object.assign(map.playerStart,location.hash==='#glass'?{x:23.5,y:18.5,directionX:1,directionY:0}:{x:18.5,y:14.5,directionX:0,directionY:-1});return new Response(JSON.stringify(map),{headers:{'Content-Type':'application/json'}});}
   return response;
  };
  const create=GPUDevice.prototype.createTexture;
  GPUDevice.prototype.createTexture=function(d){if(d.label==='WolfNET Scene Textures')window.__sceneUploads++;return create.call(this,d);};
  const write=GPUQueue.prototype.writeBuffer;
  GPUQueue.prototype.writeBuffer=function(buffer,offset,data,...args){const raw=data.buffer??data,start=data.byteOffset??0;
   if(buffer.label==='WolfNET Camera')window.__camera=Array.from(new Float32Array(raw,start,12));
   if(buffer.label==='WolfNET World Map')window.__cells=Array.from(new Uint32Array(raw,start,data.byteLength/4));
   if(buffer.label==='WolfNET Sprites'){const u=new Uint32Array(raw,start,data.byteLength/4),f=new Float32Array(raw,start,data.byteLength/4);
    for(let i=0;i<u.length;i+=8)if((u[i+7]&3)!==0){window.__doors[u[i+3]]={width:f[i+4],kind:u[i+7],x:f[i],y:f[i+1]};if(u[i+3]===427)window.__samples.push(f[i+4]);}}
   return write.call(this,buffer,offset,data,...args);
  };
 `});
 await send('Emulation.setDeviceMetricsOverride',{width:1280,height:720,deviceScaleFactor:1,mobile:false});
 await send('Page.navigate',{url:base+'/?map=showcase'});await send('Page.bringToFront');await waitFor('!!window.__doors[427]');
 assert.equal(await evaluate('window.__doors[427].width'),1);
 await action();await delay(100);assert.equal(await evaluate('window.__doors[427].width'),1,'Unlocked F activated door');
 await capture();await action();await delay(100);assert.equal(await evaluate('window.__doors[427].width'),1,'Door activated from two tiles away');
 await key('KeyW');await waitFor('window.__camera[1]<13.6');await key('KeyW',false);await delay(100);
 const approach=await evaluate('window.__camera[1]');assert(approach>13.15,'Player entered closed door');
 if(process.env.WOLFNET_SCREENSHOT){const shot=await send('Page.captureScreenshot',{format:'png'});fs.writeFileSync(process.env.WOLFNET_SCREENSHOT+'-steel.png',Buffer.from(shot.data,'base64'));}
 await key('KeyF');await waitFor('window.__doors[427].width<.85&&window.__doors[427].width>.05');
 const partial=await evaluate('window.__doors[427].width');await key('KeyF',true,true);await key('KeyF',false);
 await waitFor('window.__doors[427].width===0');assert(partial>0,'Door opened without intermediate frames');
 assert.equal(await evaluate('window.__sceneUploads'),1,'Animation reuploaded textures');
 assert(await evaluate('window.__samples.some(v=>v>0&&v<1)'),'Door never drew partial geometry');
 assert.equal(await evaluate('(window.__cells[(12*34+18)*4+3]&1)'),0,'Open steel door still stops rays');
 await key('KeyW');await waitFor('window.__camera[1]<12.5');await key('KeyW',false);await delay(100);
 const inside=await evaluate('window.__camera[1]');assert(inside>12,'Door crossing test left its cell');
 await action();await delay(150);assert.equal(await evaluate('window.__doors[427].width'),0,'Door closed while player occupied it');
 await key('KeyW');await waitFor('window.__camera[1]<11.5');await key('KeyW',false);await delay(100);
 // Turn around with real controller mouse events, then close from the other side.
 for(let i=0;i<80;i++){await evaluate('document.dispatchEvent(new MouseEvent("mousemove",{movementX:35}))');await delay(18);const c=await evaluate('window.__camera');if(c[3]>.98)break;}
 await waitFor('window.__camera[3]>.95');
 await action();await waitFor('window.__doors[427].width>.05&&window.__doors[427].width<.95');await action();await waitFor('window.__doors[427].width===0');
 await action();await waitFor('window.__doors[427].width===1');
 assert.equal(await evaluate('window.__sceneUploads'),1,'Repeated interactions recreated scene textures');
 await evaluate('document.exitPointerLock()');await send('Page.navigate',{url:base+'/?map=showcase&doorCase=glass#glass'});await waitFor('!!window.__doors[637]&&window.__camera?.[0]===23.5&&window.__camera?.[1]===18.5');await capture();
 assert.equal(await evaluate('window.__doors[637].kind'),1,'Glass door is not transparent world geometry');
 assert.equal(await evaluate('(window.__cells[(18*34+24)*4+3]&1)'),0,'Closed glass door stops rays');
 const glassAlpha=await evaluate(`(async()=>{const image=new Image();image.src='textures/doors/glass-door.png';await image.decode();const c=new OffscreenCanvas(image.width,image.height),ctx=c.getContext('2d');ctx.drawImage(image,0,0);const a=ctx.getImageData(0,0,image.width,image.height).data;let low=0,partial=0,solid=0;for(let i=3;i<a.length;i+=4){if(a[i]<20)low++;else if(a[i]<240)partial++;else solid++;}return{low,partial,solid};})()`);
 assert(glassAlpha.low>0&&glassAlpha.partial>0&&glassAlpha.solid>0,'Glass image lacks transparent, partial, or opaque areas');
 if(process.env.WOLFNET_SCREENSHOT){const shot=await send('Page.captureScreenshot',{format:'png'});fs.writeFileSync(process.env.WOLFNET_SCREENSHOT+'-glass.png',Buffer.from(shot.data,'base64'));}
 await key('KeyV');await key('KeyV',false);await waitFor('window.__camera[0]<23.4');
 await action();await waitFor('window.__doors[637].width===0');
 await key('KeyV');await key('KeyV',false);await waitFor('Math.abs(window.__camera[0]-23.5)<.001');
 await key('KeyW');await waitFor('window.__camera[0]>24.6');await key('KeyW',false);
 assert.equal(errors.length,0,errors.join('\n'));
 console.log('PASS: adjacent F interaction, capture/range checks, animated open/close/reverse, repeat suppression, occupied-door safety, both approach sides and camera modes, stable textures, glass alpha/ray pass-through and traversable openings.');
}finally{await send('Page.close').catch(()=>{});socket.close();}
