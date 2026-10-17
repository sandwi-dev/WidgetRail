// The host supplies `host` in an isolated world on the declared official origin.
// Cache only responses loaded by normal browsing; never request account data.
const listings = new Map();
const buttons = new WeakMap();
let queued = false;
let located = '';
let batchAction;
function loadedEntries() {
  return [...new Set([...document.querySelectorAll('.resultset .row[data-id]')].map(row=>row.getAttribute('data-id')))]
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
  const resultSet=document.querySelector('.resultset');
  if(resultSet&&host.addBatchAction) {
    if(!batchAction?.isConnected) {
      batchAction=host.addBatchAction(resultSet,'evaluate-items','Evaluate loaded results',()=>({context:{url:locationUrl()},items:loadedEntries()}));
      resultSet.prepend(batchAction);
    }
    const label='Evaluate loaded results ('+loadedEntries().length+')';
    if(batchAction.firstChild.textContent!==label)batchAction.firstChild.textContent=label;
    batchAction.disabled=loadedEntries().length===0;
  }
  for (const row of document.querySelectorAll('.resultset .row[data-id]')) {
    const id = row.getAttribute('data-id');
    const listing = listings.get(id);
    const previous = buttons.get(row);
    if (previous && (previous.id !== id || !listing)) { previous.button.remove(); buttons.delete(row); }
    if (!listing || buttons.has(row)) continue;
    const location = row.querySelector('.right') || row;
    const button = host.addAction(location, 'compare-item', 'Compare in PoB', () => {
      const current = listings.get(id);
      if (!row.isConnected || row.getAttribute('data-id') !== id || !current) throw new Error('Listing changed');
      return { entry: current, url: locationUrl() };
    });
    location.prepend(button);
    buttons.set(row, { id, button });
  }
  locateSavedListing();
}
function locationUrl() { return window.location.href; }
function queueRefresh() { if (!queued) { queued = true; queueMicrotask(refresh); } }
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
new MutationObserver(queueRefresh).observe(document, { childList: true, subtree: true });
queueRefresh();
window.addEventListener('hashchange', () => {
  const requested = /^#widgetrail-listing=([a-fA-F0-9]{16,128})$/.exec(window.location.hash)?.[1];
  if (requested) { requestedListing = requested; located = ''; queueRefresh(); }
});
