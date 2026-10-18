const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

// Exercise the real page's message handler and resulting accessible node labels.
// The canvas is stubbed; native screenshots validate its actual drawing.
const elements = new Map(), frames = [], sent = [], windowEvents = new Map(), drawCalls=[];
let styleReads=0;
const drawing=new Proxy({}, {get:(target,key)=>key in target?target[key]:(...args)=>drawCalls.push({op:key,args,alpha:target.globalAlpha,width:target.lineWidth})});
class Element {
  constructor(id,tag='div') { this.id = id; this.tagName=tag; this.children = []; this.style = {}; this.dataset = {}; this.attributes = {}; this.clientWidth = 1000; this.clientHeight = 600; }
  get textContent(){return this.children.length?this.children.map(c=>c.textContent).join(''):(this.text||'');}
  set textContent(value){this.text=value;this.children=[];}
  get offsetWidth(){return this.id==='tooltip'?380:200;}
  get offsetHeight(){return this.id==='tooltip'?300:32;}
  replaceChildren(){this.children=[];this.text='';}
  append(child) { child.parent = this; this.children.push(child); elements.set(child.id, child); }
  remove() { this.parent.children = this.parent.children.filter(child => child !== this); elements.delete(this.id); }
  setAttribute(name, value) { this.attributes[name] = value; }
  addEventListener(type,handler) { (this.events ??= {})[type]=handler; }
  getContext() { return drawing; }
  get width(){return this.pixelWidth;}
  set width(value){this.pixelWidth=value;this.widthWrites=(this.widthWrites||0)+1;}
  get height(){return this.pixelHeight;}
  set height(value){this.pixelHeight=value;this.heightWrites=(this.heightWrites||0)+1;}
  getBoundingClientRect() { return {left:0,top:0}; }
}
for (const id of ['map','lines','nodes','status','added','removed','selected','fit','ascendancy','locate','in','out','left','right','up','down','tooltip','tooltip-name','tooltip-kind','tooltip-copy','tooltip-cost','selection-marker','selection-label']) elements.set(id, new Element(id));
let receive, resize;
const sandbox = {
  document: { getElementById: id => elements.get(id), createElement: tag => new Element(undefined,tag), activeElement: null, documentElement:{clientWidth:1000,clientHeight:600,dataset:{}} },
  chrome: { webview: { addEventListener: (_, callback) => receive = callback } },
  WidgetRailWebSession: { post: value => sent.push(value) },
  window: { addEventListener(type, handler) { windowEvents.set(type,handler); } }, ResizeObserver: class { constructor(callback) { resize=callback; } observe() {} },
  getComputedStyle: () => {styleReads++;return { color: '#fff', backgroundColor: '#000' };}, devicePixelRatio: 1,
  requestAnimationFrame: callback => frames.push(callback),
  fetch:()=>Promise.reject(new Error('No fixture artwork')),setTimeout:callback=>{callback();return 1;},clearTimeout(){},
};
vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../web/tree.js'), 'utf8'), sandbox);
function message(data) { receive({ data }); while (frames.length) frames.shift()(); }
function label(id) { return elements.get('passive-' + id)?.attributes['aria-label']; }
const node = (id, x, name, allocated) => ({ id, x, y: 0, name, allocated, ascendancy: false, kind: 'Normal', change: '' });
assert.equal(sent[0].type, 'ready');
message({ type: 'begin', revision: 1 });
message({ type: 'nodes', revision: 1, items: [node(1, 0, 'Attribute', true), node(2, 180, 'Second', false)] });
message({ type: 'links', revision: 1, items: [{ a: 1, b: 2 }] });
assert.equal(label(1), undefined, 'Partial full snapshots are not displayed');
message({ type: 'end', revision: 1, selected: 1 });
assert.match(label(1), /Attribute.*allocated/);
message({ type: 'begin', revision: 2, reuseGeometry: true });
message({ type: 'states', revision: 2, items: [{ id: 1, allocated: true, name: 'Strength', kind: 'Notable', change: '' }, { id: 2, allocated: true, name: 'Second', kind: 'Normal', change: 'added' }] });
assert.match(label(1), /Attribute/, 'Delta is atomic until its end marker');
message({ type: 'nodes', revision: 1, items: [node(99, 0, 'Stale', true)] });
message({ type: 'end', revision: 1 });
assert.equal(label(99), undefined, 'Late chunks cannot contaminate a newer transaction');
message({ type: 'end', revision: 2, preserve: true, preview: true, selected: 1 });
assert.match(label(1), /Strength, Notable passive/);
assert.match(label(2), /preview added/);
assert.equal(elements.get('passive-1').disabled, true);
message({ type: 'begin', revision: 3, reuseGeometry: true });
message({ type: 'states', revision: 3, items: [{ id: 1, allocated: true, name: 'Attribute', kind: 'Normal', change: '' }] });
message({ type: 'end', revision: 3, preserve: true, preview: false, selected: 1 });
assert.match(label(1), /Attribute, Small passive/);
assert.match(label(2), /unallocated$/);
assert.equal(elements.get('passive-1').disabled, false);
message({ type: 'begin', revision: 2 });
message({ type: 'end', revision: 2 });
assert.match(label(1), /Attribute/, 'An old begin cannot replace the committed tree');
elements.get('passive-2').onclick();
assert.equal(sent.at(-1).id, 2);
const map = elements.get('map');
let prevented=false;
map.events.wheel({ctrlKey:true,deltaX:0,deltaY:100,preventDefault(){prevented=true;}});
assert.equal(prevented,false,'Mouse-sourced pinch must reach the browser');
map.events.wheel({ctrlKey:false,deltaX:0,deltaY:100,preventDefault(){prevented=true;}});
assert.equal(prevented,true,'Ordinary wheel still pans the tree');
assert.equal(elements.get('ascendancy').disabled,true,'No selected ascendancy must not fall back to main tree');
message({type:'begin',revision:4});
message({type:'nodes',revision:4,items:[node(1,0,'Main',true),{...node(3,8000,'Ascendancy passive',false),ascendancy:true,ascendancyName:'Test ascendancy'}]});
message({type:'end',revision:4,ascendancy:'Test ascendancy'});
assert.equal(elements.get('ascendancy').disabled,false);
elements.get('ascendancy').onclick();while(frames.length)frames.shift()();
assert.equal(parseFloat(elements.get('passive-3').style.left),map.clientWidth/2,'Frame the selected ascendancy before any nodes are allocated');
elements.get('passive-3').onpointerenter();
assert.equal(sent.at(-1).type,'inspect');
message({type:'tooltip',revision:4,id:3,name:'Test passive',kind:'Notable',description:'Adds damage',allocated:false,canAllocate:true,points:2});
assert.equal(elements.get('tooltip-copy').textContent,'Adds damage');
message({type:'tooltip',revision:3,id:3,name:'Stale',description:'Wrong'});
assert.equal(elements.get('tooltip-copy').textContent,'Adds damage','Old tooltip replies cannot replace current information');
elements.get('fit').onclick();while(frames.length)frames.shift()();
assert.equal(elements.get('tooltip').hidden,true,'Changing tree framing dismisses the tooltip for the previous location');
message({type:'tooltip',revision:4,id:3,name:'Late reply',description:'Wrong location'});
assert.equal(elements.get('tooltip').hidden,true,'Late hover replies cannot reopen a tooltip after moving the map');
console.log('PASS real tree page: atomic full/delta updates, changed labels/shapes, stale transactions, discard and selection');
message({type:'begin',revision:5});
message({type:'nodes',revision:5,items:[node(1,-400,'Left',true),node(2,400,'Right',true)]});
message({type:'end',revision:5});
const position=()=>elements.get('passive-2').style.left;
elements.get('out').onclick();while(frames.length)frames.shift()();
const beforeZoom=position();
elements.get('in').onclick();while(frames.length)frames.shift()();
const buttonZoom=position();
elements.get('out').onclick();while(frames.length)frames.shift()();
let consumed=false;
windowEvents.get('widgetrailzoom')({detail:{direction:1},preventDefault(){consumed=true;}});
while(frames.length)frames.shift()();
assert.equal(consumed,true,'Tree consumes controller zoom before browser pinch');
assert.equal(position(),buttonZoom,'R2 uses exactly the plus-button map transform');
windowEvents.get('widgetrailzoom')({detail:{direction:-1},preventDefault(){}});
while(frames.length)frames.shift()();
assert.ok(Math.abs(parseFloat(position())-parseFloat(beforeZoom))<.001,'L2 reverses the internal zoom step');
console.log('PASS controller zoom shares minus/plus controls and consumes browser zoom');
message({type:'select',id:2});
const marker=elements.get('selection-marker'),selectedLabel=elements.get('selection-label');
assert.equal(marker.hidden,false);
assert.match(selectedLabel.textContent,/Selected.*Right/);
for(let i=0;i<12;i++){elements.get('out').onclick();while(frames.length)frames.shift()();}
assert.ok(parseFloat(marker.style.width)>=44,'Selection stays large enough to spot at minimum zoom');
for(let i=0;i<20;i++)elements.get('right').onclick();while(frames.length)frames.shift()();
assert.equal(marker.hidden,true);
assert.match(elements.get('status').textContent,/Off screen/);
elements.get('locate').onclick();while(frames.length)frames.shift()();
assert.equal(parseFloat(marker.style.left),map.clientWidth/2,'Locate selected centers the actual marker');
assert.ok(Math.abs(parseFloat(elements.get('passive-1').style.left)-parseFloat(elements.get('passive-2').style.left))>=240,'Locate selected zooms enough to distinguish adjacent nodes');
elements.get('passive-2').onpointerenter();
message({type:'tooltip',revision:5,id:2,name:'Right',kind:'Keystone',paragraphs:[[{text:'+10 ',tone:'gold'},{text:'Energy Shield',tone:'blue'}],[{text:'<script>plain text</script>',tone:'text'}]],allocated:false,canAllocate:true,points:3});
const copy=elements.get('tooltip-copy');
assert.equal(copy.children.length,2,'Hover keeps distinct modifiers in separate paragraphs');
assert.equal(copy.children[0].children[1].className,'tone-blue');
assert.equal(copy.children[1].children[0].textContent,'<script>plain text</script>','Hover inserts source text without evaluating markup');
assert.ok(parseFloat(elements.get('tooltip').style.left)>=8&&parseFloat(elements.get('tooltip').style.left)+380<=992,'Hover stays within horizontal viewport bounds');
assert.ok(parseFloat(elements.get('tooltip').style.top)>=8&&parseFloat(elements.get('tooltip').style.top)+300<=592,'Hover stays within vertical viewport bounds');
console.log('PASS persistent selection marker, useful locate zoom, rich hover and bounded placement');
message({type:'begin',revision:6});
message({type:'nodes',revision:6,items:[
  {...node(10,-500,'Decorative mastery',false),kind:'OnlyImage',decoration:true,interactive:false},
  {...node(11,0,'Actual mastery',true),kind:'Mastery',interactive:true},
  {...node(12,500,'Jewel socket',true),kind:'Socket',interactive:true,socketedItem:'Test jewel'},
  {...node(13,1000,'Class start',true),kind:'ClassStart',landmark:true,interactive:false},
  {...node(14,1500,'Item passive',false),interactive:true,granted:true}]});
