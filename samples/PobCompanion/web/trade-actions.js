// The host supplies `host` in an isolated world on the declared official origin.
// Cache only responses loaded by normal browsing; never request account data.
const listings = new Map();
const buttons = new WeakMap();
let queued = false;
function refresh() {
  queued = false;
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
      price, method, indexed, fee,
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
