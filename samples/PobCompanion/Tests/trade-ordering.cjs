const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const source=fs.readFileSync('samples/PobCompanion/web/trade-actions.js','utf8');
function fixture(hash){
  let descending=false,clicks=0,now=0,request,receive,compare;const timers=[];
  const listing={id:'b'.repeat(64),item:{name:'Test item'},listing:{account:{name:'Test seller'}}};
  const row={isConnected:true,getAttribute:()=>listing.id,querySelector:()=>null,prepend(){}};
  const field={classList:{contains:name=>name==='sorted-desc'&&descending},click(){clicks++;}};
  let root={isConnected:true,querySelectorAll:()=>[row],prepend(){}};
  const action={isConnected:true,firstChild:{textContent:''}};
  const context={window:{location:{hash,href:'https://www.pathofexile.com/trade2/search/Standard/abc'+hash},addEventListener(){}},
    document:{querySelector:s=>s==='.resultset'?root:s.includes('statgroup.0')?field:null},MutationObserver:class{observe(){}disconnect(){}},
    Date:{now:()=>now},setTimeout:f=>{timers.push(f);},Map,Set,URL,
    host:{onRequest:f=>{request=f;},onResponse:f=>{receive=f;},addAction:(target,id,title,callback)=>id==='compare-item'?(compare={callback,isConnected:true}):action}};
  vm.createContext(context);vm.runInContext(source,context);
  receive({url:'https://www.pathofexile.com/api/trade2/fetch/abc',body:{result:[listing]}});
  return{tick(){timers.shift()?.();},ready(){return vm.runInContext('settleWeightedOrder()',context);},setDescending(){descending=true;},replaceResults(){root.isConnected=false;root={isConnected:true,querySelectorAll:()=>[row],prepend(){}};},
    expire(){now=16000;},get clicks(){return clicks;},get compare(){return compare;},action,request};
}
(async()=>{
  let f=fixture('#widgetrail-weighted=0');f.tick();assert.equal(f.clicks,1);assert.equal(f.ready(),false);assert.match(f.action.firstChild.textContent,/Ordering/);
  f.tick();assert.equal(f.clicks,1,'a delayed response must not cause a second click reversing order');
  assert.equal(f.compare.disabled,true);assert.throws(()=>f.compare.callback(),/Wait for the search order/);
  await assert.rejects(f.request('load-results',{limit:20},{}),/descending Weighted Sum/);
  f.setDescending();f.tick();assert.equal(f.ready(),false,"sort indicator alone cannot admit results from the old search");f.replaceResults();f.tick();assert.equal(f.ready(),true);assert.equal(f.clicks,1);
  assert.equal(f.compare.disabled,false);assert.equal(f.compare.callback().entry.item.name,'Test item');
  f=fixture('');f.tick();assert.equal(f.clicks,0,'ordinary browsing is never reordered');
  f=fixture('#widgetrail-weighted=9');f.expire();f.tick();assert.equal(f.clicks,0);assert.match(f.action.firstChild.textContent,/Select descending/);
  console.log('PASS generated searches request one weighted sort, wait for confirmed descending state, block premature evaluation, and leave ordinary browsing alone');
})().catch(e=>{console.error(e);process.exitCode=1;});
