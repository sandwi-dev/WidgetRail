const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const source=fs.readFileSync('samples/PobCompanion/web/trade-actions.js','utf8');
function fixture(total=100){
  let now=0,sequence=0,loaded=0,scrolls=0,receive,request;const timers=new Map(),progress=[];
  const elements=[];
  function element(){return{isConnected:true,style:{},firstChild:{textContent:''},setAttribute(){},append(){},prepend(){},focus(){}};}
  const root={...element(),querySelectorAll:()=>elements};
  const window={location:{href:'https://www.pathofexile.com/trade2/search/poe2/Test/abc',hash:''},scrollX:0,scrollY:120,innerHeight:600,addEventListener(){},scrollBy(){scrolls++;load(Math.min(loaded+10,total));window.scrollY+=450;},scrollTo(p){window.scrollX=p.left;window.scrollY=p.top;}};
  function load(count){const result=[];for(let i=loaded;i<count;i++){const id=i.toString(16).padStart(32,'0');elements.push({...element(),getAttribute:()=>id,querySelector:()=>null,scrollIntoView(){}});result.push({id,item:{name:'Item '+i},listing:{account:{name:'Seller'},price:{amount:1,currency:'divine'}}});}loaded=count;if(result.length)receive({url:'https://www.pathofexile.com/api/trade2/fetch/abc',body:{result}});}
  const context={window,document:{querySelector:s=>s==='.resultset'?root:null},MutationObserver:class{observe(){}disconnect(){}},Date:{now:()=>now},AbortController,URL,Map,Set,
    setTimeout(fn,ms){const id=++sequence;timers.set(id,{fn,at:now+ms});return id;},clearTimeout(id){timers.delete(id);},host:{onResponse(fn){receive=fn;},onRequest(fn){request=fn;},addAction(){return element();},batchResult:(context,items)=>({context,items})}};
  vm.createContext(context);vm.runInContext(source,context);load(Math.min(total,10));
  async function tick(){now+=150;for(const [id,t]of[...timers])if(t.at<=now){timers.delete(id);t.fn();}await Promise.resolve();await Promise.resolve();}
  async function run(limit,hook){await tick();const abort=new AbortController();let done=false,value,error;request('load-results',{limit},{signal:abort.signal,progress:p=>progress.push(p)}).then(v=>{done=true;value=v;},e=>{done=true;error=e;});for(let n=0;!done&&n<220;n++){hook?.(n,abort,window);await tick();}assert.ok(done,'loader terminates');if(error)throw error;return value;}
  return{run,progress,window,get scrolls(){return scrolls;}};
}
(async()=>{
  let f=fixture();let result=await f.run(100);assert.equal(result.items.length,100);assert.equal(f.scrolls,9);assert.equal(f.window.scrollY,120);assert.equal(f.progress.at(-1).loaded,100);
  f=fixture();result=await f.run(20);assert.equal(result.items.length,20);assert.equal(f.scrolls,1);
  f=fixture(10);result=await f.run(100);assert.equal(result.items.length,10);assert.ok(result.context.reason);assert.equal(f.window.scrollY,120);
  f=fixture();await assert.rejects(f.run(100,(n,a)=>{if(n===2)a.abort();}),/Cancelled/);assert.equal(f.window.scrollY,120);
  f=fixture();await assert.rejects(f.run(100,(n,a,w)=>{if(n===2)w.location.href+='changed';}),/Search changed/);
  console.log('PASS automatic 10-to-100 loading, selected limit, partial results, cancellation, changed-search rejection and scroll restoration');
})().catch(e=>{console.error(e);process.exitCode=1;});