message({type:'end',revision:6,selected:10});
assert.equal(label(10),undefined,'Decorative art never creates a passive button');
assert.equal(label(13),undefined,'Class landmarks never create an allocation button');
assert.equal(marker.hidden,true,'A stale decorative selection cannot show the selected-passive marker');
assert.match(label(11),/Mastery passive/);
assert.match(label(12),/Occupied jewel socket/);
assert.match(label(14),/granted by item/);
elements.get('passive-11').onpointerenter();
message({type:'tooltip',revision:6,id:11,name:'Actual mastery',kind:'Mastery',allocated:true,canRefund:true,requiresChoice:true,points:1});
assert.match(elements.get('tooltip-cost').textContent,/Refund 1 point/,'Allocated masteries advertise refund rather than an unavailable choice');
message({type:'begin',revision:7,reuseGeometry:true});
message({type:'states',revision:7,items:[{id:12,allocated:true,socketedItem:''}]});
message({type:'end',revision:7,preserve:true});
assert.match(label(12),/Empty jewel socket/,'Socket removal updates reused geometry');
assert.match(label(14),/unallocated$/,'Removed item grants do not retain active state');
message({type:'begin',revision:8});
message({type:'nodes',revision:8,items:[node(11,0,'Remaining passive',true)]});
message({type:'end',revision:8});
assert.equal(label(12),undefined,'Visibility replacement removes old hit targets');
console.log('PASS decorations, landmarks, real masteries, occupied sockets, item grants and visibility replacement');

