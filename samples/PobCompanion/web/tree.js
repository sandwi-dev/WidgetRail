(() => {
  'use strict';
  const map = document.getElementById('map'), canvas = document.getElementById('lines');
  const container = document.getElementById('nodes'), status = document.getElementById('status');
  const context = canvas.getContext('2d');
  let nodes = [], links = [], byId = new Map(), incoming = null, revision = -1;
  let currentAscendancy = '';
  let treeVersion = '', artwork = null, artEpoch=0, backgrounds=[];
  const sheets = new Map(), tooltipCache = new Map(), tooltip = document.getElementById('tooltip');
  const selectionMarker=document.getElementById('selection-marker');
  let hoverId = 0, hoverTimer = null, hoverX = 0, hoverY = 0;
  function loadArtwork(version){
    const epoch=++artEpoch;
    artwork=null;
    for(const sheet of sheets.values()){sheet.onload=sheet.onerror=null;sheet.src='';}
    sheets.clear();
    if(!/^[A-Za-z0-9_]+$/.test(version))return;
    fetch(`art/${version}/index.json`).then(r=>{if(!r.ok)throw new Error('No artwork');return r.json();})
      .then(data=>{
        const atlasValid=a=>a&&Number.isSafeInteger(a.sheets)&&a.sheets>=0&&a.sheets<=10&&Number.isSafeInteger(a.columns)&&Number.isSafeInteger(a.tile)&&a.columns*a.tile<=512&&a.columns>0&&a.tile>0;
        if(!atlasValid(data)||!atlasValid(data.decorations)||data.layers&&(!Number.isSafeInteger(data.layers.sheets)||data.layers.sheets>8||data.layers.sheets<0||data.layers.size!==1024))return;
        if(artEpoch===epoch){artwork=data;redraw();}
      }).catch(()=>{});
  }
  function sheetImage(key){
    let sheet=sheets.get(key);
    if(!sheet){
      const epoch=artEpoch;
      sheet=new Image();sheet.decoding='async';sheets.set(key,sheet);
      sheet.onload=()=>{if(epoch===artEpoch)redraw();};sheet.onerror=()=>{};
      sheet.src=`art/${treeVersion}/${key}.webp`;
    }
    return sheet.complete&&sheet.naturalWidth?sheet:null;
  }
  function layer(image,x,y,width,height,rotation=0,half=false){
    const sprite=artwork?.layers?.sprites?.[image];
    if(!sprite||!Number.isSafeInteger(sprite.sheet)||sprite.sheet<0||sprite.sheet>=artwork.layers.sheets)return false;
    if(![sprite.x,sprite.y,sprite.w,sprite.h].every(Number.isFinite)||sprite.x<0||sprite.y<0||sprite.w<=0||sprite.h<=0||sprite.x+sprite.w>1024||sprite.y+sprite.h>1024)return false;
    width=width||sprite.width*scale;height=height||sprite.height*scale;
    if(!(width>0&&height>0))return false;
    if(x+width<0||x-width>map.clientWidth||y+height*(half?2:1)<0||y-height*(half?2:1)>map.clientHeight)return false;
    const sheet=sheetImage('layers-'+sprite.sheet);if(!sheet)return false;
    context.save();context.translate(x,y);if(rotation)context.rotate(rotation);
    const paint=()=>context.drawImage(sheet,sprite.x,sprite.y,sprite.w,sprite.h,-width,-height*(half?2:1),width*2,height*2);
    paint();if(half){context.scale(1,-1);paint();}context.restore();return true;
  }
  function nodePath(n,x,y,radius) {
    context.beginPath();
    if(n.kind==='Keystone'){context.moveTo(x,y-radius);context.lineTo(x+radius,y);context.lineTo(x,y+radius);context.lineTo(x-radius,y);context.closePath();}
    else if(n.kind==='Socket')context.rect(x-radius,y-radius,radius*2,radius*2);
    else if(n.kind==='Mastery'||n.landmark){for(let i=0;i<6;i++){const angle=i*Math.PI/3-Math.PI/2;const px=x+Math.cos(angle)*radius,py=y+Math.sin(angle)*radius;i?context.lineTo(px,py):context.moveTo(px,py);}context.closePath();}
    else context.arc(x,y,radius,0,Math.PI*2);
  }
  function icon(n,x,y,radius) {
    const atlas=n.decoration?artwork?.decorations:artwork,index=atlas?.icons?.[n.icon];
    if(!Number.isSafeInteger(index) || index<0 || radius<1.5)return false;
    const perSheet=atlas.columns*atlas.columns, sheetId=Math.floor(index/perSheet),key=(n.decoration?'decorations':'nodes')+'-'+sheetId;
    if(sheetId>=atlas.sheets)return false;
    const sheet=sheetImage(key);if(!sheet)return false;
    const offset=index%perSheet;
    context.save();if(!n.decoration){nodePath(n,x,y,Math.max(.5,radius-.5));context.clip();}
    context.globalAlpha*=n.decoration ? .18 :(n.allocated||n.granted?1:.75);
    context.drawImage(sheet,(offset%atlas.columns)*atlas.tile,Math.floor(offset/atlas.columns)*atlas.tile,atlas.tile,atlas.tile,x-radius,y-radius,radius*2,radius*2);
    context.restore();return true;
  }
  function showTooltip(data) {
    if(data.id!==hoverId)return;
    document.getElementById('tooltip-name').textContent=data.name;
    document.getElementById('tooltip-kind').textContent=`${kindLabel(data)} · ${data.allocated?'Allocated':data.granted?'Granted by item':'Unallocated'}${data.socketedItem?' · '+data.socketedItem:''}`;
    const copy=document.getElementById('tooltip-copy');copy.replaceChildren();
    const tones=new Set(['text','warning','blue','green','gold','orange','violet','muted','emphasis']);
    if(Array.isArray(data.paragraphs))for(const runs of data.paragraphs){
      if(!Array.isArray(runs))continue;
      const paragraph=document.createElement('p');
      for(const run of runs){if(typeof run.text!=='string')continue;const span=document.createElement('span');span.textContent=run.text;span.className='tone-'+(tones.has(run.tone)?run.tone:'text');paragraph.append(span);}
      copy.append(paragraph);
    }else copy.textContent=data.description||'';
    document.getElementById('tooltip-cost').textContent=data.allocated
      ? data.canRefund?`Refund ${data.points} point${data.points===1?'':'s'} including dependants`:'Starting node'
      : data.granted?'Granted by an item; no passive points spent':data.requiresChoice?'Choose this option in desktop PoB'
      : data.canAllocate?`${data.points} point${data.points===1?'':'s'} to allocate path`:'No available allocation path';
    tooltip.hidden=false;
    const rect=map.getBoundingClientRect(),x=rect.left+hoverX,y=rect.top+hoverY;
    const width=tooltip.offsetWidth,height=tooltip.offsetHeight,vw=document.documentElement.clientWidth,vh=document.documentElement.clientHeight;
    const candidates=[[x+24,y+18],[x-width-24,y+18],[x+24,y-height-18],[x-width-24,y-height-18]]
      .map(([left,top])=>({left:Math.max(8,Math.min(left,vw-width-8)),top:Math.max(8,Math.min(top,vh-height-8))}));
    const overlap=p=>Math.max(0,Math.min(p.left+width,x+20)-Math.max(p.left,x-20))*Math.max(0,Math.min(p.top+height,y+20)-Math.max(p.top,y-20));
    candidates.sort((a,b)=>overlap(a)-overlap(b));
    tooltip.style.left=candidates[0].left+'px';tooltip.style.top=candidates[0].top+'px';
  }
  function hover(id,x,y) {
    hoverX=x;hoverY=y;
    if(id===hoverId)return;
    hoverId=id;redraw();clearTimeout(hoverTimer);tooltip.hidden=true;
    if(!id)return;
    hoverTimer=setTimeout(()=>{
      if(tooltipCache.has(id))showTooltip(tooltipCache.get(id));
      else WidgetRailWebSession.post({type:'inspect',id,revision});
    },180);
  }
  let scale = .1, cx = 0, cy = 0, selected = 0, preview = false, queued = false, hasSnapshot = false, autoFit = true, fitAscendancy = false;
  const point = n => [(n.x - cx) * scale + map.clientWidth / 2, (n.y - cy) * scale + map.clientHeight / 2];
  const cssColor = id => getComputedStyle(document.getElementById(id)).color;
  const kindLabel=n=>({Normal:'Small passive',Notable:'Notable passive',Socket:n.socketedItem?'Occupied jewel socket':'Empty jewel socket',Mastery:'Mastery passive',AscendClassStart:'Ascendancy start',ClassStart:'Class start'}[n.kind]||n.kind);
  // Visible geometry scales with the tree. The independent 40px hit targets
  // below remain usable; minimum-size outlines must not turn an overview into rings.
  const nodeRadius=n=>Math.max(.45,Math.min(n.kind==='Keystone'?30:n.kind==='Notable'?22:16,
    scale*(Number.isFinite(n.radius)&&n.radius>0?n.radius:n.kind==='Keystone'?120:n.kind==='Notable'?80:50)));
  function redraw() { if (!queued) { queued = true; requestAnimationFrame(draw); } }
  function pick(id) {
    if (preview || byId.get(id)?.interactive===false || !byId.has(id)) return;
    selected = id; redraw(); WidgetRailWebSession.post({ type: 'select', id });
  }
  function draw() {
    queued = false;
    const w = map.clientWidth, h = map.clientHeight, dpr = devicePixelRatio || 1;
    const pixelWidth=Math.round(w*dpr),pixelHeight=Math.round(h*dpr);
    if(canvas.width!==pixelWidth || canvas.height!==pixelHeight){canvas.width=pixelWidth;canvas.height=pixelHeight;}
    context.setTransform(dpr, 0, 0, dpr, 0, 0);
    context.clearRect(0,0,w,h);
    const colors = { base: cssColor('lines'), active: cssColor('nodes'), added: cssColor('added'), removed: cssColor('removed'), changed: cssColor('selected'), selected: cssColor('selected') };
    const active=n=>n.allocated||n.granted;
    const background=getComputedStyle(map).backgroundColor;
    const highContrast=window.matchMedia?.('(forced-colors: active)').matches===true;
    const visible = nodes.map(n => ({ n, p: point(n) })).filter(({ n,p }) => p[0] > -Math.max(30,(n.radius||50)*scale) && p[0] < w+Math.max(30,(n.radius||50)*scale) && p[1] > -Math.max(30,(n.radius||50)*scale) && p[1] < h+Math.max(30,(n.radius||50)*scale));
    if(!highContrast)for(const bg of backgrounds){
      const [x,y]=point(bg);context.globalAlpha=bg.dimmed?.18:.32;
      const sprite=artwork?.layers?.sprites?.[bg.image];
      if(sprite){
        const width=(bg.width||sprite.width)*scale*(bg.scale||1),height=(bg.height||sprite.height)*scale*(bg.scale||1);
        // At overview zoom these textures cannot communicate useful detail.
        // Keep large class artwork; reveal small group textures as users zoom in.
        if(Math.max(width,height)>=20)layer(bg.image,x,y,width,height,bg.rotation,bg.half);
      }
    }
    context.globalAlpha=1;
    for(const {n,p:[x,y]}of visible)if(n.decoration)icon(n,x,y,(n.radius||190)*scale);
    const edgeStyle = (a, b) => (a.change === 'removed' || b.change === 'removed') && (active(a) || a.change === 'removed') && (active(b) || b.change === 'removed') ? 'removed'
      : active(a) && active(b) && (!(a.allocationMode||0)||!(b.allocationMode||0)||a.allocationMode===b.allocationMode) ? a.change === 'added' || b.change === 'added' ? 'added' : 'active' : 'base';
    for (const dimmed of [true,false]) for (const style of ['base', 'active', 'removed', 'added']) {
      context.globalAlpha=highContrast?1:(dimmed ? .35 : 1)*(style==='base' ? .4 : 1);
      context.strokeStyle=colors[style];context.lineWidth=style==='base' ? (highContrast?1.25:.65) : Math.max(1.2,Math.min(2,scale*20));
      context.setLineDash(style === 'removed' ? [5, 4] : []); context.beginPath();
      for (const edge of links) {
        const a = byId.get(edge.a), b = byId.get(edge.b);
        if (!a || !b || edgeStyle(a, b) !== style || !!(a.dimmed||b.dimmed)!==dimmed) continue;
        const [x, y] = point(a); context.moveTo(x, y);
        if (Number.isFinite(edge.r) && edge.r > 0) {
          const [ox, oy] = point({ x: edge.cx, y: edge.cy });
          context.arc(ox, oy, edge.r * scale, edge.start, edge.start + edge.sweep, edge.sweep < 0);
        } else { const [xx, yy] = point(b); context.lineTo(xx, yy); }
      }
      context.stroke();
    }
    context.setLineDash([]);
    context.globalAlpha=1;
    for (const { n, p: [x, y] } of visible) {
      if(n.decoration)continue;
      const emphasized=active(n)||n.id===selected||!!n.change;
      context.globalAlpha=highContrast?1:(n.dimmed?.35:1)*(emphasized?1:.65);
      const color=colors[n.change||(active(n)?'active':'base')];
      const radius=nodeRadius(n),detailed=radius>=3;
      context.strokeStyle=color;
      context.fillStyle=detailed&&!emphasized?background:color;
      context.lineWidth=highContrast?(emphasized?2:1.5):emphasized?1.5:.75;
      nodePath(n,x,y,radius);context.fill();
      if(detailed)context.stroke();
      const painted=icon(n,x,y,radius);
      const frameSprite=artwork?.layers?.sprites?.[n.art?.frame];
      const frameWidth=n.landmark&&frameSprite?Math.min(80,frameSprite.width*scale*(n.art.scale||1)):radius*1.12;
      const frameHeight=n.landmark&&frameSprite?Math.min(80,frameSprite.height*scale*(n.art.scale||1)):radius*1.12;
      const framed=detailed&&!highContrast&&n.art?.frame&&layer(n.art.frame,x,y,frameWidth,frameHeight);
      const socketPainted=detailed&&!highContrast&&n.art?.socket&&layer(n.art.socket,x,y,radius,radius);
      if(detailed&&n.allocated&&n.allocationMode>0){context.fillStyle=colors.active;context.font='bold 11px sans-serif';context.textAlign='left';context.textBaseline='bottom';context.fillText(String(n.allocationMode),x+radius+2,y-radius-2);}
      if(!framed&&n.kind==='Notable'&&radius>=5){nodePath(n,x,y,radius+1.5);context.stroke();}
      if(!socketPainted&&n.socketedItem&&radius>=3){nodePath({kind:'Keystone'},x,y,radius*.45);context.fillStyle=colors.active;context.fill();}
      else if(!painted&&!framed&&n.landmark&&radius>=7){context.fillStyle=color;context.font=`bold ${Math.max(9,radius)}px sans-serif`;context.textAlign='center';context.textBaseline='middle';context.fillText(n.kind==='ClassStart'?'C':'A',x,y);}
      if(n.granted&&!n.allocated&&detailed){context.setLineDash([2,2]);nodePath(n,x,y,radius+2);context.stroke();context.setLineDash([]);}
      if (n.id === selected || n.change) {
        context.globalAlpha=1;
        context.strokeStyle = n.id === selected ? colors.selected : color;
        context.setLineDash(n.change === 'removed' ? [3, 2] : []);
        context.beginPath(); context.arc(x, y, radius+4, 0, Math.PI*2); context.stroke(); context.setLineDash([]);
      }
    }
    // Radius is an inspection aid: show only the selected/hovered socket.
    // Numeric radii and alternative centers are supplied by PoB; no effect rules live here.
    const inspected=byId.get(hoverId||selected);
    for(const ring of Array.isArray(inspected?.art?.radii)?inspected.art.radii:[]){
      if(!Number.isFinite(ring.outer)||ring.outer<=0)continue;
      const [x,y]=point(ring);
      if(!highContrast){context.globalAlpha=.3;layer('ShadedOuterRing',x,y,ring.outer*scale,ring.outer*scale);if(ring.inner>0)layer('ShadedInnerRing',x,y,ring.inner*scale,ring.inner*scale);}
      context.globalAlpha=highContrast?1:.7;context.strokeStyle=colors.selected;context.lineWidth=1.5;
      context.beginPath();context.arc(x,y,ring.outer*scale,0,Math.PI*2);context.stroke();
      if(ring.inner>0){context.setLineDash([4,3]);context.beginPath();context.arc(x,y,ring.inner*scale,0,Math.PI*2);context.stroke();context.setLineDash([]);}
    }
    context.globalAlpha=1;
    const target=byId.get(selected),position=target?point(target):null;
    const selectionVisible=target?.interactive!==false&&position&&position[0]>=0&&position[0]<=w&&position[1]>=0&&position[1]<=h;
    selectionMarker.hidden=!selectionVisible;
    if(selectionVisible){
      const [x,y]=position,radius=Math.max(22,nodeRadius(target)+8);
      Object.assign(selectionMarker.style,{left:x+'px',top:y+'px',width:radius*2+'px',height:radius*2+'px'});
    }
    // Large invisible input targets do not change the visual size of a passive.
    const focused = Number(document.activeElement?.dataset.node || 0);
    const chosen = [];
    visible.sort((a,b) => Number(b.n.id === selected || b.n.id === focused)-Number(a.n.id === selected || a.n.id === focused) || Number(b.n.allocated)-Number(a.n.allocated));
    for (const item of visible) {
      if(item.n.interactive===false||item.p[0]<0||item.p[0]>w||item.p[1]<0||item.p[1]>h)continue;
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
      button.setAttribute('aria-label', `${n.name}, ${kindLabel(n)}, ${n.allocated ? 'allocated'+(n.allocationMode>0?' to weapon set '+n.allocationMode:'') : n.granted?'granted by item':'unallocated'}${n.change ? ', preview ' + n.change : ''}`);
      button.setAttribute('aria-pressed',String(n.id===selected));
      button.disabled = preview;
    }
    for (const button of [...container.children]) if (!retained.has(button.id)) button.remove();
    const name = byId.get(selected)?.name;
    status.textContent = !hasSnapshot ? 'Loading passive tree…' : preview ? 'Preview: solid ring adds, dashed ring refunds' : name ? name+(selectionVisible?'':' · Off screen — Locate selected') : 'Select a passive to inspect';
  }
  function fit(ascendancy = false) {
    hover(0,0,0);
    autoFit = true; fitAscendancy = ascendancy;
    if (map.clientWidth < 120 || map.clientHeight < 60) return;
    let list = ascendancy ? nodes.filter(n => !n.decoration && n.ascendancy && n.ascendancyName === currentAscendancy)
      : nodes.filter(n => (n.allocated||n.granted) && !n.decoration && !n.ascendancy);
    if (!ascendancy && !list.length) list = nodes.filter(n => !n.decoration && !n.ascendancy);
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
  function locateSelected(){hover(0,0,0);const n=byId.get(selected);if(n){autoFit=false;cx=n.x;cy=n.y;scale=Math.max(scale,.3);redraw();}}
  document.getElementById('locate').onclick = locateSelected;
  function zoom(direction) { hover(0,0,0);autoFit=false;scale=Math.max(.01,Math.min(1,scale*(direction>0?1.5:1/1.5)));redraw(); }
  document.getElementById('in').onclick = () => zoom(1);
  document.getElementById('out').onclick = () => zoom(-1);
  window.addEventListener('widgetrailzoom',e=>{
    if(e.detail?.direction!==1 && e.detail?.direction!==-1)return;
    e.preventDefault();zoom(e.detail.direction);
  });
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
      for(const n of nodes){if(n.interactive===false)continue;const p=point(n),d=Math.hypot(p[0]-x,p[1]-y);if(d<distance){distance=d;nearest=n.id;}}hover(nearest,x,y);return;}
    hover(0,0,0);drag.moved ||= Math.hypot(e.clientX-drag.x,e.clientY-drag.y)>5;cx=drag.cx-(e.clientX-drag.x)/scale;cy=drag.cy-(e.clientY-drag.y)/scale;redraw();});
  canvas.addEventListener('pointerleave',()=>hover(0,0,0));
  canvas.addEventListener('pointerup',e=>{
    if(drag&&!drag.moved){const rect=map.getBoundingClientRect();const x=e.clientX-rect.left,y=e.clientY-rect.top;let best=null,distance=16;
      for(const n of nodes){if(n.interactive===false)continue;const p=point(n),d=Math.hypot(p[0]-x,p[1]-y);if(d<distance){distance=d;best=n;}}if(best)pick(best.id);}
    drag=null;
  });
  canvas.addEventListener('pointercancel',()=>drag=null);
  chrome.webview.addEventListener('message',({data:m})=>{
    if(!m||typeof m.type!=='string')return;
    if(m.type==='tooltip'){if(m.revision===revision){tooltipCache.set(m.id,m);showTooltip(m);}return;}
    if(m.type==='begin'){
      if(!Number.isSafeInteger(m.revision)||m.revision<=revision)return;
      revision=m.revision;
      incoming={revision,backgrounds:[],nodes:m.reuseGeometry?nodes.map(n=>({...n,allocated:false,granted:false,change:''})):[],links:m.reuseGeometry?links:[]};
      incoming.byId=new Map(incoming.nodes.map(n=>[n.id,n]));return;
    }
    if(m.type==='select'){selected=m.id;locateSelected();return;}
    if(!incoming||m.revision!==incoming.revision)return;
    if(m.type==='artwork'&&Array.isArray(m.items)&&incoming.backgrounds.length+m.items.length<=3000)incoming.backgrounds.push(...m.items);
    if(m.type==='states'&&Array.isArray(m.items))for(const state of m.items){const node=incoming.byId.get(state.id);if(node)Object.assign(node,state);}
    if(m.type==='nodes'&&Array.isArray(m.items)&&incoming.nodes.length+m.items.length<=15000)incoming.nodes.push(...m.items);
    if(m.type==='links'&&Array.isArray(m.items)&&incoming.links.length+m.items.length<=50000)incoming.links.push(...m.items);
    if(m.type==='end'){
      hasSnapshot=true;nodes=incoming.nodes;links=incoming.links;backgrounds=incoming.backgrounds;incoming=null;byId=new Map(nodes.map(n=>[n.id,n]));selected=m.selected||0;preview=!!m.preview;
      currentAscendancy=m.ascendancy||'';
      const weaponLegend=document.getElementById('weapon-legend');if(weaponLegend)weaponLegend.hidden=!m.weaponTreeModes;
      if(treeVersion!==(m.treeVersion||'')){treeVersion=m.treeVersion||'';loadArtwork(treeVersion);}tooltipCache.clear();hover(0,0,0);
      const ascendancyButton=document.getElementById('ascendancy');
      ascendancyButton.disabled=!nodes.some(n=>n.ascendancy&&n.ascendancyName===currentAscendancy);
      ascendancyButton.textContent=ascendancyButton.disabled?'No ascendancy':'Ascendancy';
      ascendancyButton.title=ascendancyButton.disabled?'Select an ascendancy in Build & import':currentAscendancy;
      if(m.preserve)redraw();else fit();
    }
  });
  window.addEventListener('widgetrailthemechange',redraw);
  // WebView can report a backing-scale/viewport change without changing the
  // map's CSS box. Use both native viewport and resolution notifications.
  window.addEventListener('resize',redraw);
  // Monitor-scale changes need a new canvas backing buffer even if CSS bounds stay put.
  let resolutionQuery=null;
  function watchResolution(){
    resolutionQuery?.removeEventListener?.('change',resolutionChanged);
    // Chromium can expose values such as 1.0000000149 while CSS resolution
    // comparison rounds differently. Keep the current ratio inside a tiny band
    // so this query is true now and reliably changes on the next scale change.
    const ratio=devicePixelRatio||1;
    resolutionQuery=window.matchMedia?.(`(min-resolution: ${Math.max(0,ratio-.0001)}dppx) and (max-resolution: ${ratio+.0001}dppx)`);
    resolutionQuery?.addEventListener?.('change',resolutionChanged);
  }
  function resolutionChanged(){watchResolution();redraw();}
  watchResolution();
  new ResizeObserver(()=>{if(hasSnapshot&&autoFit)fit(fitAscendancy);else redraw();})
    .observe(map,{box:'device-pixel-content-box'});
  WidgetRailWebSession.post({type:'ready'});
})();
