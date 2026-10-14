namespace WidgetRail.OverlayFrontend.WinUI.Browser;

/// <summary>Fixed scripts executed in an isolated world; return geometry, never page text/values.</summary>
internal static class BrowserSpatialScript
{
    internal const string Scan = """
    ((p, anchor = null) => {
      const items=[], frames=[], start=performance.now();
      const vv=visualViewport, view={x:vv?.offsetLeft??0,y:vv?.offsetTop??0,w:vv?.width??innerWidth,h:vv?.height??innerHeight};
      let styles=new WeakMap(), clips=new WeakMap();
      const style=e=>{let s=styles.get(e);if(!s){s=getComputedStyle(e);styles.set(e,s);}return s;};
      const parent=e=>e.assignedSlot??e.parentElement??e.getRootNode()?.host??null;
      const inside=(root,e)=>{for(let n=0;e&&n<128;e=parent(e),n++)if(e===root)return true;return false;};
      const inScope=e=>!anchor||anchor.container.isConnected&&inside(anchor.container,e);
      const rect=r=>({x:r.left,y:r.top,w:r.width,h:r.height});
      const intersect=(a,b)=>({x:Math.max(a.x,b.x),y:Math.max(a.y,b.y),w:Math.max(0,Math.min(a.x+a.w,b.x+b.w)-Math.max(a.x,b.x)),h:Math.max(0,Math.min(a.y+a.h,b.y+b.h)-Math.max(a.y,b.y))});
      const roles=new Set(['button','link','checkbox','radio','switch','tab','menuitem','menuitemcheckbox','menuitemradio','option','treeitem','slider','spinbutton','textbox','combobox']);
      function semantic(e){
        const tag=e.localName;
        if(tag==='label'&&e.control&&!e.control.matches(':disabled')&&e.control.type!=='hidden'){
          const control=e.control,s=style(control),r=control.getBoundingClientRect();
          // Custom checkboxes often hide the native input and expose its label.
          // Prefer the input itself when it has a visible, usable hit rectangle.
          if(r.width<2||r.height<2||s.display==='none'||s.visibility!=='visible'||Number(s.opacity)===0)return true;
        }
        return ['button','select','textarea','summary'].includes(tag)||tag==='input'&&e.type!=='hidden'||
          (tag==='a'||tag==='area')&&e.hasAttribute('href')||roles.has(e.getAttribute('role'))||
          e.isContentEditable&&!parent(e)?.isContentEditable||e.hasAttribute('onclick');
      }
      function eligible(e){
        if(e.matches(':disabled')||e.getAttribute('aria-disabled')==='true'||e.inert)return false;
        if(semantic(e))return true;
        return style(e).cursor==='pointer'&&(!parent(e)||style(parent(e)).cursor!=='pointer');
      }
      function clip(e,depth=0){
        if(!e)return view;
        if(depth>128)return null;
        if(clips.has(e))return clips.get(e);
        const s=style(e), up=parent(e);
        let c=up?clip(up,depth+1):view;
        if(!c||e.hidden||e.inert||e.getAttribute('aria-hidden')==='true'||e.getAttribute('aria-disabled')==='true'||
          s.display==='none'||Number(s.opacity)===0){clips.set(e,null);return null;}
        if(e!==document.documentElement&&e!==document.body){
          const r=e.getBoundingClientRect();
          if(/hidden|clip|scroll|auto/.test(s.overflowX))c=intersect(c,{x:r.left,y:c.y,w:r.width,h:c.h});
          if(/hidden|clip|scroll|auto/.test(s.overflowY))c=intersect(c,{x:c.x,y:r.top,w:c.w,h:r.height});
        }
        clips.set(e,c);return c;
      }
      function hit(x,y){
        let e=document.elementFromPoint(x,y);
        for(let n=0;e?.shadowRoot&&n<16;n++){const child=e.shadowRoot.elementFromPoint(x,y);if(!child||child===e)break;e=child;}
        return e;
      }
      function owns(e,x,y){
        for(let h=hit(x,y),n=0;h&&n<128;h=parent(h),n++){
          if(h===e)return true;
          if(h.localName==='iframe'||eligible(h))return false;
        }
        return false;
      }
      function point(e,r){
        const pad=Math.min(4,r.w/4,r.h/4), xs=[r.x+r.w/2,r.x+pad,r.x+r.w-pad],ys=[r.y+r.h/2,r.y+pad,r.y+r.h-pad];
        for(const y of ys)for(const x of xs)if(owns(e,x,y))return {x,y};
        return null;
      }
      const sourcePath=new Set();
      // Keyboard-focusable regions are not necessarily pointer actions. After a
      // scroll, preserve the actual source instead of hit-testing its old spot
      // against unrelated fixed headers or recycled rows.
      for(let e=anchor?anchor.source:hit(p.x,p.y),n=0;e&&n<128;e=parent(e),n++)sourcePath.add(e);
      let visited=0,truncated=false;
      function walk(root){
        const walker=document.createTreeWalker(root,NodeFilter.SHOW_ELEMENT);
        let e=root instanceof Element?root:walker.nextNode();
        while(e){
          if(++visited>12000||items.length>=512||visited%128===0&&performance.now()-start>20){truncated=true;return;}
          const s=style(e), c=clip(e);
          if(c){
            if(e.localName==='iframe'&&frames.length<16&&inScope(e)){
              let valid=true;
              for(let a=e,n=0;a&&n<128;a=parent(a),n++){
                const transform=style(a).transform;
                if(transform!=='none'){const m=new DOMMatrixReadOnly(transform);if(!m.is2D||m.a<=0||m.d<=0||Math.abs(m.b)>.001||Math.abs(m.c)>.001){valid=false;break;}}
              }
              const r=e.getBoundingClientRect();let visible=intersect(rect(r),c);
              // Fixed/positioned descendants can escape an ancestor's overflow.
              // Actual browser hit testing is the final authority in that case.
              if(visible.w<2||visible.h<2||!point(e,visible))visible=intersect(rect(r),view);
              if(valid&&e.offsetWidth>0&&e.offsetHeight>0&&visible.w>1&&visible.h>1){
                const sx=r.width/e.offsetWidth,sy=r.height/e.offsetHeight;
                // Confirm the frame itself isn't hidden by another surface.
                const sample=point(e,visible);
                if(sample)frames.push({element:e,x:r.left+e.clientLeft*sx,y:r.top+e.clientTop*sy,sx,sy,w:e.clientWidth,h:e.clientHeight,clip:visible});
              }
            }else if(eligible(e)&&inScope(e)){
              for(const box of e.getClientRects()){
                let found=false;
                for(const r of [intersect(rect(box),c),intersect(rect(box),view)]){
                  if(r.w<2||r.h<2)continue;
                  const at=point(e,r);
                  if(at){items.push({element:e,rect:r,point:at,source:sourcePath.has(e)});found=true;break;}
                }
                if(found)break;
              }
            }
            if(e.shadowRoot)walk(e.shadowRoot);
          }
          e=walker.nextNode();
        }
      }
      if(!anchor||anchor.container.isConnected)walk(anchor?.container??document.documentElement);
      return {items,frames,view,visited,truncated,elapsed:performance.now()-start,point,clip,hit,parent,style,intersect,rect,p,eligible,inScope,inside,
        refresh(){styles=new WeakMap();clips=new WeakMap();}};
    })
    """;
    internal const string Describe = """
    function(){return {view:this.view,visited:this.visited,truncated:this.truncated,elapsed:this.elapsed,
      items:this.items.map((i,index)=>({index,rect:i.rect,point:i.point,source:i.source})),
      frames:this.frames.map((f,index)=>({index,x:f.x,y:f.y,sx:f.sx,sy:f.sy,w:f.w,h:f.h,clip:f.clip}))};}
    """;
    internal const string FrameElement = "function(raw){return this.frames[Number(raw)]?.element??null;}";
    internal const string Resolve = """
    function(raw){
      const e=this.items[Number(raw)]?.element;
      if(!e?.isConnected)return null;
      // Build a fresh hit-tested snapshot: stale geometry cannot authorize a move.
      this.refresh();
      if(!this.inScope(e)||!this.eligible(e))return null;
      const c=this.clip(e);if(!c)return null;
      for(const box of e.getClientRects()){
        for(const clipped of [this.intersect(this.rect(box),c),this.intersect(this.rect(box),this.view)]){
          if(clipped.w<2||clipped.h<2)continue;
          const at=this.point(e,clipped);if(at)return at;
        }
      }
      return null;
    }
    """;
    internal const string ValidateFrame = """
    function(raw){
      const p=JSON.parse(raw),f=this.frames[p.index];if(!f?.element.isConnected)return false;
      this.refresh();
      const e=f.element,r=e.getBoundingClientRect(),sx=r.width/e.offsetWidth,sy=r.height/e.offsetHeight;
      if(Math.abs(r.left+e.clientLeft*sx-f.x)>1||Math.abs(r.top+e.clientTop*sy-f.y)>1||Math.abs(sx-f.sx)>.001||Math.abs(sy-f.sy)>.001)return false;
      const c=this.clip(e);
      return !!c&&p.x>=this.view.x&&p.x<=this.view.x+this.view.w&&p.y>=this.view.y&&p.y<=this.view.y+this.view.h&&this.hit(p.x,p.y)===e;
    }
    """;
    internal const string Scroll = """
    function(raw){
      const d=JSON.parse(raw), horizontal=d.x!==0;
      const source=this.items.findLast(i=>i.source)?.element;
      for(let e=this.hit(this.p.x,this.p.y),n=0;e&&n<128;e=this.parent(e),n++){
        const root=e===document.scrollingElement,s=getComputedStyle(e),overflow=horizontal?s.overflowX:s.overflowY;
        if(!root&&!/auto|scroll|overlay/.test(overflow)||root&&/hidden|clip/.test(overflow))continue;
        const range=horizontal?e.scrollWidth-e.clientWidth:e.scrollHeight-e.clientHeight;
        const before=horizontal?e.scrollLeft:e.scrollTop,sign=horizontal?d.x:d.y;
        const rtl=horizontal&&s.direction==='rtl',min=rtl?-range:0,max=rtl?0:range;
        if(range<2||sign>0&&before>=max-1||sign<0&&before<=min+1)continue;
        const amount=Math.max(40,(horizontal?e.clientWidth:e.clientHeight)*.65)*sign;
        e.scrollBy({left:horizontal?amount:0,top:horizontal?0:amount,behavior:'instant'});
        const delta=(horizontal?e.scrollLeft:e.scrollTop)-before;
        if(Math.abs(delta)<1)continue;
        this.scrollAnchor={container:e,source};
        const r=source?.isConnected?source.getBoundingClientRect():null;
        return {moved:true,x:r?r.left+r.width/2:this.p.x-(horizontal?delta:0),y:r?r.top+r.height/2:this.p.y-(horizontal?0:delta)};
      }
      return {moved:false};
    }
    """;
    internal const string RescanAfterScroll = "function(raw){return (" + Scan + ")(JSON.parse(raw),this.scrollAnchor);}";
    internal const string ScrollScope = """
    function(raw){
      const d=JSON.parse(raw),horizontal=d.x!==0;
      for(let e=this.hit(this.p.x,this.p.y),n=0;e&&n<128;e=this.parent(e),n++){
        const root=e===document.scrollingElement,s=getComputedStyle(e),overflow=horizontal?s.overflowX:s.overflowY;
        if(!root&&!/auto|scroll|overlay/.test(overflow)||root&&/hidden|clip/.test(overflow))continue;
        const range=horizontal?e.scrollWidth-e.clientWidth:e.scrollHeight-e.clientHeight;
        if(range<2)continue;
        const before=horizontal?e.scrollLeft:e.scrollTop,sign=horizontal?d.x:d.y,rtl=horizontal&&s.direction==='rtl';
        const canScroll=sign>0?before<(rtl?0:range)-1:before>(rtl?-range:0)+1;
        return {items:this.items.flatMap((item,index)=>this.inside(e,item.element)?[index]:[]),
          frames:this.frames.flatMap((frame,index)=>this.inside(e,frame.element)?[index]:[]),canScroll};
      }
      return null;
    }
    """;
}
