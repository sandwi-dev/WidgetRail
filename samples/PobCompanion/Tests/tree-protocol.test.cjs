const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

// Exercise the real page's message handler and resulting accessible node labels.
// The canvas is stubbed; native screenshots validate its actual drawing.
const elements = new Map(), frames = [], sent = [];
class Element {
  constructor(id) { this.id = id; this.children = []; this.style = {}; this.dataset = {}; this.attributes = {}; this.clientWidth = 1000; this.clientHeight = 600; }
  append(child) { child.parent = this; this.children.push(child); elements.set(child.id, child); }
  remove() { this.parent.children = this.parent.children.filter(child => child !== this); elements.delete(this.id); }
  setAttribute(name, value) { this.attributes[name] = value; }
  addEventListener(type,handler) { (this.events ??= {})[type]=handler; }
  getContext() { return new Proxy({}, { get: () => () => {} }); }
  getBoundingClientRect() { return {left:0,top:0}; }
}
for (const id of ['map','lines','nodes','status','added','removed','selected','fit','ascendancy','locate','in','out','left','right','up','down','tooltip','tooltip-name','tooltip-kind','tooltip-copy','tooltip-cost']) elements.set(id, new Element(id));
let receive, resize;
const sandbox = {
  document: { getElementById: id => elements.get(id), createElement: () => new Element(), activeElement: null, documentElement:{clientWidth:1000,clientHeight:600,dataset:{}} },
  chrome: { webview: { addEventListener: (_, callback) => receive = callback } },
  WidgetRailWebSession: { post: value => sent.push(value) },
  window: { addEventListener() {} }, ResizeObserver: class { constructor(callback) { resize=callback; } observe() {} },
  getComputedStyle: () => ({ color: '#fff', backgroundColor: '#000' }), devicePixelRatio: 1,
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
assert.match(label(1), /Strength, Notable/);
assert.match(label(2), /preview added/);
assert.equal(elements.get('passive-1').disabled, true);
message({ type: 'begin', revision: 3, reuseGeometry: true });
message({ type: 'states', revision: 3, items: [{ id: 1, allocated: true, name: 'Attribute', kind: 'Normal', change: '' }] });
message({ type: 'end', revision: 3, preserve: true, preview: false, selected: 1 });
assert.match(label(1), /Attribute, Normal/);
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
