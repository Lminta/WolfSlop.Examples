// node Tests/race-minimap-browser-check.mjs 9256 http://127.0.0.1:53721
import assert from 'node:assert/strict';
import fs from 'node:fs';
const port=process.argv[2]??'9256',base=process.argv[3]??'http://127.0.0.1:53721';
const target=await(await fetch(`http://127.0.0.1:${port}/json/new?about:blank`,{method:'PUT'})).json();
const socket=new WebSocket(target.webSocketDebuggerUrl);await new Promise((resolve,reject)=>{socket.onopen=resolve;socket.onerror=reject;});
let id=0;const pending=new Map(),errors=[];
function send(method,params={}){return new Promise((resolve,reject)=>{const requestId=++id,timer=setTimeout(()=>{pending.delete(requestId);reject(Error('CDP timeout: '+method));},60000);pending.set(requestId,{resolve,reject,timer});socket.send(JSON.stringify({id:requestId,method,params}));});}
socket.onmessage=event=>{const m=JSON.parse(event.data);if(m.id){const p=pending.get(m.id);if(!p)return;pending.delete(m.id);clearTimeout(p.timer);m.error?p.reject(Error(JSON.stringify(m.error))):p.resolve(m.result);}else if(m.method==='Runtime.exceptionThrown')errors.push(JSON.stringify(m.params));else if(m.method==='Runtime.consoleAPICalled'){const text=m.params.args.map(a=>a.value??a.description??'').join(' ');if(/Initialization failed|Frame failed|Uncaptured error/.test(text))errors.push(text);}};
async function evaluate(expression){const r=await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});if(r.exceptionDetails)throw Error(JSON.stringify(r.exceptionDetails));return r.result.value;}
const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
async function waitFor(expression){for(let i=0;i<600;i++){assert.deepEqual(errors,[]);if(await evaluate(expression))return;await delay(100);}throw Error('Timed out: '+expression);}
async function key(code,down=true){await evaluate(`window.dispatchEvent(new KeyboardEvent('${down?'keydown':'keyup'}',{code:'${code}',bubbles:true,cancelable:true}))`);}
async function capture(){const p=await evaluate(`(()=>{const r=document.querySelector('canvas').getBoundingClientRect();return{x:r.x+r.width/2,y:r.y+r.height/2};})()`);await send('Input.dispatchMouseEvent',{type:'mousePressed',...p,button:'left',clickCount:1});await send('Input.dispatchMouseEvent',{type:'mouseReleased',...p,button:'left',clickCount:1});await waitFor('document.pointerLockElement===document.querySelector("canvas")');}
const poseMatches=`(()=>{const g=document.querySelector('.race-minimap-course');if(!g||!window.__camera)return false;const m=g.transform.baseVal.consolidate().matrix,c=window.__camera,p=new DOMPoint(c[0],c[1]).matrixTransform(m),a=new DOMPoint(c[0]+c[2]*84,c[1]+c[3]*84).matrixTransform(m);return Math.hypot(p.x-50,p.y-70)<.01&&Math.hypot(a.x-50,a.y)<.01;})()`;
async function snapshot(name){const r=await send('Page.captureScreenshot',{format:'png'});fs.writeFileSync(`artifacts/race-minimap/${name}.png`,Buffer.from(r.data,'base64'));}
try{
 await send('Page.enable');await send('Runtime.enable');
 await send('Page.addScriptToEvaluateOnNewDocument',{source:`
  window.__camera=null;window.__info=null;window.__legacyMinimapDraws=0;
  const fetchOriginal=window.fetch;
  window.fetch=async function(input,...args){const response=await fetchOriginal.call(this,input,...args),url=String(input?.url??input);
   if(url.includes('graphics-settings.json')){const s=await response.json();s.width=640;s.height=360;s.targetFPS=30;if(location.hash==='#short-empty'){s.renderDistance=10;s.fogStartDistance=5;}return new Response(JSON.stringify(s),{headers:{'Content-Type':'application/json'}});}
   if(url.includes('maps/pine-loop.json')&&location.hash==='#short-empty'){
    const map=await response.json(),props=await Promise.all(Object.entries(map.objects).map(async([id,path])=>[+id,await(await fetchOriginal.call(this,new URL(path,new URL('maps/pine-loop.json',location.href)))).json()]));
    const sprites=new Set(props.filter(([,o])=>o.type==='sprite'||o.type==='spriteGroup').map(([id])=>id));map.worldMap=map.worldMap.map(column=>column.map(id=>sprites.has(id)?501:id));return new Response(JSON.stringify(map),{headers:{'Content-Type':'application/json'}});
   }return response;};
  const set=GPURenderPassEncoder.prototype.setPipeline;GPURenderPassEncoder.prototype.setPipeline=function(p){if(p.label==='WolfNET Minimap Pipeline')window.__legacyMinimapDraws++;return set.call(this,p);};
  const write=GPUQueue.prototype.writeBuffer;GPUQueue.prototype.writeBuffer=function(buffer,offset,data,...args){if(buffer.label==='WolfNET Camera')window.__camera=Array.from(new Float32Array(data.buffer??data,data.byteOffset??0,12));if(buffer.label==='WolfNET Map Info'&&offset===0)window.__info=Array.from(new Uint32Array(data.buffer??data,data.byteOffset??0,4));if(buffer.label==='WolfNET Map Info'&&offset===8&&window.__info)window.__info[2]=new Uint32Array(data.buffer??data,data.byteOffset??0,1)[0];return write.call(this,buffer,offset,data,...args);};
 `});
 await send('Emulation.setDeviceMetricsOverride',{width:1280,height:850,deviceScaleFactor:1,mobile:false});
 await send('Page.navigate',{url:base+'/?map=pine-loop'});await send('Page.bringToFront');
 await waitFor('!!document.querySelector(".race-minimap")&&window.__camera&&!document.querySelector(".loading-overlay")');await waitFor(poseMatches);
 fs.mkdirSync('artifacts/race-minimap',{recursive:true});await snapshot('start');
 const original=await evaluate(`({route:document.querySelector('.race-minimap-road').getAttribute('points'),transform:document.querySelector('.race-minimap-course').getAttribute('transform')})`);
 assert.equal(await evaluate('window.__legacyMinimapDraws'),0,'Race map drew old scenery dots');
 await capture();const before=await evaluate('window.__camera');
 await evaluate(`document.dispatchEvent(new MouseEvent('mousemove',{movementX:75}))`);
 await waitFor(`Math.abs(window.__camera[2]-${before[2]})>.01`);await waitFor(poseMatches);
 await key('KeyW');await delay(250);await key('KeyW',false);await delay(100);await waitFor(poseMatches);
 const moved=await evaluate('window.__camera');assert(Math.hypot(moved[0]-before[0],moved[1]-before[1])>.1,'Player did not move for minimap scroll test');
 const matrixBefore=await evaluate(`document.querySelector('.race-minimap-course').getAttribute('transform')`);
 await key('KeyV');await key('KeyV',false);await waitFor(`Math.hypot(window.__camera[0]-${moved[0]},window.__camera[1]-${moved[1]})>.1`);await delay(150);
 assert.equal(await evaluate(`document.querySelector('.race-minimap-course').getAttribute('transform')`),matrixBefore,'Third-person camera moved the player marker');await snapshot('rotated-third-person');
 await evaluate('document.exitPointerLock()');
 for(const [width,height] of [[1280,850],[1920,720],[537,850],[390,850],[390,600]]){
  await send('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:false});await delay(150);
  const layout=await evaluate(`(()=>{const panel=document.querySelector('.race-minimap'),r=panel.getBoundingClientRect(),v=document.querySelector('.race-minimap-viewport').getBoundingClientRect(),p=document.querySelector('.race-minimap-player').getBoundingClientRect(),c=document.querySelector('canvas').getBoundingClientRect(),style=getComputedStyle(panel);const intersects=s=>{const q=document.querySelector(s).getBoundingClientRect();return r.left<q.right&&r.right>q.left&&r.top<q.bottom&&r.bottom>q.top;};return{bounds:r.left>=c.left&&r.top>=c.top&&r.right<=c.right&&r.bottom<=c.bottom,square:r.width>0&&Math.abs(r.width-r.height)<1,overlap:intersects('.map-switcher')||intersects('.controls-hint'),inset:[r.left-c.left,r.top-c.top],anchor:[(p.x+p.width/2-v.x)/v.width,(p.y+p.height/2-v.y)/v.height],pointer:style.pointerEvents,background:style.backgroundColor,border:style.borderTopWidth,roadWidth:getComputedStyle(document.querySelector('.race-minimap-road')).strokeWidth};})()`);
  assert(layout.bounds&&layout.square&&!layout.overlap,'Minimap overlaps panels or leaves canvas at '+width+'x'+height);assert(Math.abs(layout.anchor[0]-.5)<.005&&Math.abs(layout.anchor[1]-.7)<.005,'Player marker moved on resize');assert.equal(layout.pointer,'none');assert.deepEqual(layout.inset,[12,12],'Minimap is not pinned to canvas');assert.equal(layout.background,'rgba(0, 0, 0, 0)');assert.equal(layout.border,'0px');assert.equal(layout.roadWidth,'8px');
 }
 console.log('PASS local minimap: rotation, movement, first/third-person anchoring,120-block window, and responsive noninteractive layout.');
 await send('Emulation.setDeviceMetricsOverride',{width:1280,height:850,deviceScaleFactor:1,mobile:false});
 await send('Page.navigate',{url:base+'/?map=pine-loop&minimapFixture=short-empty#short-empty'});await waitFor('window.__camera?.[10]===10&&!!document.querySelector(".race-minimap")&&!document.querySelector(".loading-overlay")');await waitFor(poseMatches);
 assert.equal(await evaluate('window.__info[2]'),0,'Empty scenery fixture still uploads sprites');
 assert.equal(await evaluate(`document.querySelector('.race-minimap-road').getAttribute('points')`),original.route,'Scenery/fog changed route geometry');
 assert.equal(await evaluate(`document.querySelector('.race-minimap-course').getAttribute('transform')`),original.transform,'Render distance changed minimap zoom');
 assert.equal(await evaluate('window.__legacyMinimapDraws'),0);await snapshot('short-distance-no-scenery');
 await evaluate(`Array.from(document.querySelectorAll('.map-switcher button')).find(b=>b.textContent.trim()==='Showcase').click()`);
 await waitFor('window.__info?.[0]===34&&!document.querySelector(".loading-overlay")&&window.__legacyMinimapDraws>0');
 assert.equal(await evaluate('document.querySelectorAll(".race-minimap").length'),0,'Race overlay survived scene disposal');await delay(200);assert.deepEqual(errors,[]);
 console.log('PASS minimap isolation: fog/render distance/scenery do not change the map; leaving restores the exploration minimap.');
}finally{await send('Page.close').catch(()=>{});socket.close();for(const p of pending.values())clearTimeout(p.timer);}
