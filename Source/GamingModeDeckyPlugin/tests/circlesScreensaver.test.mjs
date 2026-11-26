import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const source=fs.readFileSync(new URL('../screensaver/main.js',import.meta.url),'utf8');

// Core Starfield: a canvas-only screensaver. The fake context records what each frame draws.
function load(){
 const events={},log={fills:0,arcs:[],translates:[],strokes:0};let frame,cancelled=false;
 const ctx={setTransform(){},clearRect(){log.fills=0;log.arcs.length=0;log.translates.length=0;log.strokes=0},beginPath(){},arc(...a){log.arcs.push(a)},fill(){log.fills++},stroke(){log.strokes++},
  moveTo(){},lineTo(){},save(){},restore(){},translate(x,y){log.translates.push([x,y])},scale(){},set fillStyle(v){},set strokeStyle(v){},set lineWidth(v){},set lineCap(v){},set shadowColor(v){},set shadowBlur(v){}};
 const canvas={getContext:()=>ctx};const window={addEventListener:(name,fn)=>events[name]=fn};
 vm.runInNewContext(source,{document:{getElementById:id=>id==='circles'?canvas:null},window,navigator:{language:'en-US'},innerWidth:1280,innerHeight:720,devicePixelRatio:3,
  performance:{now:()=>0},matchMedia:()=>({matches:false}),requestAnimationFrame:fn=>{frame=fn;return 1},cancelAnimationFrame:()=>{cancelled=true},Intl,Date,Math});
 return{canvas,log,events,api:window.__playhubCircles,step:t=>frame(t),cancelled:()=>cancelled};
}

test('Core Starfield draws stars and the Core mark, and the mark moves',()=>{
 const s=load();
 assert.equal(s.canvas.width,2560);assert.equal(s.canvas.height,1440);
 s.step(0);s.step(20);
 assert.ok(s.log.fills>100,'stars are drawn');
 assert.ok(s.log.arcs.some(a=>a[2]===62),'the ring is drawn');
 const first=JSON.stringify(s.log.translates);
 s.step(40);
 assert.notEqual(JSON.stringify(s.log.translates),first,'the mark moves between frames');
});

test('the mark stays on screen and bounces off the walls',()=>{
 const s=load();
 let t=0;s.step(t);
 for(let i=0;i<3000;i++){t+=20;s.step(t);
  const [x,y]=s.log.translates.at(-1);
  assert.ok(x>=0&&x<=1280&&y>=0&&y<=720,`mark off screen at ${x},${y}`);}
});

test('the time is Steam\'s own clock without AM/PM, and the page releases its frame on exit',()=>{
 const s=load();
 s.api.update({clockText:'12:55 AM'});assert.equal(s.api.timeText(),'12:55');
 s.api.update({clockText:'21:07'});assert.equal(s.api.timeText(),'21:07');
 s.api.update({clockText:''});assert.match(s.api.timeText(),/^\d{1,2}:\d{2}$/);
 s.events.pagehide();assert.equal(s.cancelled(),true);
});
