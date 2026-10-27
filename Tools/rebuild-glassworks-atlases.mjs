import fs from 'node:fs';
import zlib from 'node:zlib';
// Run from the repository root: node Tools/rebuild-glassworks-atlases.mjs
// Reads the preserved generated sheets; no generation API or dependencies needed.
const directory='WolfNET.Examples/wwwroot/textures/glassworks';
fs.mkdirSync('artifacts',{recursive:true});
function decode(filename){
 const file=fs.readFileSync(filename);let width,height,type,depth,parts=[];
 for(let p=8;p<file.length;){const n=file.readUInt32BE(p),tag=file.toString('ascii',p+4,p+8),data=file.subarray(p+8,p+8+n);if(tag==='IHDR'){width=data.readUInt32BE(0);height=data.readUInt32BE(4);depth=data[8];type=data[9];}if(tag==='IDAT')parts.push(data);p+=n+12;}
 if(depth!==8||![2,6].includes(type))throw Error(`Unsupported PNG ${type}/${depth}`);
 const bpp=type===6?4:3,stride=width*bpp,raw=zlib.inflateSync(Buffer.concat(parts)),bytes=Buffer.alloc(height*stride);
 const paeth=(a,b,c)=>{const p=a+b-c,pa=Math.abs(p-a),pb=Math.abs(p-b),pc=Math.abs(p-c);return pa<=pb&&pa<=pc?a:pb<=pc?b:c;};
 for(let y=0;y<height;y++){const f=raw[y*(stride+1)];for(let x=0;x<stride;x++){const p=y*stride+x,a=x>=bpp?bytes[p-bpp]:0,b=y?bytes[p-stride]:0,c=y&&x>=bpp?bytes[p-stride-bpp]:0;bytes[p]=(raw[y*(stride+1)+1+x]+[0,a,b,(a+b)>>1,paeth(a,b,c)][f])&255;}}
 const rgba=Buffer.alloc(width*height*4);for(let i=0;i<width*height;i++){rgba[i*4]=bytes[i*bpp];rgba[i*4+1]=bytes[i*bpp+1];rgba[i*4+2]=bytes[i*bpp+2];rgba[i*4+3]=bpp===4?bytes[i*bpp+3]:255;}
 return {width,height,rgba};
}
const crcTable=Array.from({length:256},(_,i)=>{let c=i;for(let k=0;k<8;k++)c=c&1?0xedb88320^(c>>>1):c>>>1;return c>>>0;});
function chunk(type,data){const tag=Buffer.from(type),body=Buffer.concat([tag,data]);let crc=0xffffffff;for(const b of body)crc=crcTable[(crc^b)&255]^(crc>>>8);const result=Buffer.alloc(data.length+12);result.writeUInt32BE(data.length);body.copy(result,4);result.writeUInt32BE((crc^0xffffffff)>>>0,result.length-4);return result;}
function encode(filename,{width,height,rgba}){const header=Buffer.alloc(13);header.writeUInt32BE(width);header.writeUInt32BE(height,4);header[8]=8;header[9]=6;const lines=Buffer.alloc(height*(width*4+1));for(let y=0;y<height;y++)rgba.copy(lines,y*(width*4+1)+1,y*width*4,(y+1)*width*4);fs.writeFileSync(filename,Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]),chunk('IHDR',header),chunk('IDAT',zlib.deflateSync(lines)),chunk('IEND',Buffer.alloc(0))]));}
function components(image){
 const {width,height,rgba}=image,labels=new Int32Array(width*height),queue=new Int32Array(width*height),result=[];
 for(let seed=0;seed<labels.length;seed++){
  if(labels[seed]||rgba[seed*4+3]<64)continue;
  const id=result.length+1;let count=1,left=width,top=height,right=0,bottom=0,sx=0,sy=0;queue[0]=seed;labels[seed]=id;
  for(let c=0;c<count;c++){const p=queue[c],x=p%width,y=(p/width)|0;sx+=x;sy+=y;left=Math.min(left,x);top=Math.min(top,y);right=Math.max(right,x);bottom=Math.max(bottom,y);
   for(let oy=-1;oy<=1;oy++)for(let ox=-1;ox<=1;ox++){const nx=x+ox,ny=y+oy,n=ny*width+nx;if(nx>=0&&nx<width&&ny>=0&&ny<height&&!labels[n]&&rgba[n*4+3]>=64){labels[n]=id;queue[count++]=n;}}
  }
  result.push({id,count,left,top,right,bottom,cx:sx/count,cy:sy/count});
 }
 return {labels,components:result};
}
function extract(image,labels,c){
 const width=c.right-c.left+1,height=c.bottom-c.top+1,rgba=Buffer.alloc(width*height*4);
 for(let y=0;y<height;y++)for(let x=0;x<width;x++){
  const sx=x+c.left,sy=y+c.top,p=sy*image.width+sx;let keep=labels[p]===c.id;
  // Retain the generated antialias fringe, without unrelated transparent noise.
  if(!keep&&image.rgba[p*4+3])for(let oy=-1;oy<=1&&!keep;oy++)for(let ox=-1;ox<=1;ox++){const nx=sx+ox,ny=sy+oy;if(nx>=0&&nx<image.width&&ny>=0&&ny<image.height&&labels[ny*image.width+nx]===c.id){keep=true;break;}}
  if(keep)image.rgba.copy(rgba,(y*width+x)*4,p*4,p*4+4);
 }
 return {width,height,rgba};
}
function place(target,source,slot){
 const scale=Math.min(448/source.height,488/source.width),w=Math.round(source.width*scale),h=Math.round(source.height*scale),dx=(slot%4)*512+Math.floor((512-w)/2),dy=Math.floor(slot/4)*512+500-h;
 for(let y=0;y<h;y++)for(let x=0;x<w;x++){const sx=Math.min(source.width-1,Math.floor((x+.5)*source.width/w)),sy=Math.min(source.height-1,Math.floor((y+.5)*source.height/h)),p=(sy*source.width+sx)*4;source.rgba.copy(target.rgba,((dy+y)*target.width+dx+x)*4,p,p+4);}
}
const manifest={};
// Classify the pump by its wheel/gauge silhouette rather than assumed grid labels:
// the front diagonals are exchanged, and source 3 is the broad-wheel rear view,
// while source 4 is the narrower rear-right view. Side views remain 2 and 6.
const sourceOrder={bench:[0,1,2,3,4,5,6,7],pump:[0,7,2,4,3,5,6,1]};
for(const name of ['bench','pump']){
 const image=decode(`${directory}/${name}-directional-source.png`),segmentation=components(image);
 const main=segmentation.components.filter(c=>c.count>10000).sort((a,b)=>(a.cy<image.height/2?0:1)-(b.cy<image.height/2?0:1)||a.cx-b.cx);
 if(main.length!==8)throw Error(`${name}: expected eight complete connected objects, found ${main.length}`);
 const atlas={width:2048,height:1024,rgba:Buffer.alloc(2048*1024*4)};
 manifest[name]={sourceOrder:sourceOrder[name],components:main};
 for(let i=0;i<main.length;i++){const sprite=extract(image,segmentation.labels,main[sourceOrder[name][i]]);place(atlas,sprite,i);encode(`artifacts/${name}-isolated-${i}.png`,sprite);}
 encode(`${directory}/${name}-directional-atlas-v2.png`,atlas);
 console.log(name,main.map((c,i)=>({view:i,bounds:[c.left,c.top,c.right-c.left+1,c.bottom-c.top+1],pixels:c.count})));
}
fs.writeFileSync(`${directory}/directional-atlas-layout.json`,JSON.stringify(manifest,null,2)+'\n');
