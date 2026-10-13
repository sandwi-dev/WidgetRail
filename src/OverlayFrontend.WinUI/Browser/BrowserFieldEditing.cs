namespace WidgetRail.OverlayFrontend.WinUI.Browser;

/// <summary>Fixed host scripts. Remote edit state never enters widget IPC or page globals.</summary>
internal static class BrowserFieldEditing
{
    internal const string Begin = """
        function() {
          const field=this;
          const active=()=>{let e=document.activeElement;while(e?.shadowRoot?.activeElement)e=e.shadowRoot.activeElement;return e;};
          const input=field instanceof HTMLInputElement && ['text','search','url','email','tel','password'].includes(field.type);
          const area=field instanceof HTMLTextAreaElement;
          const rich=field instanceof HTMLElement && field.isContentEditable;
          const password=input && field.type==='password';
          if((!input&&!area&&!rich)||active()!==field||!field.isConnected||field.disabled||field.readOnly)return null;
          let range=null, value='', original=null;
          if(rich){
            const selection=field.ownerDocument.getSelection();
            if(selection?.rangeCount){
              const candidate=selection.getRangeAt(0);
              if(field.contains(candidate.startContainer)&&field.contains(candidate.endContainer))range=candidate.cloneRange();
            }
            if(!range){range=document.createRange();range.selectNodeContents(field);range.collapse(false);}
            value=range.toString();original=field.textContent;
            if(original.length>65536)return null;
          }else if(!password){value=field.value;original=value;}
          if(value.length>2048)return null;
          const maximum=(input||area)&&field.maxLength>=0?Math.min(2048,field.maxLength):2048;
          return {
            describe:()=>({value,password,rich,multiline:rich||area,maximum}),
            prepare:(value)=>{
              if(typeof value!=='string'||value.length>maximum||active()!==field||!field.isConnected||field.disabled||field.readOnly)return false;
              if(rich){
                if(field.textContent!==original||!range.startContainer.isConnected||!range.endContainer.isConnected||
                   !field.contains(range.startContainer)||!field.contains(range.endContainer))return false;
                field.focus({preventScroll:true});
                const selection=field.ownerDocument.getSelection();selection.removeAllRanges();selection.addRange(range);
              }else{
                if(!password&&field.value!==original)return false;
                field.focus({preventScroll:true});field.select();
              }
              return active()===field;
            },
            current:()=>field.isConnected&&active()===field&&!field.disabled&&!field.readOnly
          };
        }
        """;
    internal const string Describe = "function(){return this.describe();}";
    internal const string Prepare = "function(value){return this.prepare(value);}";
    internal const string Current = "function(){return this.current();}";
}