// A large overview must not inflate every node into a screen-sized outline.
const dense=Array.from({length:400},(_,i)=>({...node(1000+i,(i%20)*1000,'Passive '+i,false),y:Math.floor(i/20)*1000,radius:50}));
message({type:'begin',revision:9});message({type:'nodes',revision:9,items:dense});
drawCalls.length=0;styleReads=0;
message({type:'end',revision:9});
const overviewArcs=drawCalls.filter(call=>call.op==='arc');
assert.ok(overviewArcs.length>100,'Overview continues to render its visible nodes');
assert.ok(overviewArcs.every(call=>call.args[2]<2),'Overview nodes scale down instead of accumulating minimum-sized rings');
assert.ok(styleReads<=10,'Style lookups are per frame, not per node');
assert.ok(elements.get('nodes').children.length<=100,'Accessible hit targets remain bounded');
const canvas=elements.get('lines'),widthWrites=canvas.widthWrites;
for(let i=0;i<8;i++)elements.get('right').onclick();
assert.equal(frames.length,1,'Rapid navigation coalesces into one redraw');
while(frames.length)frames.shift()();
assert.equal(canvas.widthWrites,widthWrites,'Panning does not reallocate the canvas backing buffer');
map.clientWidth=800;resize();while(frames.length)frames.shift()();
assert.equal(canvas.width,800,'Real resizes update the drawing buffer');
assert.equal(frames.length,0,'The map does not schedule a continuous idle render loop');
console.log('PASS overview detail level, bounded targets/style reads, coalesced draws and stable canvas allocation');
