using System.Globalization;
using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PobCompanion;

public sealed partial class PobWidget
{
    public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (action.ActionId == "cancel-operation") { Operations.Cancel("pob.work"); return ValueTask.CompletedTask; }
            if (busy) return ValueTask.CompletedTask;
            var id=action.ActionId; var parts=id.Split('.');
            if (pendingBefore is not null && id is not "apply" and not "cancel") return ValueTask.CompletedTask;
            if(id=="apply")
            {
                var previous=pendingBefore!; var current=result!;
                Work(async token=>{token.ThrowIfCancellationRequested();Persist(current);lock(gate){undo.Push(previous.Xml);while(undo.Count>12){var keep=undo.Take(12).Reverse().ToArray();undo.Clear();foreach(var s in keep)undo.Push(s);}pendingBefore=null;}if(mapSession is {} map)await SendMapAsync(map,current.State,true,token);},"Saving change…");
            }
            else if(id=="cancel") Restore(pendingBefore!.Xml,clearPreview:true);
            else if(id=="undo" && undo.Count>0) { var xml=undo.Peek(); Work(async token=>{var restored=await engine.ExecuteAsync(new{op="load",xml,name="Working build"},token);Persist(restored);lock(gate){result=restored;undo.Pop();detailKind="";}await RefreshMapAsync(token);},"Undoing change…"); }
            else if(id is "paste" or "import" || parts[0]=="saved")
            {
                Work(async token=>
                {
                    var input=id=="paste"?readClipboard():parts[0]=="saved"?Path.Combine(dataDirectory,parts[1]+".xml"):action.CommittedText??"";
                    if(parts[0]=="saved"&&!Guid.TryParseExact(parts[1],"N",out _))throw new IOException("Invalid saved build.");
                    var xml=await BuildDocument.ReadAsync(input,game,token);
                    var loaded=await engine.ExecuteAsync(new{op="load",xml,name="Imported build"},token);
                    var renamed=BuildDocument.VerifySkillPreservation(xml,loaded.Xml);
                    var newId=parts[0]=="saved"?parts[1]:Guid.NewGuid().ToString("N");
                    if(parts[0]!="saved")BuildDocument.SaveAtomic(Path.Combine(dataDirectory,newId+".original.xml"),xml);
                    BuildDocument.SaveAtomic(Path.Combine(dataDirectory,newId+".xml"),loaded.Xml);
                    BuildDocument.SaveAtomic(Path.Combine(dataDirectory,"selected.txt"),newId);
                    lock(gate){documentId=newId;result=loaded;pendingBefore=null;undo.Clear();selections.Clear();detailKind="";section="Calcs";page=0;filter="";compatibilityNotice=renamed>0?$"PoB updated {renamed} gem names for this engine version. Your original export is retained.":"";}
                    EnsureBuildName(loaded);
                    await RefreshMapAsync(token);
                },"Loading and calculating build…");
            }
            else if(id=="new") Work(async token=>
            {
                var loaded=await engine.ExecuteAsync(new{op="new"},token);var newId=Guid.NewGuid().ToString("N");
                token.ThrowIfCancellationRequested();Persist(loaded,newId);
                lock(gate){documentId=newId;result=loaded;pendingBefore=null;undo.Clear();selections.Clear();detailKind="";section="Calcs";page=0;filter="";compatibilityNotice="";}
                await RefreshMapAsync(token);
            },"Creating build…");
            else if(id=="rename" && documentId is not null) Work(_=>
            {
                var name=(action.CommittedText??"").Trim();
                if(name.Length is <1 or >100 || name.Any(char.IsControl))throw new IOException("Use a build name between 1 and 100 characters.");
                BuildDocument.SaveAtomic(Path.Combine(dataDirectory,documentId+".name.txt"),name);
                return Task.CompletedTask;
            },"Renaming build…");
            else if(id=="export") Work(_=>{ClipboardText.Write(BuildDocument.Encode(result!.Xml));return Task.CompletedTask;},"Copying build…");
            else if(id=="export-file") Work(_=>{BuildDocument.SaveAtomic(Path.Combine(dataDirectory,documentId+".export.xml"),result!.Xml);return Task.CompletedTask;},"Exporting build…");
            else if(id=="menu") { detailKind="menu";page=0; }
            else if(id=="custom") { detailKind="custom";page=0; }
            else if(id=="skill-options") {detailKind="skill-options";page=0;}
            else if(parts[0]=="skill-option"&&parts.Length==3&&int.TryParse(parts[2],out var optionValue)) Preview(new{op="skill-option",control=parts[1],value=optionValue});
            else if(parts[0]=="stat"&&parts.Length==2&&A(State,"stats").Any(s=>S(s,"name")==parts[1])) {statDetail=parts[1];detailKind="stat";section="Calcs";page=0;}
            else if(id=="back") { detailKind=detailKind=="node"&&nodeReturnToMap&&mapSession is not null?"map":"";nodeReturnToMap=false;page=0;pastedItem=null;if(section=="Tree")treeFocusRequest++; }
            else if(id is "next-section" or "previous-section") { var index=Array.IndexOf(Sections,section);section=Sections[(index+(id=="next-section"?1:Sections.Length-1))%Sections.Length];detailKind="";page=0;filter=""; }
            else if(parts[0]=="section"&&int.TryParse(parts.ElementAtOrDefault(1),out var selected)&&selected>=0&&selected<Sections.Length) {section=Sections[selected];detailKind="";page=0;filter="";}
            else if(id=="search") {filter=action.CommittedText??"";page=0;}
            else if(parts[0]=="config-section"&&parts.Length==2&&int.TryParse(parts[1],out var category)&&category>=0&&category<ConfigurationSections().Length)
            {configSection=ConfigurationSections()[category];filter="";}
            else if(id=="config-help")configHelp=!configHelp;
            else if(id=="config-clear-search")filter="";
            else if(id=="page-next")page++;
            else if(id=="page-previous")page=Math.Max(0,page-1);
            else if(parts[0] is "node" or "skill" or "item" && parts.Length==2 && int.TryParse(parts[1],out var detail))
            {var array=parts[0]=="node"?"nodes":parts[0]=="skill"?"skills":"items";if(A(State,array).Any(x=>I(x,"id")==detail)){detailKind=parts[0];detailId=detail;selections[parts[0]]=detail;page=0;nodeReturnToMap=false;if(parts[0]=="node"){treeFocusRequest++;SendNodeSelection(detail);}}}
            else if(id=="node-change")
            {
                var node=A(State,"nodes").FirstOrDefault(n=>I(n,"id")==detailId);
                if (node.ValueKind == JsonValueKind.Object && (B(node,"canAllocate") || B(node,"canRefund")))
                    Preview(new{op="node",id=detailId,mode=B(node,"allocated")?"refund":"allocate"});
            }
            else if(id.StartsWith("skill-",StringComparison.Ordinal)) Preview(new{op="skill",id=detailId,field=id[6..]});
            else if(parts[0]=="gem"&&parts.Length==3&&int.TryParse(parts[1],out var gem)&&double.TryParse(action.CommittedText,NumberStyles.Number,CultureInfo.InvariantCulture,out var number)) Preview(new{op="gem",group=detailId,id=gem,field=parts[2],value=number});
            else if(id=="level"&&int.TryParse(action.CommittedText,out var level)) Preview(new{op="level",value=level});
            else if(parts[0] is "class" or "ascendancy" && parts.Length==2 && int.TryParse(parts[1],out var classId)) Preview(new{op=parts[0],id=classId});
            else if(parts[0]=="loadout"&&int.TryParse(parts.ElementAtOrDefault(1),out var index)&&index>=0&&index<A(State,"loadouts").Length) Switch(new{op="loadout",name=S(A(State,"loadouts")[index],"name")});
            else if(parts[0]=="set"&&parts.Length==3&&int.TryParse(parts[2],out var setId)) Switch(new{op="set",kind=parts[1],id=setId});
            else if(parts[0] is "condition" or "option" && parts.Length>=2&&int.TryParse(parts[1],out var conditionIndex))
            {
                var conditions=A(State,"conditions"); if(conditionIndex<0||conditionIndex>=conditions.Length)return ValueTask.CompletedTask;
                var c=conditions[conditionIndex];var v=c.GetProperty("value");object? changed=null;
                if(parts[0]=="option"&&parts.Length==3&&int.TryParse(parts[2],out var option)&&option>=0&&option<A(c,"options").Length)changed=A(c,"options")[option].GetProperty("value");
                else if(v.ValueKind is JsonValueKind.True or JsonValueKind.False)changed=!v.GetBoolean();
                else if(v.ValueKind==JsonValueKind.Number&&double.TryParse(action.CommittedText,NumberStyles.Float,CultureInfo.InvariantCulture,out var value))changed=value;
                else if(v.ValueKind==JsonValueKind.String&&action.CommittedText is not null)changed=action.CommittedText;
                if(changed is not null)Preview(new{op="config",key=S(c,"id"),value=changed});
            }
            else if(id=="item-paste") Work(_=>{var raw=readClipboard();if(raw.Length>32768)throw new IOException("Item text is too long.");lock(gate){pastedItem=raw;detailKind="choose-slot";page=0;}return Task.CompletedTask;},"Reading item…");
            else if(parts[0]=="replace-slot"&&parts.Length==2&&int.TryParse(parts[1],out var slotIndex)&&slotIndex>=0&&slotIndex<A(State,"slots").Length&&pastedItem is not null)
                Preview(new{op="item",raw=pastedItem,slot=S(A(State,"slots")[slotIndex],"name")});
            else if(id=="map") Work(OpenMapAsync,"Opening tree map…");
            if (section == "Tree" && mapSession is null && (parts[0] == "section" || id is "next-section" or "previous-section"))
                Work(OpenMapAsync, "Opening passive tree…");
            Invalidate();
        }
        return ValueTask.CompletedTask;
    }
    private void Persist(PobResult current,string? targetId=null)
    {
        targetId??=documentId;
        if(targetId is null)throw new InvalidOperationException("No working build is selected.");
        EnsureBuildName(current,targetId);
        BuildDocument.SaveAtomic(Path.Combine(dataDirectory,targetId+".xml"),current.Xml);
        BuildDocument.SaveAtomic(Path.Combine(dataDirectory,"selected.txt"),targetId);
    }
    private void Switch(object command)=>Work(async token=>{var next=await engine.ExecuteAsync(command,token,result!.Xml);Persist(next);lock(gate){result=next;page=0;detailKind="";}await RefreshMapAsync(token);},"Switching build set…");
    private void Preview(object command)=>Work(async token=>{var before=result!;var next=await engine.ExecuteAsync(command,token,result!.Xml);lock(gate){pendingBefore=before;result=next;}await RefreshMapAsync(token);},"Calculating comparison…");
    private void Restore(string xml,bool clearPreview)=>Work(async token=>{var next=await engine.ExecuteAsync(new{op="load",xml,name="Working build"},token);lock(gate){result=next;if(clearPreview)pendingBefore=null;}await RefreshMapAsync(token);},"Restoring build…");

    private WidgetWebSession? mapSession;
    private string? mapArtworkVersion;
    private void SendNodeSelection(int nodeId)
    {
        if(mapSession is not {} session)return;
        Operations.RunSingleFlight("pob.map-selection",async operation=>
        {
            try
            {
                await mapSerial.WaitAsync(operation.CancellationToken);
                try
                {
                    lock(gate){if(!ReferenceEquals(mapSession,session)||detailKind!="node")return;nodeId=detailId;}
                    await session.PostMessageAsync(new{type="select",id=nodeId},operation.CancellationToken);
                }
                finally{mapSerial.Release();}
            }
            catch(Exception error) when(error is WidgetCapabilityException or IOException or OperationCanceledException or ObjectDisposedException)
            { /* Native details remain usable when the optional map has closed. */ }
        },WidgetOperationLifetime.Widget);
    }
    private async Task OpenMapAsync(CancellationToken token)
    {
        if(mapSession is not null){await mapSession.DisposeAsync();mapSession=null;}
        var resources=new List<EmbeddedMediaResource>{
            new(){Path="payload/web/tree.html",ContentType="text/html"},new(){Path="payload/web/tree.js",ContentType="text/javascript"},
            new(){Path="payload/web/tree.css",ContentType="text/css"},new(){Path="payload/web/web-session.js",ContentType="text/javascript"}};
        var version=S(State,"treeVersion");
        if(!version.All(c=>char.IsAsciiLetterOrDigit(c)||c=='_'))throw new IOException("Invalid tree artwork version.");
        var art=artworkDirectory is null?null:Path.Combine(artworkDirectory,version);
        if(art is not null&&Directory.Exists(art))
        {
            resources.Add(new(){Path="payload/web/art/"+version+"/index.json",ContentType="application/json"});
            foreach(var file in Directory.EnumerateFiles(art,"nodes-*.jpg").Order())
                resources.Add(new(){Path="payload/web/art/"+version+"/"+Path.GetFileName(file),ContentType="image/jpeg"});
        }
        var source=WebSessionSource.Package("payload/web/tree.html",resources);
        var session=await HostServices.WebSessions.OpenAsync(new("passive-tree","Passive skill tree",source){Browser=new(){Chrome=BrowserChrome.OnDemand,Background=BrowserBackgroundMode.Automatic}},token);
        mapSession=session;
        mapArtworkVersion=version;
        _=ReceiveMapAsync(session);
        lock(gate)detailKind="map";
        Invalidate();
        // Page readiness is observed through its message loop before sending data.
    }
    private async Task ReceiveMapAsync(WidgetWebSession session)
    {
        try
        {
            await foreach(var message in session.ReadMessagesAsync(WidgetLifetimeToken))
            {
                if(!ReferenceEquals(mapSession,session))return;
                if(S(message,"type")=="ready")
                {
                    JsonElement snapshot;lock(gate)snapshot=State;
                    await SendMapAsync(session,snapshot,false,WidgetLifetimeToken);
                }
                else if(S(message,"type")=="inspect"&&message.TryGetProperty("id",out var inspectedId)&&inspectedId.TryGetInt32(out var inspected)&&
                    message.TryGetProperty("revision",out var requestRevision)&&requestRevision.TryGetInt64(out var inspectedRevision))
                {
                    JsonElement node;lock(gate)node=A(State,"nodes").FirstOrDefault(n=>I(n,"id")==inspected);
                    if(node.ValueKind==JsonValueKind.Object)
                        await session.PostMessageAsync(new{type="tooltip",id=inspected,revision=inspectedRevision,
                            name=Limit(S(node,"name"),160),description=Limit(S(node,"description"),2500),kind=S(node,"kind"),allocated=B(node,"allocated"),
                            points=B(node,"allocated")?I(node,"refundCount"):I(node,"pathCount"),canAllocate=B(node,"canAllocate"),canRefund=B(node,"canRefund"),requiresChoice=B(node,"requiresChoice")},WidgetLifetimeToken);
                }
                else if(S(message,"type")=="select")
                {
                    var id=I(message,"id");lock(gate){if(!busy&&pendingBefore is null&&A(State,"nodes").Any(n=>I(n,"id")==id)){detailId=id;selections["node"]=id;treeFocusRequest++;detailKind="node";nodeReturnToMap=true;page=0;}}
                    Invalidate();
                }
            }
        }
        catch(Exception e) when(e is OperationCanceledException or WidgetCapabilityException or ObjectDisposedException or IOException)
        {if(!WidgetLifetimeToken.IsCancellationRequested){lock(gate){if(ReferenceEquals(mapSession,session)){mapSession=null;status="Tree map closed. You can reopen it or use the searchable node list.";if(detailKind=="map")detailKind="";}}Invalidate();}}
    }
    private readonly SemaphoreSlim mapSerial = new(1);
    private WidgetWebSession? sentMapSession;
    private string? sentMapGeometry;
    private JsonElement? sentMapState;
    private long mapTransferRevision;
    private async Task RefreshMapAsync(CancellationToken token)
    {
        WidgetWebSession? session; JsonElement snapshot;
        lock(gate){session=mapSession;snapshot=State;}
        if(session is not null&&mapArtworkVersion!=S(snapshot,"treeVersion")){await OpenMapAsync(token);return;}
        if(session is not null) await SendMapAsync(session,snapshot,true,token);
    }
    private async Task SendMapAsync(WidgetWebSession session,JsonElement snapshot,bool preserve,CancellationToken token)
    {
        await mapSerial.WaitAsync(token);
        try
        {
            JsonElement? before;int selected;
            lock(gate)
            {
                if (!ReferenceEquals(mapSession,session)) return;
                // A ready event may have queued behind a newer preview. Capture
                // state and comparison together after acquiring the send lease.
                snapshot=State;before=pendingBefore?.State;selected=detailKind=="node"?detailId:0;
            }
            var geometry = TreeMapMessages.GeometryKey(snapshot);
            var reuse = preserve && ReferenceEquals(sentMapSession,session) && sentMapGeometry == geometry;
            var messages = TreeMapMessages.Create(snapshot,before,preserve,selected,reuse,++mapTransferRevision,sentMapState);
            await session.PostMessageAsync(messages[0],token);
            // Chunks are independent and the page swaps only after the final
            // acknowledgement. Bound in-flight work instead of serial round trips.
            for (var offset=1;offset<messages.Count-1;offset+=4)
                await Task.WhenAll(messages.Skip(offset).Take(Math.Min(4,messages.Count-1-offset))
                    .Select(message=>session.PostMessageAsync(message,token).AsTask()));
            await session.PostMessageAsync(messages[^1],token);
            sentMapSession=session;sentMapGeometry=geometry;sentMapState=snapshot;
        }
        catch(Exception error) when(error is WidgetCapabilityException or IOException or ObjectDisposedException)
        {
            lock(gate) { if(ReferenceEquals(mapSession,session)) { mapSession=null;if(detailKind=="map")detailKind=""; } }
            try { await session.DisposeAsync(); } catch(Exception cleanup) when(cleanup is WidgetCapabilityException or IOException or ObjectDisposedException) { }
            // Native inspection and saved changes remain valid when the optional map closes.
        }
        finally{mapSerial.Release();}
    }
}
