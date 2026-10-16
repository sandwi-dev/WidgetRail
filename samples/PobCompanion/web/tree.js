(() => {
  'use strict';
  const map = document.getElementById('map'), canvas = document.getElementById('lines');
  const container = document.getElementById('nodes'), status = document.getElementById('status');
  const context = canvas.getContext('2d');
  let nodes = [], links = [], byId = new Map(), incoming = null, revision = -1;
  let currentAscendancy = '';
  let treeVersion = '', artwork = null;
  const sheets = new Map(), tooltipCache = new Map(), tooltip = document.getElementById('tooltip');
  let hoverId = 0, hoverTimer = null, hoverX = 0, hoverY = 0;
  function loadArtwork(version){
    artwork=null;sheets.clear();
    if(!/^[A-Za-z0-9_]+$/.test(version))return;
    fetch(`art/${version}/index.json`).then(r=>{if(!r.ok)throw new Error('No artwork');return r.json();})
      .then(data=>{if(treeVersion===version){artwork=data;redraw();}}).catch(()=>{});
  }
  function icon(n,x,y,radius) {
    const index=artwork?.icons?.[n.icon];
    if(!Number.isSafeInteger(index) || index<0 || radius<5)return;
    const perSheet=artwork.columns*artwork.columns, sheetId=Math.floor(index/perSheet);
    let sheet=sheets.get(sheetId);
    if(!sheet){sheet=new Image();sheets.set(sheetId,sheet);sheet.onload=redraw;sheet.src=`art/${treeVersion}/nodes-${sheetId}.jpg`;}
    if(!sheet.complete || !sheet.naturalWidth)return;
    const offset=index%perSheet;
    context.save();context.beginPath();context.arc(x,y,radius-1,0,Math.PI*2);context.clip();
    context.globalAlpha=n.allocated?1:.5;
    context.drawImage(sheet,(offset%artwork.columns)*artwork.tile,Math.floor(offset/artwork.columns)*artwork.tile,artwork.tile,artwork.tile,x-radius,y-radius,radius*2,radius*2);
    context.restore();
  }
  function showTooltip(data) {
    if(data.id!==hoverId)return;
    document.getElementById('tooltip-name').textContent=data.name;
    document.getElementById('tooltip-kind').textContent=`${data.kind} · ${data.allocated?'Allocated':'Unallocated'}`;
    document.getElementById('tooltip-copy').textContent=data.description;
    document.getElementById('tooltip-cost').textContent=data.requiresChoice?'Choose this option in desktop PoB':data.allocated
      ? data.canRefund?`Refund ${data.points} point${data.points===1?'':'s'} including dependants`:'Starting node'
      : data.canAllocate?`${data.points} point${data.points===1?'':'s'} to allocate path`:'No available allocation path';
    tooltip.hidden=false;
    const rect=map.getBoundingClientRect();
    tooltip.style.left=Math.max(8,Math.min(rect.left+hoverX+18,document.documentElement.clientWidth-tooltip.offsetWidth-8))+'px';
    tooltip.style.top=Math.max(8,Math.min(rect.top+hoverY+18,document.documentElement.clientHeight-tooltip.offsetHeight-8))+'px';
  }
  function hover(id,x,y) {
    hoverX=x;hoverY=y;
    if(id===hoverId)return;
    hoverId=id;clearTimeout(hoverTimer);tooltip.hidden=true;
    if(!id)return;
    hoverTimer=setTimeout(()=>{
      if(tooltipCache.has(id))showTooltip(tooltipCache.get(id));
      else WidgetRailWebSession.post({type:'inspect',id,revision});
    },180);
  }
  let scale = .1, cx = 0, cy = 0, selected = 0, preview = false, queued = false, hasSnapshot = false, autoFit = true, fitAscendancy = false;
  const point = n => [(n.x - cx) * scale + map.clientWidth / 2, (n.y - cy) * scale + map.clientHeight / 2];
  const cssColor = id => getComputedStyle(document.getElementById(id)).color;
  function redraw() { if (!queued) { queued = true; requestAnimationFrame(draw); } }
  function pick(id) {
    if (preview) return;
    selected = id; redraw(); WidgetRailWebSession.post({ type: 'select', id });
  }
  function draw() {
    queued = false;
    const w = map.clientWidth, h = map.clientHeight, dpr = devicePixelRatio || 1;
    canvas.width = Math.round(w * dpr); canvas.height = Math.round(h * dpr);
    context.setTransform(dpr, 0, 0, dpr, 0, 0);
    const colors = { base: cssColor('lines'), active: cssColor('nodes'), added: cssColor('added'), removed: cssColor('removed'), selected: cssColor('selected') };
    const edgeStyle = (a, b) => (a.change === 'removed' || b.change === 'removed') && (a.allocated || a.change === 'removed') && (b.allocated || b.change === 'removed') ? 'removed'
      : a.allocated && b.allocated ? a.change === 'added' || b.change === 'added' ? 'added' : 'active' : 'base';
    for (const style of ['base', 'active', 'removed', 'added']) {
      context.strokeStyle = colors[style]; context.lineWidth = style === 'base' ? 1 : 2;
      context.setLineDash(style === 'removed' ? [5, 4] : []); context.beginPath();
      for (const edge of links) {
        const a = byId.get(edge.a), b = byId.get(edge.b);
        if (!a || !b || edgeStyle(a, b) !== style) continue;
        const [x, y] = point(a); context.moveTo(x, y);
        if (Number.isFinite(edge.r) && edge.r > 0) {
          const [ox, oy] = point({ x: edge.cx, y: edge.cy });
          context.arc(ox, oy, edge.r * scale, edge.start, edge.start + edge.sweep, edge.sweep < 0);
        } else { const [xx, yy] = point(b); context.lineTo(xx, yy); }
      }
      context.stroke();
    }
    context.setLineDash([]);
    const visible = nodes.map(n => ({ n, p: point(n) })).filter(({ p }) => p[0] > 0 && p[0] < w && p[1] > 0 && p[1] < h);
    for (const { n, p: [x, y] } of visible) {
      const color = colors[n.change || (n.allocated ? 'active' : 'base')];
      const major = ['Notable', 'Keystone', 'ClassStart', 'AscendClassStart'].includes(n.kind);
      const radius = Math.max(major?4:2, Math.min(n.kind==='Keystone'?26:major?20:12, scale * (n.kind==='Keystone'?140:major?90:50)));
      context.strokeStyle = color; context.fillStyle = n.allocated ? color : getComputedStyle(map).backgroundColor;
      context.lineWidth = n.allocated ? 2 : 1; context.beginPath();
      if (n.kind === 'Keystone') { context.moveTo(x, y-radius); context.lineTo(x+radius, y); context.lineTo(x, y+radius); context.lineTo(x-radius, y); context.closePath(); }
      else if (n.kind === 'Socket') context.rect(x-radius, y-radius, radius*2, radius*2);
      else context.arc(x, y, radius, 0, Math.PI*2);
      context.fill(); context.stroke();
      icon(n,x,y,radius);
      if (n.id === selected || n.change) {
        context.strokeStyle = n.id === selected ? colors.selected : color;
        context.setLineDash(n.change === 'removed' ? [3, 2] : []);
        context.beginPath(); context.arc(x, y, radius+4, 0, Math.PI*2); context.stroke(); context.setLineDash([]);
      }
    }
    // Large invisible input targets do not change the visual size of a passive.
    const focused = Number(document.activeElement?.dataset.node || 0);
    const chosen = [];
    visible.sort((a,b) => Number(b.n.id === selected || b.n.id === focused)-Number(a.n.id === selected || a.n.id === focused) || Number(b.n.allocated)-Number(a.n.allocated));
    for (const item of visible) {
      if (chosen.length >= 100) break;
      if (chosen.every(c => Math.hypot(c.p[0]-item.p[0], c.p[1]-item.p[1]) > 44)) chosen.push(item);
    }
    const retained = new Set();
    for (const { n, p } of chosen) {
      const id = 'passive-' + n.id; retained.add(id);
      let button = document.getElementById(id);
      if (!button) { button = document.createElement('button'); button.id = id; button.className = 'node'; button.dataset.node = n.id; button.onclick = () => pick(n.id); container.append(button); }
      button.style.left = p[0] + 'px'; button.style.top = p[1] + 'px';
      button.onpointerenter=()=>hover(n.id,p[0],p[1]);button.onpointerleave=()=>hover(0,0,0);
      button.onfocus=()=>hover(n.id,p[0],p[1]);button.onblur=()=>hover(0,0,0);
      button.setAttribute('aria-label', `${n.name}, ${n.kind}, ${n.allocated ? 'allocated' : 'unallocated'}${n.change ? ', preview ' + n.change : ''}`);
      button.disabled = preview;
    }
    for (const button of [...container.children]) if (!retained.has(button.id)) button.remove();
    const name = byId.get(selected)?.name;
    status.textContent = !hasSnapshot ? 'Loading passive tree…' : preview ? 'Preview: solid ring adds, dashed ring refunds' : name || 'Select a passive to inspect';
  }
  function fit(ascendancy = false) {
    hover(0,0,0);
    autoFit = true; fitAscendancy = ascendancy;
    if (map.clientWidth < 120 || map.clientHeight < 60) return;
    let list = ascendancy ? nodes.filter(n => n.ascendancy && n.ascendancyName === currentAscendancy)
      : nodes.filter(n => n.allocated && !n.ascendancy);
    if (!ascendancy && !list.length) list = nodes.filter(n => !n.ascendancy);
    if (!list.length) return;
    const xs = list.map(n => n.x), ys = list.map(n => n.y);
    const minX = Math.min(...xs), maxX = Math.max(...xs), minY = Math.min(...ys), maxY = Math.max(...ys);
    cx = (minX+maxX)/2; cy = (minY+maxY)/2;
    const padding = Math.min(40, map.clientHeight * .12, map.clientWidth * .12);
    scale = Math.max(.01, Math.min(.8, (map.clientWidth-padding)/Math.max(600,maxX-minX), (map.clientHeight-padding)/Math.max(600,maxY-minY)));
    redraw();
  }
  document.getElementById('fit').onclick = () => fit();
  document.getElementById('ascendancy').onclick = () => fit(true);
  document.getElementById('locate').onclick = () => { hover(0,0,0);const n=byId.get(selected); if(n){autoFit=false;cx=n.x;cy=n.y;scale=Math.max(scale,.12);redraw();} };
  document.getElementById('in').onclick = () => { hover(0,0,0);autoFit=false;scale=Math.min(1,scale*1.5);redraw(); };
  document.getElementById('out').onclick = () => { hover(0,0,0);autoFit=false;scale=Math.max(.01,scale/1.5);redraw(); };
  for(const [name,dx,dy] of [['left',-1,0],['right',1,0],['up',0,-1],['down',0,1]]) document.getElementById(name).onclick=()=>{hover(0,0,0);autoFit=false;cx+=dx*100/scale;cy+=dy*100/scale;redraw();};
  map.addEventListener('wheel',e=>{
    hover(0,0,0);
    // Chromium delivers mouse-sourced pinch gestures as Ctrl+wheel. Let the
    // shared host zoom handle those; ordinary wheel input pans the tree.
    if(e.ctrlKey)return;
    e.preventDefault();autoFit=false;cx+=e.deltaX/scale;cy+=e.deltaY/scale;redraw();
  },{passive:false});
  let drag=null;
  canvas.addEventListener('pointerdown',e=>{autoFit=false;drag={x:e.clientX,y:e.clientY,cx,cy,moved:false};canvas.setPointerCapture(e.pointerId);});
  canvas.addEventListener('pointermove',e=>{
    if(!drag){const rect=map.getBoundingClientRect(),x=e.clientX-rect.left,y=e.clientY-rect.top;let nearest=0,distance=18;
      for(const n of nodes){const p=point(n),d=Math.hypot(p[0]-x,p[1]-y);if(d<distance){distance=d;nearest=n.id;}}hover(nearest,x,y);return;}
    hover(0,0,0);drag.moved ||= Math.hypot(e.clientX-drag.x,e.clientY-drag.y)>5;cx=drag.cx-(e.clientX-drag.x)/scale;cy=drag.cy-(e.clientY-drag.y)/scale;redraw();});
  canvas.addEventListener('pointerleave',()=>hover(0,0,0));
  canvas.addEventListener('pointerup',e=>{
    if(drag&&!drag.moved){const rect=map.getBoundingClientRect();const x=e.clientX-rect.left,y=e.clientY-rect.top;let best=null,distance=16;
      for(const n of nodes){const p=point(n),d=Math.hypot(p[0]-x,p[1]-y);if(d<distance){distance=d;best=n;}}if(best)pick(best.id);}
    drag=null;
  });
  canvas.addEventListener('pointercancel',()=>drag=null);
  chrome.webview.addEventListener('message',({data:m})=>{
    if(!m||typeof m.type!=='string')return;
    if(m.type==='tooltip'){if(m.revision===revision){tooltipCache.set(m.id,m);showTooltip(m);}return;}
    if(m.type==='begin'){
      if(!Number.isSafeInteger(m.revision)||m.revision<=revision)return;
      revision=m.revision;
      incoming={revision,nodes:m.reuseGeometry?nodes.map(n=>({...n,allocated:false,change:''})):[],links:m.reuseGeometry?links:[]};
      incoming.byId=new Map(incoming.nodes.map(n=>[n.id,n]));return;
    }
    if(m.type==='select'){hover(0,0,0);selected=m.id;const n=byId.get(selected);if(n){autoFit=false;cx=n.x;cy=n.y;scale=Math.max(scale,.14);}redraw();return;}
    if(!incoming||m.revision!==incoming.revision)return;
    if(m.type==='states'&&Array.isArray(m.items))for(const state of m.items){const node=incoming.byId.get(state.id);if(node){node.allocated=!!state.allocated;node.change=state.change||'';node.name=state.name;node.kind=state.kind;node.icon=state.icon||'';}}
    if(m.type==='nodes'&&Array.isArray(m.items)&&incoming.nodes.length+m.items.length<=15000)incoming.nodes.push(...m.items);
    if(m.type==='links'&&Array.isArray(m.items)&&incoming.links.length+m.items.length<=50000)incoming.links.push(...m.items);
    if(m.type==='end'){
      hasSnapshot=true;nodes=incoming.nodes;links=incoming.links;incoming=null;byId=new Map(nodes.map(n=>[n.id,n]));selected=m.selected||0;preview=!!m.preview;
      currentAscendancy=m.ascendancy||'';
      if(treeVersion!==(m.treeVersion||'')){treeVersion=m.treeVersion||'';loadArtwork(treeVersion);}tooltipCache.clear();hover(0,0,0);
      const ascendancyButton=document.getElementById('ascendancy');
      ascendancyButton.disabled=!nodes.some(n=>n.ascendancy&&n.ascendancyName===currentAscendancy);
      ascendancyButton.textContent=ascendancyButton.disabled?'No ascendancy':'Ascendancy';
      ascendancyButton.title=ascendancyButton.disabled?'Select an ascendancy in Build & import':currentAscendancy;
      if(m.preserve)redraw();else fit();
    }
  });
  window.addEventListener('widgetrailthemechange',redraw);
  new ResizeObserver(()=>{if(hasSnapshot&&autoFit)fit(fitAscendancy);else redraw();}).observe(map);WidgetRailWebSession.post({type:'ready'});
})();
