/* Presentation only: consumes a ReplaySource model. Never queries SQL or evaluates a strategy. */
(function () {
 'use strict';
 const el=id=>document.getElementById(id), colors=['--s1','--s2','--s3','--s4','--s5'];
 const numeric=new Intl.NumberFormat('en-IN',{minimumFractionDigits:2,maximumFractionDigits:4});
 const datetime=new Intl.DateTimeFormat('en-IN',{timeZone:'Asia/Kolkata',day:'2-digit',month:'short',year:'numeric',hour:'2-digit',minute:'2-digit',hour12:false});
 const compactTime=new Intl.DateTimeFormat('en-IN',{timeZone:'Asia/Kolkata',day:'2-digit',month:'short',hour:'2-digit',minute:'2-digit',hour12:false});
 let model=null,pos=0,hover=null,timer=null,events=[],shown=new Set();
 const format=v=>v===null||v===undefined?'Unavailable':numeric.format(v);
 const time=v=>datetime.format(new Date(v*1000));
 function stop(){clearInterval(timer);timer=null;el('play').textContent='Play';}
 function row(parent,values){const tr=document.createElement('tr');values.forEach(value=>{const td=document.createElement('td');td.textContent=String(value);tr.append(td);});parent.append(tr);}
 function move(index){if(!model)return;pos=Math.max(0,Math.min(model.frames.length-1,index));hover=null;render();}
 function setModel(next){
  stop();model=next;pos=0;hover=null;shown=new Set(model.indicators.filter(s=>s.panel!=='data').map(s=>s.id));
  events=model.trades.flatMap(t=>[{index:t.entryIndex,time:t.entryTime,type:'Entry',price:t.entryPrice,trade:t},...(t.exitIndex===null?[]:[{index:t.exitIndex,time:t.exitTime,type:'Exit',price:t.exitPrice,trade:t}])]).sort((a,b)=>a.index-b.index||a.time-b.time);
  el('run-title').textContent=model.title;el('run-meta').textContent=model.description;el('notice').textContent=model.warning;
  document.title=model.title;
  el('events-caption').textContent='Recorded trade events · reached in replay · '+model.valueUnit;
  el('snapshot').textContent=JSON.stringify({strategy:model.strategy,provenance:model.provenance},null,2);
  el('trade').replaceChildren();
  const all=document.createElement('option');all.value='';all.textContent='Choose a recorded trade';el('trade').append(all);
  model.trades.forEach((t,i)=>{const o=document.createElement('option');o.value=i;o.textContent=t.direction+' #'+t.id+' · '+time(t.entryTime);el('trade').append(o);});
  el('legend').replaceChildren();
  model.indicators.forEach((s,i)=>{const button=document.createElement('button');button.type='button';button.setAttribute('aria-pressed',String(shown.has(s.id)));const swatch=document.createElement('span');swatch.className='swatch';swatch.style.background='var('+colors[i%colors.length]+')';button.append(swatch,document.createTextNode(s.label+(s.panel==='data'?' · table only':'')));button.disabled=s.panel==='data';button.onclick=()=>{if(shown.has(s.id))shown.delete(s.id);else shown.add(s.id);button.setAttribute('aria-pressed',String(shown.has(s.id)));render();};el('legend').append(button);});
  document.querySelectorAll('.controls button,.controls select,#scrub').forEach(control=>control.disabled=!model.frames.length);
  el('trade').disabled=!model.trades.length;el('scrub').max=model.frames.length-1;
  if(model.trades.length)pos=model.trades[0].entryIndex;
  render();
 }
 function render(){
  if(!model){draw();return;}
  if(!model.frames.length){el('clock').textContent='No saved timeline observations.';el('counter').textContent='0 / 0';draw();return;}
  const i=hover??pos,b=model.frames[i];
  window.setReplayCandleOHLC?.({...b,time:new Date(b.time*1000).toISOString()});
  el('counter').textContent=(pos+1)+' / '+model.frames.length;el('scrub').value=pos;
  el('clock').textContent=time(model.frames[pos].time)+' IST'+(model.hasCandles?' · O '+format(model.frames[pos].open)+' · H '+format(model.frames[pos].high)+' · L '+format(model.frames[pos].low)+' · C '+format(model.frames[pos].close):' · Saved candles unavailable')+' · Equity '+format(model.equity?.[pos]);
  el('inspect').textContent=(hover===null?'Replay timestamp: ':'Inspecting: ')+time(b.time)+' IST · Equity '+format(model.equity?.[i]);
  el('values').replaceChildren();model.indicators.forEach(s=>row(el('values'),[s.label,i?format(s.values[i-1]):'—',format(s.values[i])]));
  el('signals').replaceChildren();const flag=v=>typeof v==='boolean'?(v?'TRUE':'FALSE'):'Unavailable';model.signals.forEach(s=>row(el('signals'),[s.label,i?flag(s.values[i-1]):'—',flag(s.values[i])]));
  if(!model.indicators.length)row(el('values'),['Saved indicator values unavailable','—','—']);
  if(!model.signals.length)row(el('signals'),['Saved signals unavailable','—','—']);
  el('events').replaceChildren();events.filter(e=>e.index<=pos).slice(-8).reverse().forEach(e=>row(el('events'),[time(e.time),e.trade.direction+' #'+e.trade.id,e.type+(e.type==='Exit'?' · '+e.trade.reason:''),format(e.price),e.type==='Exit'?format(e.trade.net):'—']));
  if(!el('events').children.length)row(el('events'),['No recorded events reached','—','—','—','—']);
  draw();
 }
 function frame(id){
  const canvas=el(id),rect=canvas.getBoundingClientRect(),dpr=window.devicePixelRatio||1;
  canvas.width=Math.round(rect.width*dpr);canvas.height=Math.round(rect.height*dpr);
  const ctx=canvas.getContext('2d');ctx.scale(dpr,dpr);ctx.font='12px system-ui';
  // Resolve theme colors through DOM; canvas does not reliably parse light-dark().
  return {ctx,w:rect.width,h:rect.height};
 }
 function theme(variable){const probe=document.createElement('span');probe.style.color='var('+variable+')';probe.hidden=true;document.body.append(probe);const c=getComputedStyle(probe).color;probe.remove();return c;}
 function text(ctx,s,x,y,align='left'){ctx.fillStyle=theme('--fg');ctx.textAlign=align;ctx.fillText(String(s),x,y);}
 function path(ctx,points,color,dash=[]){ctx.beginPath();let active=false;points.forEach(p=>{if(!p||!Number.isFinite(p[1])){active=false;return;}if(active)ctx.lineTo(...p);else ctx.moveTo(...p);active=true;});ctx.strokeStyle=color;ctx.lineWidth=1.5;ctx.setLineDash(dash);ctx.stroke();ctx.setLineDash([]);}
 function draw(){
  ['price','oscillator','equity'].forEach(id=>drawPanel(id));
 }
 function drawPanel(id){
  const {ctx,w,h}=frame(id);if(w<1||h<1)return;
  if(!model||!model.frames.length){text(ctx,'No saved timeline observations',w/2,h/2,'center');return;}
  const first=Math.max(0,pos-Number(el('window').value)+1),left=12,right=w<450?78:100,top=26,bottom=h-34;
  const x=i=>left+(i-first+.6)/Math.max(2,pos-first+1.2)*(w-left-right);
  const visible=model.frames.slice(first,pos+1),series=model.indicators.filter(s=>shown.has(s.id)&&s.panel===id);
  let values=[];
  if(id==='price')values=visible.flatMap(b=>[b.low,b.high]).concat(events.filter(e=>e.index>=first&&e.index<=pos).map(e=>e.price),model.trades.filter(t=>t.entryIndex<=pos&&t.exitIndex>pos).map(t=>t.entryPrice));
  series.forEach(s=>values.push(...s.values.slice(first,pos+1)));
  if(id==='equity'&&model.equity)values=model.equity.slice(first,pos+1);
  values=values.filter(v=>v!==null&&v!==undefined&&Number.isFinite(v));
  if(!values.length){text(ctx,id==='equity'?'Saved equity unavailable':id==='oscillator'?'Saved oscillator values unavailable':'Saved candles unavailable',w/2,h/2,'center');return;}
  let low=Math.min(...values),high=Math.max(...values),pad=Math.max((high-low)*.12,Math.abs(high)*.00002,.01);low-=pad;high+=pad;
  const panel=model.panels?.[id];if(panel){low=panel.minimum;high=panel.maximum;}
  const y=v=>bottom-(v-low)/(high-low)*(bottom-top);
  ctx.strokeStyle=theme('--line');ctx.lineWidth=1;ctx.strokeRect(left,top,w-left-right,bottom-top);
  text(ctx,id==='price'?(model.hasCandles?'Price':'Saved fills · candles unavailable'):id==='oscillator'?'Oscillator':'Saved equity · '+model.valueUnit,left,16);
  for(let j=0;j<4;j++){const value=low+(high-low)*j/3,yy=y(value);path(ctx,[[left,yy],[w-right,yy]],theme('--line'));text(ctx,new Intl.NumberFormat('en-IN',{maximumFractionDigits:2}).format(value),w-right+8,yy+4,'left');}
  const count=w<600?3:6;
  const ticks=[...new Set(Array.from({length:count},(_,j)=>first+Math.round((pos-first)*j/(count-1))))];
  ticks.forEach((i,j)=>text(ctx,compactTime.format(new Date(model.frames[i].time*1000)),x(i),h-12,j===0?'left':j===ticks.length-1?'right':'center'));
  ctx.save();ctx.beginPath();ctx.rect(left,top,w-left-right,bottom-top);ctx.clip();
  (panel?.thresholds??[]).forEach(value=>{path(ctx,[[left,y(value)],[w-right,y(value)]],theme('--muted'),[4,4]);text(ctx,String(value),w-right-4,y(value)-4,'right');});
  if(id==='price'){
   const cw=Math.max(2,Math.min(12,(w-left-right)/Math.max(15,visible.length)*.72));
    visible.forEach((b,k)=>{if(!model.hasCandles)return;const xx=x(first+k);path(ctx,[[xx,y(b.low)],[xx,y(b.high)]],theme(b.close>=b.open?'--long':'--short'));ctx.fillStyle=b.close>=b.open?theme('--long'):theme('--short');ctx.fillRect(xx-cw/2,Math.min(y(b.open),y(b.close)),cw,Math.max(1,Math.abs(y(b.open)-y(b.close))));});
   model.trades.filter(t=>t.entryIndex<=pos&&(t.exitIndex===null||t.exitIndex>pos)).forEach(t=>path(ctx,[[x(Math.max(first,t.entryIndex)),y(t.entryPrice)],[x(pos),y(t.entryPrice)]],theme(t.direction==='Long'?'--long':'--short'),[5,4]));
   events.filter(e=>e.index>=first&&e.index<=pos).forEach(e=>{const xx=x(e.index),yy=y(e.price),short=e.trade.direction==='Short';ctx.beginPath();if(e.type==='Entry'){ctx.moveTo(xx,yy+(short?6:-6));ctx.lineTo(xx-5,yy+(short?-4:4));ctx.lineTo(xx+5,yy+(short?-4:4));}else{ctx.moveTo(xx,yy-6);ctx.lineTo(xx+5,yy);ctx.lineTo(xx,yy+6);ctx.lineTo(xx-5,yy);}ctx.closePath();ctx.strokeStyle=theme(short?'--short':'--long');ctx.fillStyle=e.type==='Exit'&&short?theme('--bg'):ctx.strokeStyle;ctx.fill();ctx.stroke();});
  }
  series.forEach(s=>path(ctx,s.values.slice(first,pos+1).map((v,k)=>v===null?null:[x(first+k),y(v)]),theme(colors[model.indicators.indexOf(s)%colors.length]),[]));
  if(id==='equity'&&model.equity)path(ctx,model.equity.slice(first,pos+1).map((v,k)=>v===null?null:[x(first+k),y(v)]),theme('--s1'));
  const selected=hover??pos;
  path(ctx,[[x(selected),top],[x(selected),bottom]],theme('--muted'),[3,3]);
  if(id==='price'&&model.hasCandles){
   const candle=model.frames[selected],yy=y(candle.close),color=theme(candle.close>=candle.open?'--long':'--short');
   path(ctx,[[left,yy],[w-right,yy]],color,[2,3]);
   ctx.restore();ctx.fillStyle=color;ctx.fillRect(w-right,yy-10,right,20);
   ctx.fillStyle=theme('--bg');ctx.textAlign='left';ctx.fillText(format(candle.close),w-right+7,yy+4);
  }else ctx.restore();
 }
 el('scrub').oninput=e=>{stop();move(Number(e.target.value));};
 el('step').onclick=()=>{stop();move(pos+1);};el('back').onclick=()=>{stop();move(pos-1);};
 el('prev-event').onclick=()=>{stop();move(events.findLast(e=>e.index<pos)?.index??0);};
 el('next-event').onclick=()=>{stop();move(events.find(e=>e.index>pos)?.index??model.frames.length-1);};
 el('trade').onchange=e=>{if(e.target.value==='')return;stop();move(model.trades[Number(e.target.value)].entryIndex);};
 el('window').onchange=render;
 el('equity-toggle').onchange=event=>{
  el('plots').classList.toggle('show-equity',event.target.checked);draw();
 };
 el('play').onclick=()=>{if(timer){stop();return;}if(pos===model.frames.length-1)move(0);el('play').textContent='Pause';timer=setInterval(()=>{const target=Math.min(model.frames.length-1,pos+Number(el('speed').value));move(events.find(e=>e.index>pos&&e.index<=target)?.index??target);if(pos===model.frames.length-1)stop();},400);};
 ['price','oscillator','equity'].forEach(id=>{
  el(id).addEventListener('pointermove',event=>{if(!model?.frames.length)return;const rect=el(id).getBoundingClientRect(),left=12,right=rect.width<450?78:100,first=Math.max(0,pos-Number(el('window').value)+1);hover=Math.max(first,Math.min(pos,Math.round(first-.6+(event.clientX-rect.left-left)/(rect.width-left-right)*Math.max(2,pos-first+1.2))));render();});
  el(id).addEventListener('pointerleave',()=>{hover=null;render();});
 });
 window.addEventListener('pagehide',stop);document.addEventListener('visibilitychange',()=>{if(document.hidden)stop();});
 new ResizeObserver(draw).observe(el('plots'));window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change',draw);
 function loadSnapshot(data){
  el('error').hidden=true;
  try{setModel(window.ReplaySource.fromSavedRun(data));}
  catch(error){el('error').textContent='Saved replay could not be loaded: '+error.message;el('error').hidden=false;}
 }
 el('snapshot-file').onchange=async event=>{
  const file=event.target.files[0];if(!file)return;
  try{loadSnapshot(JSON.parse(await file.text()));}
  catch(error){el('error').textContent='Snapshot could not be read: '+error.message;el('error').hidden=false;}
 };
 if(window.chrome?.webview)window.chrome.webview.addEventListener('message',event=>{
  loadSnapshot(event.data);
 });
 else{
  el('snapshot-import').hidden=false;
  el('notice').textContent='Open a run from WPF backtest history, or select a saved replay snapshot JSON file.';
  el('run-meta').textContent='Waiting for a saved run snapshot.';
 }
})();
