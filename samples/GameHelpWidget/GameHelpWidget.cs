using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.GameHelp;

public sealed partial class GameHelpWidget : Widget
{
    private sealed record Message(long Id, string Role, string Text, GameHelpAnswer? Answer = null, string? ContextLabel = null, bool SolutionExpanded = false, bool SourcesExpanded = false);
    private sealed record State
    {
        internal bool HasKey, KeyReady, Settings, Busy, Capturing, OfferContext, PreviewReadOnly, CaptureOptions, ConfirmNew, ConfirmDelete;
        internal string? FocusGroup;
        internal long FocusRequest;
        internal ScrollRevealRequest? Reveal;
        internal string Draft = "", Spoilers = "Hints first";
        internal bool Grounding;
        internal long AnswerEpoch, CaptureEpoch;
        internal string? Error, PendingQuestion;
        internal IReadOnlyList<Message> Messages = [];
        internal WindowCaptureTicket? Ticket;
        internal CaptureAttachment? Preview, Context;
    }
    private readonly IGameHelpService service;
    private readonly GameHelpPreferencesStore? preferences;
    private readonly WidgetModel<State> model;
    private long nextMessage, keyRevision, nextOperation, nextFocusRequest, nextRevealRequest;
    public GameHelpWidget()
    {
        service = new GeminiGameHelpService(() => HostServices);
        preferences = new();
        var saved = preferences.Load();
        model = CreateModel(new State { Grounding = saved.Grounding, Spoilers = saved.Spoilers });
    }
    public GameHelpWidget(IGameHelpService service) : this(service, null) { }
    internal GameHelpWidget(IGameHelpService service, GameHelpPreferencesStore? preferences)
    {
        this.service = service;
        this.preferences = preferences;
        var saved = preferences?.Load() ?? new();
        model = CreateModel(new State { Grounding = saved.Grounding, Spoilers = saved.Spoilers });
    }
    protected override ValueTask OnActivatedAsync(CancellationToken activeLifetime)
    {
        var revision = Interlocked.Read(ref keyRevision);
        Operations.RunSingleFlight("initialize", async context =>
        {
            try
            {
                var configured = await service.HasKeyAsync(context.CancellationToken);
                if (context.IsCurrent && revision == Interlocked.Read(ref keyRevision)) model.Update(state => state with { HasKey = configured, KeyReady = true, Error = null });
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested) { }
            catch (Exception error) { if (context.IsCurrent) model.Update(state => state with { Error = Friendly(error), KeyReady = true }); }
        });
        return ValueTask.CompletedTask;
    }
    public override ValueTask<bool> OnIntentCompletedAsync(WidgetIntentFeedback feedback, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(feedback.Request.ContractId == WidgetIntentContracts.SearchVideo &&
            feedback.Status is WidgetIntentStatus.Unavailable or WidgetIntentStatus.Rejected);
    }

