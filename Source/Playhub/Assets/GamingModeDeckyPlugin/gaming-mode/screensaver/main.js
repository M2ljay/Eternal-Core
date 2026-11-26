"use strict";
// Core Starfield (Mark III). The Core mark bounces DVD-style through a slow flight of
// stars. Its ring fills to show how close each bounce came to a corner; a true corner
// flashes the mark and rushes the stars. When the minute changes, stars gather into the
// time, hold and scatter. Nothing stays lit in one place, so it is safe on an OLED.
// The plugin pushes the locale and Steam's own clock text through
// window.__playhubCircles.update (the name is kept so older plugin code still reaches it).
(() => {
 const canvas=document.getElementById('circles'),ctx=canvas.getContext('2d');
 const CRIMSON='215,38,61',STEEL='201,209,217',WHITE='230,236,242',TAU=Math.PI*2;
 const reduced=matchMedia('(prefers-reduced-motion: reduce)').matches,start=performance.now();
 let state={locale:navigator.language},width=0,height=0,frame=0,stopped=false,last=null;
 let seed=23;const rand=()=>{seed=(seed*16807)%2147483647;return(seed-1)/2147483646;};
 const stars=Array.from({length:420},()=>{const c=rand();return{x:rand(),y:rand(),z:rand(),c:c<.04?CRIMSON:c<.3?WHITE:STEEL};});
 const logo={x:null,y:0,vx:.8,vy:.6};
 let zoff=0,boost=0,flash=0,fill=0,shown=0,cons=null,lastMinute=null,firstFormed=false;

 function resize(){width=innerWidth;height=innerHeight;const scale=Math.min(devicePixelRatio||1,2);canvas.width=Math.round(width*scale);canvas.height=Math.round(height*scale);ctx.setTransform(scale,0,0,scale,0,0);}
 function dot(x,y,r,color,alpha){ctx.fillStyle='rgba('+color+','+alpha+')';ctx.beginPath();ctx.arc(x,y,r,0,TAU);ctx.fill();}

 // The time as Steam shows it, without AM/PM; the date is not shown.
 function timeText(){
  const steam=(state.clockText||'').replace(/[^\d:.\s]/g,'').trim();
  if(/\d/.test(steam))return steam;
  return new Intl.DateTimeFormat(state.locale||undefined,{hour:'numeric',minute:'2-digit'}).formatToParts(new Date()).filter(p=>p.type!=='dayPeriod').map(p=>p.value).join('').trim();
 }

 function form(now){
  if(!width||typeof document.createElement!=='function')return;
  const text=timeText(),size=Math.round(height*.3),off=document.createElement('canvas');
  off.width=Math.ceil(width);off.height=Math.ceil(height);const o=off.getContext('2d');if(!o)return;
  o.fillStyle='#fff';o.textAlign='center';o.textBaseline='middle';o.font='500 '+size+'px "Motiva Sans", Helvetica, sans-serif';o.fillText(text,width/2,height/2);
  const data=o.getImageData(0,0,off.width,off.height).data,step=Math.max(3,Math.round(size/18)),points=[];
  for(let y=0;y<off.height;y+=step)for(let x=0;x<off.width;x+=step)if(data[(y*off.width+x)*4+3]>140)points.push([x,y]);
  for(let i=points.length-1;i>0;i--){const j=Math.floor(Math.random()*(i+1));[points[i],points[j]]=[points[j],points[i]];}
  cons={start:now,points:points.slice(0,360).map(([x,y])=>{const a=Math.random()*TAU,r=Math.max(width,height)*(.6+Math.random()*.5),c=Math.random();
   return{tx:x,ty:y,sx:Math.random()*width,sy:Math.random()*height,ex:width/2+Math.cos(a)*r,ey:height/2+Math.sin(a)*r,r:.8+Math.random()*1.2,p:Math.random()*TAU,c:c<.05?CRIMSON:c<.35?WHITE:STEEL};})};
 }

 function constellation(now){
  const minute=new Date().getMinutes();
  if(lastMinute===null)lastMinute=minute;
  if(!firstFormed&&now-start>2500){firstFormed=true;form(now);}
  else if(minute!==lastMinute){lastMinute=minute;form(now);}
  if(!cons)return;
  const t=(now-cons.start)/1000,ease=v=>v<.5?2*v*v:1-Math.pow(-2*v+2,2)/2;
  if(t>8.6){cons=null;return;}
  for(const s of cons.points){let x,y,a;
   if(reduced){if(t>6.2)continue;x=s.tx;y=s.ty;a=.85;}
   else if(t<2.4){const k=ease(t/2.4);x=s.sx+(s.tx-s.sx)*k;y=s.sy+(s.ty-s.sy)*k;a=.25+.6*k;}
   else if(t<6.2){x=s.tx;y=s.ty;a=.7+.25*Math.sin(now/1000*2+s.p);}
   else{const k=Math.min(1,(t-6.2)/2.4),e=k*k;x=s.tx+(s.ex-s.tx)*e;y=s.ty+(s.ey-s.ty)*e;a=.85*(1-k);}
   dot(x,y,s.r,s.c,a);}
 }

 // The Core mark on a 140 unit box: unlit ring, crimson charge from nine o'clock, eight
 // steel ticks, steel core with a crimson centre.
 function mark(cx,cy,diameter,now){
  const k=diameter/140,glow=flash>now?(flash-now)/1400:0;
  ctx.save();ctx.translate(cx,cy);ctx.scale(k,k);
  if(glow>0){ctx.shadowColor='rgba(255,255,255,'+(.85*glow)+')';ctx.shadowBlur=32*glow;}
  ctx.lineCap='round';ctx.lineWidth=9;
  ctx.strokeStyle='#3A3F46';ctx.beginPath();ctx.arc(0,0,62,0,TAU);ctx.stroke();
  if(shown>.003){ctx.strokeStyle='#D7263D';ctx.beginPath();ctx.arc(0,0,62,Math.PI,Math.PI+TAU*Math.min(1,shown));ctx.stroke();}
  ctx.strokeStyle='#C9D1D9';ctx.lineWidth=5;
  for(let i=0;i<8;i++){const a=i*Math.PI/4,c=Math.cos(a),s=Math.sin(a);ctx.beginPath();ctx.moveTo(c*38,s*38);ctx.lineTo(c*48,s*48);ctx.stroke();}
  ctx.fillStyle='#C9D1D9';ctx.beginPath();ctx.arc(0,0,22,0,TAU);ctx.fill();
  ctx.fillStyle='#D7263D';ctx.beginPath();ctx.arc(0,0,9,0,TAU);ctx.fill();
  ctx.restore();
 }

 function bounce(dt,now){
  const diameter=Math.max(48,Math.min(width,height)*.16),r=diameter/2,speed=Math.min(width,height)*.26;
  if(logo.x===null){logo.x=width*.2+r;logo.y=height*.25+r;}
  logo.x+=logo.vx*speed*dt;logo.y+=logo.vy*speed*dt;let hx=false,hy=false;
  if(logo.x<=r){logo.x=r;logo.vx=Math.abs(logo.vx);hx=true;}else if(logo.x>=width-r){logo.x=Math.max(r,width-r);logo.vx=-Math.abs(logo.vx);hx=true;}
  if(logo.y<=r){logo.y=r;logo.vy=Math.abs(logo.vy);hy=true;}else if(logo.y>=height-r){logo.y=Math.max(r,height-r);logo.vy=-Math.abs(logo.vy);hy=true;}
  if(hx||hy){
   // How close to a corner: 1 at a corner, 0 in the middle of the wall that was hit.
   const sx=Math.max(1,width-diameter),sy=Math.max(1,height-diameter),px=logo.x-r,py=logo.y-r;
   const v=hx&&hy?1:hx?1-Math.min(py,sy-py)/(sy/2):1-Math.min(px,sx-px)/(sx/2);
   fill=Math.max(0,Math.min(1,v));if(fill>.97){flash=now+1400;boost=now+1800;}
  }
  shown=reduced?fill:shown+(fill-shown)*Math.min(1,dt*5);
  mark(logo.x,logo.y,diameter,now);
 }

 function render(now){
  if(stopped)return;frame=requestAnimationFrame(render);
  if(last!==null&&now-last<1000/61)return;
  const dt=reduced||last===null?0:Math.min(.05,(now-last)/1000);last=now;
  ctx.clearRect(0,0,width,height);
  zoff+=dt*.035*(boost>now?7:1);
  const cx=width/2,cy=height/2,f=Math.min(width,height)*.55;
  for(const s of stars){const z=Math.max(.03,((s.z-zoff)%1+1)%1),x=cx+(s.x-.5)*f/z,y=cy+(s.y-.5)*f/z;
   if(x<0||x>width||y<0||y>height)continue;dot(x,y,Math.max(.4,(1-z)*1.7),s.c,Math.min(1,(1-z)*1.2)*.75);}
  constellation(now);
  bounce(dt,now);
 }

 window.__playhubCircles={update(value){state={...state,...value};},timeText};
 resize();frame=requestAnimationFrame(render);window.addEventListener('resize',resize);
 window.addEventListener('pagehide',()=>{stopped=true;cancelAnimationFrame(frame);});
})();
