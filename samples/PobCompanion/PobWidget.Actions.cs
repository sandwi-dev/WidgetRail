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
            skillWorkspaceFocusRequested=false;
            skillWorkspaceReturnControl=null;
            if(HandleUnifiedSearchAction(action)){Invalidate();return ValueTask.CompletedTask;}
            if(HandleDisclosureAction(action)){Invalidate();return ValueTask.CompletedTask;}
            if(HandleWebsiteAction(action))return ValueTask.CompletedTask;
            if(HandleRankingAction(action)){Invalidate();return ValueTask.CompletedTask;}
            if(HandleTradeReviewAction(action)){Invalidate();return ValueTask.CompletedTask;}
            var id=action.ActionId; var parts=id.Split('.');
            modifierFocusRequested=false;
            if(featureReading is not null||featureRemoval is not null||timelessDetail is not null){HandlePlanningAction(action);Invalidate();return ValueTask.CompletedTask;}
            if(id is not "group-up" and not "group-down")skillOrderFocusRequested=false;
            if(pendingImport is {} importing)
            {
                if(id=="import-cancel"){pendingImport=null;Invalidate();}
                else if(id=="import-confirm"){pendingImport=null;ReplaceImportedBuild(importing);}
                return ValueTask.CompletedTask;
            }
            if(deleteBuildId is {} deleting)
            {
                if(id=="delete-cancel"){deleteBuildId=null;Invalidate();}
                else if(id=="delete-confirm"){deleteBuildId=null;DeleteBuild(deleting);}
                return ValueTask.CompletedTask;
            }
            if (pendingBefore is not null && detailKind=="node" && id=="preview-nodes")
            { showChangedNodes=!showChangedNodes;Invalidate();return ValueTask.CompletedTask; }
            quickControlTarget=null;
            libraryOpenFocusRequested=false;
            if(id.StartsWith("quick-controls.",StringComparison.Ordinal))
            {
                var target=id["quick-controls.".Length..];
                if(target is "pob.buildtools" or "pob.body" && !BuildControlsAvailable)return ValueTask.CompletedTask;
                var permitted=pendingBefore is null || target is "pob.tree.preview.actions" or "pob.tree.preview-details";
                if(permitted&&quickControlTargets.Contains(target)){quickControlTarget=target;historyFocusRequested=false;focusRequestSequence++;Invalidate();}
                return ValueTask.CompletedTask;
            }
            if (pendingBefore is not null && id is not "apply" and not "cancel") return ValueTask.CompletedTask;
            if(HandleSimilarSearchAction(action)){Invalidate();return ValueTask.CompletedTask;}
            if(HandleBuildComparisonAction(action)){Invalidate();return ValueTask.CompletedTask;}
            if(HandleGameExportAction(action)){Invalidate();return ValueTask.CompletedTask;}
            if(HandleWebsitePreference(action)){Invalidate();return ValueTask.CompletedTask;}
            if(HandleSetAction(action)){Invalidate();return ValueTask.CompletedTask;}
            if(HandleTradeAction(action)){Invalidate();return ValueTask.CompletedTask;}
            if(HandleItemAuthoringAction(action)){Invalidate();return ValueTask.CompletedTask;}
            if(HandleCalculationAction(action)){Invalidate();return ValueTask.CompletedTask;}
            if(HandleGemOptionsAction(action)){Invalidate();return ValueTask.CompletedTask;}
            if(HandlePlanningAction(action)){Invalidate();return ValueTask.CompletedTask;}
            if(id is not "undo" and not "redo")historyFocusRequested=false;
            if(HandleSkillManagementAction(action)){Invalidate();return ValueTask.CompletedTask;}
            if(id=="apply"&&pendingBefore is not null)
            {
                var previous=pendingBefore!; var current=result!;
                Work(async token=>
                {
                    token.ThrowIfCancellationRequested();
                    SaveWork(current,new(current.Xml,undo.Prepend(previous.Xml).Take(12).ToArray(),[],ItemDraft:detailKind=="choose-slot"?null:pastedItem,
                        TradeListing:detailKind=="choose-slot"||pastedItem is null?null:tradeListing));
                    lock(gate)
                    {
                        PushHistory(undo,previous.Xml);redo.Clear();

                        pendingBefore=null;focusRequestSequence++;
                        if(detailKind=="choose-slot")
                        {
                            var item=A(State,"items").FirstOrDefault(i=>S(i,"slot")==previewSlot);
                            detailKind="item";detailId=I(item,"id");selections["item"]=detailId;pastedItem=null;
                        }
                    }
                    await RefreshCommittedMapAsync(token);
                },"Saving change…");
            }
            else if(id=="cancel"&&pendingBefore is not null) Restore(pendingBefore.Xml,clearPreview:true);
            else if(id=="undo" && undo.Count>0) MoveHistory(true);
            else if(id=="redo" && redo.Count>0) MoveHistory(false);
            else if(parts is ["recent-build",var recent]&&recent!=documentId&&!buildComparisonRunning&&recentBuildEntries.Any(entry=>entry.Id==recent))
                Work(token=>OpenSavedWorkAsync(recent,token),"Opening recent build…");
            else if(parts[0]=="saved"&&parts.Length==2) Work(token=>OpenSavedWorkAsync(parts[1],token),"Opening saved work…");
            else if(id is "paste" or "import") ImportBuild(id=="paste"?null:action.CommittedText??"");
            else if(parts[0]=="import-mode"&&parts.Length==2&&parts[1] is "new" or "replace")replaceOnImport=parts[1]=="replace";
            else if(id=="new") Work(async token=>
            {
                var loaded=await engine.ExecuteAsync(new{op="new"},token);var newId=Guid.NewGuid().ToString("N");
                token.ThrowIfCancellationRequested();Persist(loaded,newId);
                lock(gate){tradeCandidates.Clear();tradeListing=null;tradeBrowsing=false;websiteReview=false;documentId=newId;result=loaded;pendingBefore=null;pastedItem=null;previewSlot=null;recoveryReadOnly=false;undo.Clear();redo.Clear();selections.Clear();detailKind="";SetSection("Calcs");filter="";passiveReturnKey=null;compatibilityNotice="";RestoreRanking(newId);RestoreTradeReturn(newId);}
                RememberRecentBuild(newId);
                await RefreshMapAsync(token);
            },"Creating build…");
            else if(id=="rename" && documentId is not null) RenameBuild(documentId,action.CommittedText??"");
            else if(parts[0]=="library-select"&&parts.Length==2&&savedBuilds.Any(b=>b.Id==parts[1])){librarySelection=parts[1];libraryOpenFocusRequested=true;focusRequestSequence++;}
            else if(id=="library-search")librarySearch=action.CommittedText??"";
            else if(parts[0]=="library-sort"&&parts.Length==2&&parts[1] is "recent" or "name")librarySort=parts[1];
            else if(id=="library-rename"&&librarySelection is {} rename)RenameBuild(rename,action.CommittedText??"");
            else if(id=="library-duplicate"&&librarySelection is {} copy)DuplicateBuild(copy);
            else if(id=="library-delete"&&librarySelection is {} remove)deleteBuildId=remove;
            else if(id=="export") Work(_=>{ClipboardText.Write(BuildDocument.Encode(result!.Xml));return Task.CompletedTask;},"Copying build…");
            else if(id=="export-file") Work(_=>{BuildDocument.SaveAtomic(Path.Combine(dataDirectory,documentId+".export.xml"),result!.Xml);return Task.CompletedTask;},"Exporting build…");
            else if(id=="loadout-create")Edit(new{op="loadout-create",name=action.CommittedText??""},closeDetail:true);
            else if(id=="menu") { detailKind="menu"; }
            else if(id=="library") { detailKind="library";librarySelection=documentId; }
            else if(id=="custom") { detailKind="custom"; }
            else if(id=="skill-options" && detailKind=="skill") OpenSkillOptions();
            else if(parts[0]=="main-group"&&parts.Length==2&&int.TryParse(parts[1],out var mainGroup)&&A(State,"skills").Any(s=>I(s,"id")==mainGroup))
                Edit(new{op="skill",id=mainGroup,field="main"});
            else if(parts is ["skill-number",var numberGroup,var numberControl]&&detailKind=="skill-options"&&
                int.TryParse(numberGroup,out var numericGroup)&&numericGroup==detailId&&CurrentSkillOptions is {} options&&
                options.Options.Any(o=>o.Id==numberControl&&o.Numeric))
                Edit(new{op="skill-number",group=numericGroup,control=numberControl,value=action.CommittedText??""});
            else if(parts[0]=="stat"&&parts.Length==2&&A(State,"stats").Any(s=>S(s,"name")==parts[1])) {statDetail=parts[1];detailKind="stat";SetSection("Calcs");}
            else if(id=="back"&&detailKind=="choose-slot"&&pastedItem is not null) DiscardItemDraft();
            else if(id=="back") { if(detailKind=="node"&&!(nodeReturnToMap&&mapSession is not null))RevealPassiveOnReturn();detailKind=detailKind=="library"&&result is not null?"menu":detailKind=="node"&&nodeReturnToMap&&mapSession is not null?"map":"";nodeReturnToMap=false;if(section=="Tree")focusRequestSequence++; }
            else if(id is "next-section" or "previous-section") { var index=Array.IndexOf(NavigationSections,section);SetSection(NavigationSections[(index+(id=="next-section"?1:NavigationSections.Length-1))%NavigationSections.Length]);detailKind="";filter="";passiveReturnKey=null;focusRequestSequence++; }
            else if(parts[0]=="section"&&int.TryParse(parts.ElementAtOrDefault(1),out var selected)&&selected>=0&&selected<Sections.Length) {SetSection(Sections[selected]);detailKind="";filter="";passiveReturnKey=null;focusRequestSequence++;}
            else if(id=="search") {filter=action.CommittedText??"";passiveReturnKey=null;}
            else if(parts[0]=="config-section"&&parts.Length==2&&int.TryParse(parts[1],out var category)&&category>=0&&category<ConfigurationSections().Length)
            {configSection=ConfigurationSections()[category];filter="";passiveReturnKey=null;}
            else if(id=="config-help")configHelp=!configHelp;
            else if(id=="config-changed"&&ConfigurationCatalog.SupportsChangedFilter)configChangedOnly=!configChangedOnly;
            else if(id=="config-clear-search"){filter="";passiveReturnKey=null;}
            else if(parts[0] is "node" or "skill" or "item" && parts.Length==2 && int.TryParse(parts[1],out var detail))
            {var array=parts[0]=="node"?"nodes":parts[0]=="skill"?"skills":"items";if(A(State,array).Any(x=>I(x,"id")==detail&&(parts[0]!="node"||B(x,"interactive")))){detailKind=parts[0];detailId=detail;selections[parts[0]]=detail;nodeReturnToMap=false;if(parts[0]=="node"){focusRequestSequence++;SendNodeSelection(detail);ReadAttributeChoices(detail);}}}
            else if(id=="node-change")
            {
                var node=A(State,"nodes").FirstOrDefault(n=>I(n,"id")==detailId);
                if (node.ValueKind == JsonValueKind.Object && (B(node,"canAllocate") || B(node,"canRefund")))
                    Preview(new{op="node",id=detailId,allocationMode=Capabilities.WeaponTreeModes?(int?)I(State,"allocationMode"):null,mode=B(node,"allocated")?"refund":"allocate"});
            }
            else if(id is "skill-main" or "skill-enabled" or "skill-fullDps") Edit(new{op="skill",id=detailId,field=id[6..]});
            else if(parts[0]=="gem"&&parts.Length==3&&int.TryParse(parts[1],out var gem)&&double.TryParse(action.CommittedText,NumberStyles.Number,CultureInfo.InvariantCulture,out var number))
            {
                var skill=A(State,"skills").FirstOrDefault(s=>I(s,"id")==detailId);
                var entry=A(skill,"gems").FirstOrDefault(g=>I(g,"id")==gem);
                if(parts[2]=="level"&&B(entry,"levelReadOnly"))ShowFeedback("Level is source-controlled","Change the granting item or passive to change this skill's level.",ToastTone.Info);
                else Edit(new{op="gem",group=detailId,id=gem,field=parts[2],value=number});
            }
            else if(id=="level"&&int.TryParse(action.CommittedText,out var level)) Edit(new{op="level",value=level});
            else if(parts[0] is "class" or "ascendancy" && parts.Length==2 && int.TryParse(parts[1],out var classId)) Preview(new{op=parts[0],id=classId});
            else if(parts[0]=="loadout"&&int.TryParse(parts.ElementAtOrDefault(1),out var index)&&index>=0&&index<A(State,"loadouts").Length) Edit(new{op="loadout",name=S(A(State,"loadouts")[index],"name")},closeDetail:true);
            else if(parts[0]=="set"&&parts.Length==3&&int.TryParse(parts[2],out var setId)) Edit(new{op="set",kind=parts[1],id=setId},closeDetail:true);
            else if(parts[0] is "condition" or "option" && parts.Length>=2&&int.TryParse(parts[1],out var conditionIndex))
            {
                var conditions=A(State,"conditions"); if(conditionIndex<0||conditionIndex>=conditions.Length)return ValueTask.CompletedTask;
                var c=conditions[conditionIndex];var v=c.GetProperty("value");object? changed=null;
                if(parts[0]=="option"&&parts.Length==3&&int.TryParse(parts[2],out var option)&&option>=0&&option<A(c,"options").Length)changed=A(c,"options")[option].GetProperty("value");
                else if(v.ValueKind is JsonValueKind.True or JsonValueKind.False)changed=!v.GetBoolean();
                else if(v.ValueKind==JsonValueKind.Number&&double.TryParse(action.CommittedText,NumberStyles.Float,CultureInfo.InvariantCulture,out var value))changed=value;
                else if(v.ValueKind==JsonValueKind.String&&action.CommittedText is not null)changed=action.CommittedText;
                if(changed is not null)Edit(new{op="config",key=S(c,"id"),value=changed});
            }
            else if(id=="item-paste") SetItemDraft(null);
            else if(id=="item-resume"&&pastedItem is not null) {SetSection("Items");detailKind="choose-slot";}
            else if(parts[0]=="replace-slot"&&parts.Length==2&&int.TryParse(parts[1],out var slotIndex)&&slotIndex>=0&&slotIndex<A(State,"slots").Length&&pastedItem is not null)
            {previewSlot=S(A(State,"slots")[slotIndex],"name");Preview(new{op="item",raw=pastedItem,slot=previewSlot});}
            else if(id=="map") Work(OpenMapAsync,"Opening tree map…");
            if (section == "Tree" && mapSession is null && (parts[0] == "section" || id is "next-section" or "previous-section"))
                Work(OpenMapAsync, "Opening passive tree…");
            Invalidate();
        }
        return ValueTask.CompletedTask;
    }
    private void Persist(PobResult current,string? targetId=null)
    {
        SaveWork(current,new(current.Xml,[],[]),targetId);
    }
    private void Preview(object command)=>Work(async token=>
    {
        var before=result!;var next=await engine.ExecuteAsync(command,token,before.Xml,compare:true);
        var pending=new PobPendingChange(next.Xml,detailKind=="node"?"node":detailKind=="choose-slot"?"item":"character",detailId,previewSlot);
        token.ThrowIfCancellationRequested();SaveWork(before,CurrentWork(before.Xml,pending,pastedItem),refreshXml:false);
        lock(gate){pendingBefore=before;result=next;showChangedNodes=false;focusRequestSequence++;}
        await RefreshCommittedMapAsync(token);
    },"Calculating comparison…");
    private void Restore(string xml,bool clearPreview)=>Work(async token=>
    {
        var next=await engine.ExecuteAsync(new{op="load",xml,name="Working build"},token);
        token.ThrowIfCancellationRequested();SaveWork(next,CurrentWork(next.Xml,draft:pastedItem),refreshXml:false);
        lock(gate){result=next;if(clearPreview){pendingBefore=null;focusRequestSequence++;}}
        if(detailKind=="skill-options")await RefreshSkillOptionsAsync(detailId,token);
        await RefreshCommittedMapAsync(token);
    },"Restoring build…");

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
            foreach(var file in Directory.EnumerateFiles(art,"*.webp").Order())
                resources.Add(new(){Path="payload/web/art/"+version+"/"+Path.GetFileName(file),ContentType="image/webp"});
        }
        var source=WebSessionSource.Package("payload/web/tree.html",resources);
        var session=await HostServices.WebSessions.OpenAsync(new("passive-tree","Passive skill tree",source){Browser=new(){Chrome=BrowserChrome.OnDemand,Background=BrowserBackgroundMode.Automatic}},token);
        mapSession=session;
        mapArtworkVersion=version;
        _=ReceiveMapAsync(session);
        lock(gate)if(detailKind!="node")detailKind="map";
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
                    if(node.ValueKind==JsonValueKind.Object&&B(node,"interactive"))
                        await session.PostMessageAsync(CreateTooltip(node,inspectedRevision),WidgetLifetimeToken);
                }
                else if(S(message,"type")=="select")
                {
                    var id=I(message,"id");lock(gate){if(!busy&&pendingBefore is null&&passiveChoicePopup is null&&A(State,"nodes").Any(n=>I(n,"id")==id&&B(n,"interactive"))){detailId=id;selections["node"]=id;focusRequestSequence++;detailKind="node";nodeReturnToMap=true;passiveReturnKey=null;ReadAttributeChoices(id);}}
                    Invalidate();
                }
            }
        }
        catch(Exception e) when(e is OperationCanceledException or WidgetCapabilityException or ObjectDisposedException or IOException)
        {if(!WidgetLifetimeToken.IsCancellationRequested){lock(gate){if(ReferenceEquals(mapSession,session)){mapSession=null;ShowFeedback("Tree map closed","Reopen it or use the searchable node list.",ToastTone.Warning);if(detailKind=="map")detailKind="";}}Invalidate();}}
    }
    private readonly SemaphoreSlim mapSerial = new(1);
    internal static JsonElement CreateTooltip(JsonElement node,long revision)
    {
        var rich=DescriptionParagraphs(node).Select(p=>p.Select(r=>new{text=r.Text,tone=r.Tone}).ToArray()).ToArray();
        JsonElement Payload(bool formatted)=>JsonSerializer.SerializeToElement(new{type="tooltip",id=I(node,"id"),revision,
            name=Limit(S(node,"name"),160),description=formatted?"":Limit(S(node,"description"),1500),
            paragraphs=formatted?rich:null,kind=S(node,"kind"),allocated=B(node,"allocated"),granted=B(node,"granted"),socketedItem=S(node,"socketedItem"),
            points=B(node,"allocated")?I(node,"refundCount"):I(node,"pathCount"),canAllocate=B(node,"canAllocate"),canRefund=B(node,"canRefund"),requiresChoice=B(node,"requiresChoice"),
            truncated=!formatted&&S(node,"description").Length>1500});
        var payload=Payload(true);
        return JsonSerializer.SerializeToUtf8Bytes(payload).Length<=16000?payload:Payload(false);
    }
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
            int? searchNode;lock(gate)searchNode=unifiedMapNode;
            if(searchNode is {} target&&target==selected)
            {await session.PostMessageAsync(new{type="select",id=target},token);lock(gate)if(unifiedMapNode==target)unifiedMapNode=null;}
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