    public override ValueTask OnActionAsync(WidgetActionEvent action, CancellationToken cancellationToken = default)
    {
        if (!IsActive) return ValueTask.CompletedTask;
        var state=model.Value;
        switch(action.ActionId)
        {
            case "draft": model.Update(s=>s with {Draft=action.CommittedText ?? ""}); break;
            case "settings": model.Update(s=>s with {Settings=true, CaptureOptions=false}); break;
            case "context.open": model.Update(s=>s with {CaptureOptions=true, Settings=false, Error=null}); break;
            case "context.close": model.Update(s=>s with {CaptureOptions=false}); break;
            case "compose.focus": RequestFocus("help.compose-focus"); break;
            case "settings.close": model.Update(s=>s with {Settings=false}); break;
            case "preview.close": model.Update(s=>s with {PreviewReadOnly=true}); break;
            case "preview.open": model.Update(s=>s with {PreviewReadOnly=false}); break;
            case "preview.question.focus": RequestFocus("help.preview-question-group"); break;
            case "preview.question" when !state.Busy && !state.Capturing && state.Preview is not null && action.CommittedText is { Length: <= 2048 } question:
                var pending = state.Messages.LastOrDefault(message => message.Role == "user")?.Id;
                model.Update(s => s with { PendingQuestion = question.Trim(), Messages = s.Messages.Select(message => message.Id == pending
                    ? message with { Text = question.Trim() } : message).ToArray() });
                break;
            case "key.save" when !state.Busy && !state.Capturing && action.CommittedText is { } key: ConfigureKey(key); break;
            case "key.delete" when !state.Busy && !state.Capturing: model.Update(s => s with { ConfirmDelete = true }); break;
            case "key.delete.cancel": model.Update(s => s with { ConfirmDelete = false }); break;
            case "key.delete.confirm": ConfigureKey(null); break;
            case "ask" when !state.Busy && !state.Capturing && !string.IsNullOrWhiteSpace(state.Draft): AddQuestion(state.Draft,false); break;
            case "ask.plain" when !state.Busy && !state.Capturing: Answer(null); break;
            case "answer.retry" when !state.Busy && !state.Capturing: model.Update(s => s with { Settings = false }); Answer(state.Context); break;
            case "answer.cancel": Operations.Cancel("answer"); model.Update(s=>s with {Busy=false,AnswerEpoch=Interlocked.Increment(ref nextOperation)}); break;
            case "capture.image" when !state.Busy && !state.Capturing: Capture(WindowCaptureKind.Screenshot); break;
            case "capture.video" when !state.Busy && !state.Capturing: Capture(WindowCaptureKind.Video); break;
            case "capture.cancel": Operations.Cancel("capture"); model.Update(s=>s with {Capturing=false,Ticket=null,CaptureEpoch=Interlocked.Increment(ref nextOperation)}); break;
            case "preview.send" when !state.Busy && !string.IsNullOrWhiteSpace(state.PendingQuestion): Answer(state.Preview); break;
            case "preview.discard" when !state.Busy: DiscardPreview(); break;
            case "preview.retry" when !state.Busy: Capture(state.Preview?.ContentType=="video/mp4" ? WindowCaptureKind.Video : WindowCaptureKind.Screenshot); break;
            case "grounding.on": SetPreferences(grounding: true); break;
            case "grounding.off": SetPreferences(grounding: false); break;
            case "new": model.Update(s => s with { ConfirmNew = true }); break;
            case "new.cancel": model.Update(s => s with { ConfirmNew = false }); break;
            case "new.confirm": NewConversation(); break;
            default:
                if(action.ActionId.StartsWith("solution.",StringComparison.Ordinal) && long.TryParse(action.ActionId[9..], out var solutionId))
                    model.Update(s => s with { Messages = s.Messages.Select(message => message.Id == solutionId && !string.IsNullOrWhiteSpace(message.Answer?.DetailedSolution)
                        ? message with { SolutionExpanded = !message.SolutionExpanded } : message).ToArray() });
                else if(action.ActionId.StartsWith("sources.",StringComparison.Ordinal) && long.TryParse(action.ActionId[8..], out var sourcesId))
                    model.Update(s => s with { Messages = s.Messages.Select(message => message.Id == sourcesId && message.Answer?.Sources.Count > 0
                        ? message with { SourcesExpanded = !message.SourcesExpanded } : message).ToArray() });
                else if(action.ActionId.StartsWith("spoiler.",StringComparison.Ordinal) && int.TryParse(action.ActionId[8..],out var spoiler) && spoiler>=0 && spoiler<Spoilers.Length)
                    SetPreferences(spoilers: Spoilers[spoiler]);
                else if(!state.Busy && !state.Capturing && action.ActionId.StartsWith("starter.",StringComparison.Ordinal) && int.TryParse(action.ActionId[8..],out var starter) && starter>=0 && starter<Starters.Length)
                    AddQuestion(Starters[starter],true);
                else if(!state.Busy && !state.Capturing && action.ActionId.StartsWith("suggest.",StringComparison.Ordinal))
                {
                    var parts=action.ActionId.Split('.');
                    if(parts.Length==3 && long.TryParse(parts[1],out var id) && int.TryParse(parts[2],out var index) && state.Messages.FirstOrDefault(m=>m.Id==id)?.Answer is { } answer && index>=0 && index<answer.ReplyOptions.Count)
                        AddQuestion(answer.ReplyOptions[index].Message,false);
                }
                break;
        }
        return ValueTask.CompletedTask;
    }
    private void RequestFocus(string group) => model.Update(s => s with { FocusGroup = group, FocusRequest = Interlocked.Increment(ref nextFocusRequest) });
    private void SetPreferences(bool? grounding = null, string? spoilers = null)
    {
        var state = model.Value;
        var next = new GameHelpPreferences(grounding ?? state.Grounding, spoilers ?? state.Spoilers);
        try
        {
            preferences?.Save(next);
            model.Update(s => s with { Grounding = next.Grounding, Spoilers = next.Spoilers, Error = null });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { model.Update(s => s with { Error = "Game Help could not save this setting. Check access to your local app data and try again." }); }
    }
    private void AddQuestion(string question,bool offerContext)
    {
        var id=Interlocked.Increment(ref nextMessage);
        model.Update(s=>s with {Messages=s.Messages.Append(new Message(id,"user",question)).TakeLast(20).ToArray(),Draft="",PendingQuestion=question,OfferContext=offerContext,Error=null,
            Reveal = new(Interlocked.Increment(ref nextRevealRequest), "help.transcript", "help.message." + id),
            FocusGroup = offerContext ? "help.context-choice" : "help.compose-focus", FocusRequest = Interlocked.Increment(ref nextFocusRequest)});
        if(!offerContext) Answer(model.Value.Context);
    }
    private void ConfigureKey(string? key)
    {
        Interlocked.Increment(ref keyRevision);
        Operations.RunLatest("key",async context=>
        {
            try
            {
                if(key is null) await service.DeleteKeyAsync(context.CancellationToken); else await service.SaveKeyAsync(key.Trim(),context.CancellationToken);
                if(context.IsCurrent)
                {
                    model.Update(s=>s with {HasKey=key is not null,KeyReady=true,Settings=false,ConfirmDelete=false,Error=null});
                    if (key is not null) _ = OnActivatedAsync(ActiveCancellationToken);
                }
            }
            catch(Exception error) when(error is not OperationCanceledException) {if(context.IsCurrent)model.Update(s=>s with {Error=Friendly(error)});}
        });
    }
    private void Capture(WindowCaptureKind kind)
    {
        var state=model.Value;
        if (!string.IsNullOrWhiteSpace(state.Draft)) AddQuestion(state.Draft, true);
        else if(state.PendingQuestion is null || state.Messages.LastOrDefault()?.Role == "assistant")
            AddQuestion(state.PendingQuestion ?? "Help me understand this part of the game.", true);
        var epoch=Interlocked.Increment(ref nextOperation);
        model.Update(s=>s with {Capturing=true,Error=null,PreviewReadOnly=false,CaptureEpoch=epoch,CaptureOptions=false});
        Operations.RunLatest("capture",async context=>
        {
            WindowCaptureTicket? ticket=null; var complete=false;
            try
            {
                if(model.Value.Preview is { } prior) { await HostServices.Capture.DiscardAsync(prior,context.CancellationToken); model.Update(s => s with { Preview = null, Context = s.Context == prior ? null : s.Context }); }
                ticket=await HostServices.Capture.RequestAsync(kind,context.CancellationToken);
                if(context.IsCurrent) model.Update(s=>s with {Ticket=ticket,Preview=null});
                while(true)
                {
                    await Task.Delay(300,context.CancellationToken);
                    var result=await HostServices.Capture.GetStatusAsync(ticket,context.CancellationToken);
                    if(result.Phase==WindowCapturePhase.Ready)
                    {complete=true;if(context.IsCurrent)model.Update(s=>s with {Capturing=false,Preview=result.Attachment,OfferContext=false,Ticket=null});break;}
                    if(result.Phase is WindowCapturePhase.Failed or WindowCapturePhase.Cancelled)
                        throw new GameHelpException(result.ErrorCode??"capture_failed",result.Phase==WindowCapturePhase.Cancelled?"Capture cancelled.":"Capture could not finish. Bring your game to the foreground before the countdown ends and keep it there during recording, then retry.");
                }
            }
            catch(OperationCanceledException) when(context.CancellationToken.IsCancellationRequested) { }
            catch(Exception error) {if(context.IsCurrent)model.Update(s=>s with {Error=Friendly(error)});}
            finally
            {
                if(!complete && ticket is not null) {try{await HostServices.Capture.CancelAsync(ticket,WidgetLifetimeToken);}catch(Exception){}}
                model.Update(s=>s.CaptureEpoch==epoch ? s with {Capturing=false,Ticket=null} : s);
            }
        },WidgetOperationLifetime.Widget);
    }
    private void Answer(CaptureAttachment? attachment)
    {
        var state=model.Value;
        if(!state.HasKey || string.IsNullOrWhiteSpace(state.PendingQuestion) || state.Busy)return;
        if(attachment?.ExpiresAtUnixMilliseconds<=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        { model.Update(s=>s with {Error="This capture expired. Capture again or answer without it.",OfferContext=true}); return; }
        var contextAttachment=attachment;
        var epoch=Interlocked.Increment(ref nextOperation);
        model.Update(s=>s with {Busy=true,OfferContext=false,PreviewReadOnly=true,Error=null,AnswerEpoch=epoch,Context=contextAttachment,
            Reveal = s.Messages.LastOrDefault(message => message.Role == "user") is { } latest
                ? new(Interlocked.Increment(ref nextRevealRequest), "help.transcript", "help.message." + latest.Id) : null });
        Operations.RunLatest("answer",async context=>
        {
            var phase = "conversation-preparation";
            try
            {
                var history=new List<GameHelpTurn>();var length=0;
                foreach(var message in state.Messages.Reverse())
                {
                    var detail = message.SolutionExpanded ? message.Answer?.DetailedSolution : null;
                    var hasDetail = !string.IsNullOrWhiteSpace(detail);
                    if(history.Count + (hasDetail ? 2 : 1)>16 || length+message.Text.Length+(hasDetail ? detail!.Length : 0)>22000)break;
                    if(hasDetail) {history.Add(new("assistant",detail!));length+=detail!.Length;}
                    history.Add(new(message.Role,message.Text));length+=message.Text.Length;
                }
                history.Reverse();
                if(state.Context is { } old && old!=contextAttachment) {try{await HostServices.Capture.DiscardAsync(old,context.CancellationToken);}catch(WidgetCapabilityException){}}
                phase = "provider-service";
                var answer=await service.AskAsync(new(contextAttachment?.SourceApplication?.ApplicationName ?? "Unknown application",
                    contextAttachment?.SourceApplication?.WindowTitle ?? "",state.Spoilers,history,state.Grounding,contextAttachment),context.CancellationToken);
                phase = "answer-presentation";
                if(context.IsCurrent)
                {
                    if(!context.IsCurrent)return;
                    model.Update(s=>s with {Messages=s.Messages.Append(new Message(Interlocked.Increment(ref nextMessage),"assistant",answer.Answer,answer)).TakeLast(20).ToArray(),
                        Context=contextAttachment,Preview=null,Busy=false,Error=null});
                    var retained = model.Value.Messages.Select(message => message.Answer?.Attribution).OfType<ProviderDocumentReference>().ToHashSet();
                    foreach (var reference in state.Messages.Select(message => message.Answer?.Attribution).OfType<ProviderDocumentReference>().Where(reference => !retained.Contains(reference)))
                        try { await HostServices.Documents.DiscardAsync(reference, WidgetLifetimeToken); } catch (WidgetCapabilityException) { }
                }
            }
            catch(OperationCanceledException) when(context.CancellationToken.IsCancellationRequested) { }
            catch(Exception error)
            {
                if(context.IsCurrent)
                {
                    if (error is not (GameHelpException or WidgetCapabilityException))
                    {
                        var diagnostics = new GameHelpRequestDiagnostics(state.Grounding);
                        diagnostics.SetStage(phase);
                        error = diagnostics.Failed(error);
                    }
                    model.Update(s=>s with {Error=Friendly(error)});
                }
            }
            finally {model.Update(s=>s.AnswerEpoch==epoch ? s with {Busy=false} : s);}
        },WidgetOperationLifetime.Widget);
    }
    private void DiscardPreview()
    {
        var attachment=model.Value.Preview;
        model.Update(s=>s with {Preview=null,Context=s.Context==attachment?null:s.Context,PreviewReadOnly=false,OfferContext=s.PendingQuestion is not null});
        if(attachment is not null)Operations.RunSingleFlight("discard",async context=> {try{await HostServices.Capture.DiscardAsync(attachment,context.CancellationToken);}catch(Exception){}},WidgetOperationLifetime.Widget);
    }
    private void NewConversation()
    {
        Operations.Cancel("answer");Operations.Cancel("capture");
        var state=model.Value;
        model.Update(s=>new State {HasKey=s.HasKey,KeyReady=s.KeyReady,Spoilers=s.Spoilers,Grounding=s.Grounding});
        var attachments=new[]{state.Preview,state.Context}.OfType<CaptureAttachment>().Distinct().ToArray();
        Operations.RunSingleFlight("discard",async context=>
        {
            foreach(var attachment in attachments){try{await HostServices.Capture.DiscardAsync(attachment,context.CancellationToken);}catch(WidgetCapabilityException){}}
            foreach(var reference in state.Messages.Select(message => message.Answer?.Attribution).OfType<ProviderDocumentReference>())
                try { await HostServices.Documents.DiscardAsync(reference, context.CancellationToken); } catch (WidgetCapabilityException) { }
        },WidgetOperationLifetime.Widget);
    }
    protected override ValueTask OnDestroyingAsync(CancellationToken shutdownToken) { if(service is IDisposable disposable)disposable.Dispose();return ValueTask.CompletedTask; }
    private static string Friendly(Exception error)=>error switch
    {
        GameHelpException help=>help.Message,
        WidgetCapabilityException capability when capability.ErrorCode is "permission_denied" or "capability_revoked"=>"Allow Game Help's permissions in Settings, then reload the widget.",
        WidgetCapabilityException=>"Capture is unavailable. Check capture permission or try capturing again.",
        _=>"The request could not finish. Try again.",
    };
    private static string[] Split(string text)=>Enumerable.Range(0,Math.Max(1,(text.Length+2999)/3000)).Select(i=>text.Substring(i*3000,Math.Min(3000,text.Length-i*3000))).ToArray();
    private static ButtonElement Source(GameHelpSource source,string id, string? label = null)
    {
        var uri=new Uri(source.Url);
        var query=uri.Query.TrimStart('?').Split('&').Select(p=>p.Split('=',2)).Where(p=>p.Length==2).GroupBy(p=>p[0],StringComparer.OrdinalIgnoreCase).ToDictionary(group=>group.Key,group=>group.Last()[1],StringComparer.OrdinalIgnoreCase);
        var video=uri.Host.Equals("youtu.be",StringComparison.OrdinalIgnoreCase)?uri.AbsolutePath.Trim('/'):uri.Host is "www.youtube.com" or "youtube.com" ? query.GetValueOrDefault("v") : null;
        var button=UI.Button(label ?? source.Title,"unused",id);
        if (video is {Length:11} && video.All(c=>char.IsAsciiLetterOrDigit(c)||c is '_' or '-') && TryStartTime(uri, query, out var start))
            return button.OpenIntent(WidgetIntentContracts.Video, start is { } seconds
                ? JsonSerializer.SerializeToElement(new { provider="youtube", videoId=video, startTimeSeconds=seconds })
                : JsonSerializer.SerializeToElement(new { provider="youtube", videoId=video }),
                presentation: WidgetIntentPresentation.PreferExistingSurface);
        return button.OpenIntent(WidgetIntentContracts.Web,JsonSerializer.SerializeToElement(new{url=source.Url}),
            presentation: WidgetIntentPresentation.PreferExistingSurface);
    }
    private static bool TryStartTime(Uri uri, IReadOnlyDictionary<string,string> query, out double? seconds)
    {
        seconds = null;
        var raw = query.GetValueOrDefault("t") ?? query.GetValueOrDefault("start") ?? query.GetValueOrDefault("time_continue");
        if (raw is null && uri.Fragment.StartsWith("#t=", StringComparison.OrdinalIgnoreCase)) raw = uri.Fragment[3..];
        if (raw is null) return true;
        raw = Uri.UnescapeDataString(raw);
        if (raw.Length is 0 or > 32) return false;
        if (double.TryParse(raw, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var number))
        { if (!double.IsFinite(number) || number is < 0 or > 86400) return false; seconds = number; return true; }
        var match = System.Text.RegularExpressions.Regex.Match(raw, "^(?:(?<h>[0-9]{1,2})h)?(?:(?<m>[0-9]{1,4})m)?(?:(?<s>[0-9]{1,5})s)?$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
        if (!match.Success) return false;
        static int Part(System.Text.RegularExpressions.Group group) => group.Success ? int.Parse(group.Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
        number = Part(match.Groups["h"]) * 3600 + Part(match.Groups["m"]) * 60 + Part(match.Groups["s"]);
        if (number > 86400) return false;
        seconds = number; return true;
    }
    private static readonly string[] Starters=["Help me understand this area","How should I approach this boss?","Help me improve my build","Help me find an item"];
    private static readonly string[] Spoilers=["Hints first","Balanced","Full solution"];
}
