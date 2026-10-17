// The host supplies `host` in an isolated world on the declared official origin.
// Cache only responses loaded by normal browsing; never request account data.
const listings = new Map();
const buttons = new WeakMap();
let queued = false;
let located = '';
let batchAction;
let resultRoot;
let resultObserver;
let loadingResults=false;
const weightedGroup=/^#widgetrail-weighted=([0-9]{1,2})$/.exec(window.location.hash)?.[1];
let weightedReady=weightedGroup===undefined,weightedClicked=false,weightedStarted=Date.now(),weightedOldRoot;
function settleWeightedOrder() {
  if(weightedReady)return true;
  const field=document.querySelector('.resultset .s[data-field="statgroup.'+weightedGroup+'"]');
  if(field?.classList.contains('sorted-desc')&&(!weightedClicked||weightedOldRoot!==resultRoot||!weightedOldRoot?.isConnected)){weightedReady=true;return true;}
  if(field&&!weightedClicked){weightedClicked=true;weightedOldRoot=resultRoot;field.click();}
  if(Date.now()-weightedStarted<15000)queueRefresh();
  return false;
}
function resultRows() { return [...(resultRoot?.querySelectorAll('.row[data-id]')??[])]; }
function loadedEntries() {
  return [...new Set(resultRows().map(row=>row.getAttribute('data-id')))]
    .map(id=>listings.get(id)).filter(Boolean).slice(0,100);
}
let requestedListing = /^#widgetrail-listing=([a-fA-F0-9]{16,128})$/.exec(window.location.hash)?.[1];
function locateSavedListing() {
  if (!requestedListing || located === requestedListing) return;
  const row = document.querySelector('.resultset .row[data-id="' + requestedListing + '"]');
  if (!row) return;
  const action = buttons.get(row)?.button;
  if (!action) return;
  located = requestedListing;
  row.scrollIntoView({ block: 'center' });
  action.firstChild.textContent = 'Compare saved item in PoB';
  action.setAttribute('aria-label', 'WidgetRail: Compare saved item in PoB');
  action.focus({ preventScroll: true });
}
function refresh() {
  queued = false;
  if(!resultRoot?.isConnected) {
    resultObserver?.disconnect();resultRoot=document.querySelector('.resultset');
    if(resultRoot){resultObserver=new MutationObserver(queueRefresh);resultObserver.observe(resultRoot,{childList:true,subtree:true,attributes:true,attributeFilter:['class']});}
  }
  const resultSet=resultRoot;
  const ordered=settleWeightedOrder();
  if(resultSet) {
    if(!batchAction?.isConnected) {
      batchAction=host.addAction(resultSet,'evaluate-request','Load and evaluate results',()=>({url:locationUrl()}));
      resultSet.prepend(batchAction);
    }
    batchAction.disabled=loadingResults||!ordered||loadedEntries().length===0;
    if(!ordered)batchAction.firstChild.textContent=Date.now()-weightedStarted<15000?'Ordering by PoB weights…':'Select descending Weighted Sum on the website to evaluate';
    else batchAction.firstChild.textContent='Load and evaluate results';
  }
  for (const row of resultRows()) {
    const id = row.getAttribute('data-id');
    const listing = listings.get(id);
    const previous = buttons.get(row);
    if (previous && (previous.id !== id || !listing)) { previous.button.remove(); buttons.delete(row); }
    else if(previous)previous.button.disabled=!ordered;
    if (!listing || buttons.has(row)) continue;
    const location = row.querySelector('.right') || row;
    const button = host.addAction(location, 'compare-item', 'Compare in PoB', () => {
      if(!settleWeightedOrder())throw new Error('Wait for the search order to finish updating');
      const current = listings.get(id);
      if (!row.isConnected || row.getAttribute('data-id') !== id || !current) throw new Error('Listing changed');
      return { entry: current, url: locationUrl() };
    });
    button.disabled=!ordered;
    location.prepend(button);
    buttons.set(row, { id, button });
  }
  locateSavedListing();
}
function locationUrl() {
  const url=new URL(window.location.href);
  if(weightedGroup!==undefined&&weightedReady&&document.querySelector('.resultset .s[data-field="statgroup.'+weightedGroup+'"].sorted-desc'))url.hash='widgetrail-weighted='+weightedGroup;
  return url.href;
}
function queueRefresh() { if (!queued) { queued = true; setTimeout(refresh,50); } }
host.onRequest(async(method,payload,{signal,progress})=>{
  if(!settleWeightedOrder())throw new Error('Select descending Weighted Sum on the website before evaluating.');
  if(method!=='load-results'||loadingResults)throw new Error('Loading is unavailable');
  const limit=payload?.limit;
  if(![20,50,100].includes(limit)||!resultRoot?.isConnected)throw new Error('Invalid result limit');
  loadingResults=true;queueRefresh();
  const url=locationUrl(),root=resultRoot,first=resultRows()[0]?.getAttribute('data-id');
  const originalX=window.scrollX,originalY=window.scrollY;
  const started=Date.now();let lastProgress=started,lastCount=-1,lastScroll=0;
  let reason='';
  const wait=()=>new Promise((resolve,reject)=>{
    if(signal.aborted){reject(new Error('Cancelled'));return;}
    const abort=()=>{clearTimeout(timer);reject(new Error('Cancelled'));};
    const timer=setTimeout(()=>{signal.removeEventListener('abort',abort);resolve();},150);
    signal.addEventListener('abort',abort,{once:true});
  });
  try {
    while(true) {
      if(signal.aborted)throw new Error('Cancelled');
      if(locationUrl()!==url||!root.isConnected||resultRows()[0]?.getAttribute('data-id')!==first)throw new Error('Search changed');
      const entries=loadedEntries().slice(0,limit),now=Date.now();
      if(entries.length!==lastCount){lastCount=entries.length;lastProgress=now;progress({kind:'trade-loading',loaded:lastCount,limit});}
      if(entries.length>=limit)break;
      if(now-lastProgress>6000||now-started>24000){reason='No more item details arrived from the website.';break;}
      if(now-lastScroll>=700){
        const rows=resultRows();
        const next=rows.find(row=>!listings.has(row.getAttribute('data-id')))||rows.at(-1);
        next?.scrollIntoView({block:'end',behavior:'instant'});
        window.scrollBy({top:Math.min(window.innerHeight*.75,700),behavior:'instant'});
        lastScroll=now;
      }
      await wait();
    }
    const items=loadedEntries().slice(0,limit);
    if(!items.length)throw new Error('No loaded listings');
    return host.batchResult({url,requested:limit,loaded:items.length,reason},items);
  }finally{
    loadingResults=false;queueRefresh();
    if(locationUrl()===url&&root.isConnected)window.scrollTo({left:originalX,top:originalY,behavior:'instant'});
  }
});
host.onResponse(({ url, body }) => {
  if (!/^\/api\/trade2?\/fetch\//.test(new URL(url).pathname) || !Array.isArray(body.result)) return;
  for (const entry of body.result) {
    if (!entry || typeof entry.id !== 'string' || !entry.item || !entry.listing) continue;
    const { price, method, indexed, account, fee } = entry.listing;
    // Exclude whisper/purchase tokens and all unrelated account information.
    const safe = { id: entry.id, item: entry.item, listing: {
      price, method, indexed, fee, instantBuyout: typeof entry.listing.hideout_token === 'string' && entry.listing.hideout_token.length > 0,
      account: account && { name: account.name, online: account.online, lastCharacterName: account.lastCharacterName }
    }};
    if (JSON.stringify(safe).length > 48000) continue;
    listings.delete(entry.id); listings.set(entry.id, safe);
    while (listings.size > 100) listings.delete(listings.keys().next().value);
  }
  queueRefresh();
});
// The outer observer only discovers/replaces the result container. Row scans
// are scoped to that container and coalesced, including during automatic loading.
new MutationObserver(()=>{if(!resultRoot?.isConnected)queueRefresh();}).observe(document, { childList: true, subtree: true });
queueRefresh();
window.addEventListener('hashchange', () => {
  const requested = /^#widgetrail-listing=([a-fA-F0-9]{16,128})$/.exec(window.location.hash)?.[1];
  if (requested) { requestedListing = requested; located = ''; queueRefresh(); }
});
