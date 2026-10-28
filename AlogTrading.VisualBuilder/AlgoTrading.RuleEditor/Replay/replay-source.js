/* Maps the selected run's stored SQL result to presentation data.

   Does not query market data, evaluate a strategy, or infer missing observations. */

(function (scope) {

 'use strict';

 function requireValue(test,message){if(!test)throw new Error(message);}

 function parseJson(text,label,fallback){

  if(text===null||text===undefined)return fallback;

  try{return JSON.parse(text);}catch{throw new Error('Invalid saved '+label+' JSON.');}

 }

 function timestamp(value,label){

  requireValue(typeof value==='string'&&/(Z|[+-]\d{2}:\d{2})$/i.test(value),label+' has no explicit timezone.');

  const ms=Date.parse(value);requireValue(Number.isFinite(ms),label+' is not a valid timestamp.');return ms/1000;

 }

 function readCandles(artifacts){

  const datasets=new Map();

  artifacts.filter(a=>a.artifactKind==='Candles').forEach(a=>{

   requireValue(a.payloadFormat==='JSON'&&a.schemaVersion===1,'Unsupported saved candle format.');

   const d=parseJson(a.payloadText,'candles',null),n=d?.timestampsUtc?.length;

   requireValue(n>0&&['open','high','low','close','closesAtUtc'].every(k=>Array.isArray(d[k])&&d[k].length===n),'Saved candle arrays do not match.');

   requireValue(!datasets.has(d.timeframe),'Duplicate saved candle timeframe.');

   const frames=d.timestampsUtc.map((t,i)=>({time:timestamp(t,'Candle'),closedAt:timestamp(d.closesAtUtc[i],'Candle close'),open:d.open[i],high:d.high[i],low:d.low[i],close:d.close[i]}));

   frames.forEach((f,i)=>requireValue([f.open,f.high,f.low,f.close].every(Number.isFinite)&&f.closedAt>f.time&&(!i||f.time>frames[i-1].time),'Invalid saved candle.'));

   datasets.set(d.timeframe,frames);

  });

  return datasets;

 }

 function describeSource(source){

  if(!source)return '';

  if(source.kind==='field')return ['Open','High','Low','Close','Volume','OI'][source.Field]??JSON.stringify(source);

  if(source.kind==='indicator'){

   const names=['Sma','Ema','Dema','Tema','Wma','Hma','Rsi','Macd','BollingerBands','Atr','Adx','Supertrend','Vwap','Obv','Adl','Stochastic','Cci','PivotPointsClassic','PivotPointsCamarilla','VolumeSma','Roc','Momentum','Trix','KeltnerChannel','DonchianChannel','Mfi','HighLow52W'];

   return (names[source.Indicator]??source.Indicator)+'('+Object.entries(source.Params??{}).map(([k,v])=>k+'='+v).concat(describeSource(source.Source)).join(', ')+')';

  }

  return JSON.stringify(source);

 }

 // Unitless outputs must never participate in the candle price scale.
 function indicatorPanel(indicator,output){
  if(indicator==='Supertrend'&&output==='Direction')return 'data';
  if(['Rsi','Stochastic','Mfi','Adx'].includes(indicator))return 'oscillator';
  if(['Obv','Adl','VolumeSma','Roc','Momentum','Trix','Cci','Macd'].includes(indicator))return 'data';
  return 'price';
 }
 function readIndicators(artifacts,datasets,frames){

  return artifacts.filter(a=>a.artifactKind==='Indicators').flatMap(a=>{

   requireValue(a.payloadFormat==='JSON'&&a.schemaVersion===1,'Unsupported saved indicator format.');

   const d=parseJson(a.payloadText,'indicators',null),candles=datasets.get(d?.timeframe);

   requireValue(candles&&Array.isArray(d.timestampsUtc)&&Array.isArray(d.outputNames)&&Array.isArray(d.values)&&d.values.length===d.outputNames.length,'Saved indicator definition or candle timeframe is missing.');

   const closes=new Map(candles.map(c=>[c.time,c.closedAt]));

   const available=d.timestampsUtc.map(t=>closes.get(timestamp(t,'Indicator')));

   requireValue(available.every((t,i)=>Number.isFinite(t)&&(!i||t>available[i-1])),'Saved indicator timestamps do not match candles.');

   return d.outputNames.map((output,k)=>{

    const saved=d.values[k];requireValue(Array.isArray(saved)&&saved.length===available.length&&saved.every(v=>v===null||Number.isFinite(v)),'Saved indicator values do not match timestamps.');

    let at=-1;

    const values=frames.map(f=>{while(at+1<available.length&&available[at+1]<=f.closedAt)at++;return at<0?null:saved[at];});

    return {id:d.key+'|'+output,label:d.indicator+'('+Object.entries(d.parameters??{}).map(([key,v])=>key+'='+v).concat(describeSource(d.source)).filter(Boolean).join(', ')+') · '+d.timeframe+' · '+output,

     panel:indicatorPanel(d.indicator,output),values,definition:{key:d.key,indicator:d.indicator,parameters:d.parameters,source:d.source,timeframe:d.timeframe,output}};

   });

  });

 }

 function readSignals(artifacts,frames){

  return artifacts.filter(a=>a.artifactKind==='Signals').flatMap(a=>{

   requireValue(a.payloadFormat==='JSON'&&a.schemaVersion===1,'Unsupported saved signal format.');

   const d=parseJson(a.payloadText,'signals',null);

   // Older artifacts contain synthetic all-false arrays, not engine observations.

   if(d?.recordedEvaluations!==true)return [];

   requireValue(Array.isArray(d.availableAtUtc)&&Array.isArray(d.signalNames)&&Array.isArray(d.values)&&d.values.length===d.signalNames.length,'Invalid saved signal definition.');

   const times=d.availableAtUtc.map(t=>timestamp(t,'Signal availability'));

   requireValue(times.every((t,i)=>!i||t>times[i-1]),'Saved signal timestamps must increase.');

   return d.signalNames.map((name,k)=>{

    const values=d.values[k];

    requireValue(Array.isArray(values)&&values.length===times.length&&values.every(v=>typeof v==='boolean'),'Invalid saved signal values.');

    return {label:name,values:frames.map(f=>{

     const end=f.closedAt??f.time;

     let low=0,high=times.length-1,at=-1;

     while(low<=high){const mid=(low+high)>>1;if(times[mid]<=end){at=mid;low=mid+1;}else high=mid-1;}

     return at>=0&&times[at]>(f.time??end)?values[at]:null;

    })};

   });

  });

 }

 function frameIndex(frames,time){

  let low=0,high=frames.length-1;

  while(low<=high){const mid=(low+high)>>1;if(frames[mid].time<=time)low=mid+1;else high=mid-1;}

  requireValue(high>=0&&(frames[high].closedAt===undefined?frames[high].time===time:time<=frames[high].closedAt),'Saved trade lies outside the saved candles.');return high;

 }

 function fromSavedRun(raw){

  requireValue(typeof raw?.runId==='string'&&/^[\da-f]{8}(-[\da-f]{4}){3}-[\da-f]{12}$/i.test(raw.runId),'Selected run identity is missing.');

  const config=parseJson(raw.configurationJson,'configuration',null);

  requireValue(config&&typeof config==='object'&&!Array.isArray(config),'Saved run configuration is missing.');

  const savedTrades=parseJson(raw.tradesJson,'trades',[]),savedEquity=parseJson(raw.equityJson,'equity',[]);

  requireValue(Array.isArray(savedTrades)&&Array.isArray(savedEquity),'Saved trades and equity must be arrays.');

  const equity=new Map();

  savedEquity.forEach((point,i)=>{

   const t=timestamp(point.dateTime,'Equity row '+i);requireValue(Number.isFinite(point.equity),'Invalid saved equity at row '+i);

   requireValue(!equity.has(t),'Duplicate saved equity timestamp.');equity.set(t,point.equity);

  });

  const times=new Set(equity.keys()),identities=new Set();

  const trades=savedTrades.map((t,i)=>{

   requireValue(Number.isInteger(t.tradeId)&&!identities.has(t.tradeId),'Invalid or duplicate saved trade identity at row '+i);identities.add(t.tradeId);

   requireValue(['long','short'].includes(t.direction),'Unknown saved trade direction at row '+i);

   requireValue(Number.isFinite(t.entryPrice)&&Number.isFinite(t.exitPrice)&&Number.isFinite(t.netPnL),'Invalid saved trade prices or P&L at row '+i);

   const entryTime=timestamp(t.entryTime,'Trade entry '+i),exitTime=timestamp(t.exitTime,'Trade exit '+i);

   requireValue(exitTime>=entryTime,'Saved trade exit precedes entry.');times.add(entryTime);times.add(exitTime);

   return {id:String(t.tradeId),direction:t.direction==='long'?'Long':'Short',entryTime,exitTime,entryPrice:t.entryPrice,exitPrice:t.exitPrice,net:t.netPnL,reason:t.exitReason??'Unavailable'};

  });

  const artifacts=raw.artifacts??[];

  requireValue(Array.isArray(artifacts)&&artifacts.every(a=>typeof a.runId==='string'&&a.runId.toLowerCase()===raw.runId.toLowerCase()),'Artifact run identity does not match the selected run.');

  const datasets=readCandles(artifacts),evaluation=config.rule?.evaluationTimeframe;

  if(datasets.size)requireValue(datasets.has(evaluation),'Saved evaluation-timeframe candles are missing.');

  const frames=datasets.get(evaluation)??[...times].sort((a,b)=>a-b).map(time=>({time}));

  trades.forEach(t=>{t.entryIndex=frameIndex(frames,t.entryTime);t.exitIndex=frameIndex(frames,t.exitTime);});

  const indicators=readIndicators(artifacts,datasets,frames);

  const signals=readSignals(artifacts,frames);

  const kinds=new Set(artifacts.map(a=>a.artifactKind));

  const missing=['Candles','Indicators','Signals'].filter(kind=>!kinds.has(kind));

  const notes=[];

  if(!signals.length)notes.push('Recorded signal evaluations unavailable; rerun with signal recording enabled.');

  if(missing.length)notes.push('Saved artifacts unavailable: '+missing.join(', ')+'.');

  if(raw.tradesJson==null)notes.push('Saved trades unavailable.');

  if(raw.equityJson==null)notes.push('Saved equity unavailable.');

  if(raw.runStatus!=='Completed')notes.push('Run status: '+raw.runStatus+'.');

  if(config.runMode&&config.runMode!=='Single')notes.push('Showing stored root trades/equity; an optimized or out-of-sample result is not certified.');

  return {

   title:(raw.strategyName||'Saved strategy')+' · '+raw.runId,

   description:'Instrument '+raw.instrumentId+' · '+(config.runMode||'Mode unavailable')+' · '+raw.runStatus+' · '+trades.length+' saved trades · IST',

   warning:notes.join(' ')||'Showing saved run data.',

   frames,hasCandles:datasets.size>0,indicators,signals,trades,

   panels:indicators.some(s=>s.panel==='oscillator')?{oscillator:{minimum:0,maximum:100,thresholds:[30,70]}}:undefined,

   equity:raw.equityJson==null?null:frames.map(f=>equity.get(f.time)??null),

   strategy:config.rule??null,

   provenance:{runId:raw.runId,runStatus:raw.runStatus,resultUnit:raw.resultUnit,executionModel:raw.executionModel,startedAt:raw.startedAt,finishedAt:raw.finishedAt,errorMessage:raw.errorMessage,configuration:config,indicatorDefinitions:indicators.map(s=>s.definition)},

   valueUnit:raw.resultUnit||'Unit unavailable'

  };

 }

 const api=Object.freeze({fromSavedRun});

 if(typeof module!=='undefined')module.exports=api;

 scope.ReplaySource=api;

})(typeof window==='undefined'?globalThis:window);

