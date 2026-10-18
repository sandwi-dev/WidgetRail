using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.SdkGalleryWidget;

public sealed partial class SdkGalleryWidget
{
    private sealed record DemoState
    {
        public string Text { get; init; } = "";
        public bool SecretReceived { get; init; }
        public bool Expanded { get; init; }
        public double Level { get; init; } = 50;
        public int Messages { get; init; }
        public ScrollRevealRequest? Reveal { get; init; }
        public string Surface { get; init; } = "none";
        public bool ActivateBrowser { get; init; } = true;
        public bool WebAppWorkProfile { get; init; }
        public bool WebAppKeepAlive { get; init; }
        public bool ModalOpen { get; init; }
        public bool Loop { get; init; }
        public string MediaUrl { get; init; } = "";
        public long MediaRevision { get; init; } = 1;
        public ProviderDocumentReference? Document { get; init; }
        public CaptureAttachment? Capture { get; init; }
        public CaptureCapabilities? CaptureCapabilities { get; init; }
        public CaptureAudioMode CaptureAudio { get; init; } = CaptureAudioMode.None;
        public string Status { get; init; } = "Choose a demo. Nothing opens or records automatically.";
    }
    private readonly WidgetModel<DemoState> _demo;

    private StackElement ModernControls() => UI.Stack("gallery.modern-controls",
        UI.SettingsField("gallery.field", "Level", UI.Slider(_demo.Value.Level, 0, 100, 5,
            "gallery.level", "gallery.level.direct", "Level: adjust on focus"),
            "Adjust on focus, or use the second slider to enter with A and leave with B."),
        UI.Slider(_demo.Value.Level, 0, 100, 5, "gallery.level", "gallery.level.explicit", "Level: press A to adjust")
            .RequireControllerActivation(),
        UI.Row("gallery.busy.row",
            UI.Button("Run two-second task", "gallery.busy", "gallery.busy").Busy(Operations.IsBusy("gallery.busy")),
            UI.Button("Unavailable action", "gallery.unavailable", "gallery.disabled").Disabled()),
        UI.Text("Busy controls keep their focus while preventing duplicate activation. Disabled controls are unavailable.", "gallery.busy.help"),
        UI.SectionHeader("Line graphs", "gallery.graph.header", description: "Informational charts have no controller focus stop. Null samples leave a gap."),
        UI.LineGraph("Example load history", new LineGraphDefinition(
            [new(-60, 20), new(-50, 35), new(-40, 30), new(-30, null), new(-20, 55), new(-10, 85), new(0, 65)],
            -60, 0, 0, 100, "%"), "gallery.graph"),
        UI.Text("This is illustrative data. Authors publish up to 120 ordered samples, units, and axis bounds; the host draws the graph using the active theme.", "gallery.graph.help"));

    private StackElement TextPage()
    {
        var state = _demo.Value;
        var content = new List<WidgetElement>
        {
            UI.SectionHeader("Text and input", "gallery.text.header", description: "Native text, controller keyboard, inline links, and scroll reveal."),
            UI.SettingsField("gallery.text.field", "Example message",
                UI.TextEntry(state.Text, "Type a message", "gallery.text.commit", "gallery.text.entry", 240),
                "Select to open the shared keyboard. Ordinary keyboard input and paste also work."),
            UI.Text(state.Text.Length == 0 ? "No message entered." : state.Text, "gallery.text.echo"),
            UI.SensitiveTextEntry("Enter a dummy secret", "gallery.secret.commit", "gallery.secret", 120),
            UI.Text(state.SecretReceived ? "Dummy secret received and immediately discarded." : "Use dummy text only. This demo never stores or echoes the secret.", "gallery.secret.status"),
            UI.RichText("gallery.rich",
                UI.Text("Rich text keeps ", "gallery.rich.before"),
                UI.InlineLink("a web link", "gallery.rich.link", WebRequest("https://example.com"), "Open example.com"),
                UI.Text(" and a compact reference ", "gallery.rich.middle"),
                UI.InlineLink("[1]", "gallery.rich.citation", WebRequest("https://example.com"), "Reference 1: example.com").Classes("gallery-inline-citation"),
                UI.Text(" inside the same wrapping paragraph. Links are reachable with the controller.", "gallery.rich.after")),
            UI.Button(state.Expanded ? "Hide details" : "Show details", "gallery.details", "gallery.details"),
        };
        if (state.Expanded)
            content.Add(UI.Card("gallery.details.body", CardVariant.Subtle,
                UI.Text("This expansion is ordinary widget state and composition, not HTML or another browser.", "gallery.details.copy")));
        content.Add(UI.Button("Append message and reveal it", "gallery.message", "gallery.message"));
        for (var i = Math.Max(1, state.Messages - 5); i <= state.Messages; i++)
            content.Add(UI.Text($"Message {i}: the shared scroll container reveals this new item once, without moving focus away from the button.", "gallery.message." + i));
        content.Add(UI.Text("The remaining text has no focusable controls. Keep using the D-pad or left stick at the focus boundary to scroll to the end of this container.\n\n" +
            string.Join("\n\n", Enumerable.Repeat("Native text follows the active theme, text scale, and available width. Containers need no artificial focus target to make this content reachable.", 5)), "gallery.text.scroll-tail"));
        return UI.Stack("gallery.text", content.ToArray()).Classes("gallery-page");
    }

    private StackElement SurfacesPage()
    {
        var state = _demo.Value;
        var content = new List<WidgetElement>
        {
            UI.SectionHeader("Web and media", "gallery.surfaces.header", description: "One live demo at a time. Playback never starts automatically."),
            UI.Select("Demo", new[] { "none", "browser", "webapp", "player", "document", "capture" }.Select(mode =>
                new SelectOption("gallery.surface.option." + mode, mode switch { "none" => "Choose a demo", "browser" => "Browser", "webapp" => "Persistent webapp", "player" => "Native media player", "document" => "Provider document", _ => "Capture preview" },
                    "gallery.surface." + mode, state.Surface == mode)).ToArray(), "gallery.surfaces.choose", "Choose a web or media demo"),
            UI.Text(state.Status, "gallery.surfaces.status"),
        };
        switch (state.Surface)
        {
            case "browser":
                content.Add(UI.Switch("Press A to interact", state.ActivateBrowser, "gallery.browser.mode", "gallery.browser.mode"));
                content.Add(UI.Text("On: A enters and B leaves interaction. Off: focus starts interaction and B follows the normal host back behavior. Y opens URL/Google search; LS opens the toolbar.", "gallery.browser.help"));
                content.Add(UI.WebBrowser(new("gallery.browser", 1, WebBrowserDocument.StartPage, "Gallery browser"),
                    state.ActivateBrowser ? BrowserInteractionMode.ActivateToInteract : BrowserInteractionMode.InteractOnFocus, "gallery.browser").Classes("gallery-live-surface"));
                break;
            case "webapp":
                content.Add(UI.Switch("Use separate work profile", state.WebAppWorkProfile, "gallery.webapp.account", "gallery.webapp.account"));
                content.Add(UI.Switch("Keep session when Gallery is hidden", state.WebAppKeepAlive, "gallery.webapp.background", "gallery.webapp.background"));
                content.Add(UI.Text("Persistent website data is isolated by widget and profile. LS opens controls, permissions and clear-data; Menu opens page actions. Switching profiles reloads the page. Leaving this demo removes its declaration and closes the session.", "gallery.webapp.help"));
                content.Add(UI.WebBrowser(new("gallery.webapp", 1, "https://example.com", "Gallery webapp")
                {
                    Options = WebBrowserOptions.WebApp with
                    {
                        ProfileKey = state.WebAppWorkProfile ? "gallery.work" : "gallery.personal",
                        Background = state.WebAppKeepAlive ? BrowserBackgroundMode.KeepAlive : BrowserBackgroundMode.Automatic,
                    },
                }, BrowserInteractionMode.InteractOnFocus, "gallery.webapp").Classes("gallery-live-surface"));
                break;
            case "player":
                content.Add(UI.Text("The bundled clip works offline. Optionally enter a direct HTTP(S) media URL; a web page or YouTube URL is not a media file.", "gallery.player.help"));
                content.Add(UI.TextEntry(state.MediaUrl, "Optional direct media URL", "gallery.media.url", "gallery.media.url", 2048));
                content.Add(UI.Button("Use bundled clip", "gallery.media.reset", "gallery.media.reset"));
                content.Add(UI.Switch("Loop playback", state.Loop, "gallery.media.loop", "gallery.media.loop"));
                content.Add(UI.Button("Open player in modal", "gallery.media.modal.open", "gallery.media.modal.open"));
                if (!state.ModalOpen) content.Add(UI.MediaPlayer(state.MediaUrl.Length == 0 ? MediaPlayerSource.PackageAsset("payload/media/sample.mp4") : MediaPlayerSource.WebUrl(state.MediaUrl),
                    "gallery.player", "Gallery native media player", new(Loop: state.Loop), state.MediaRevision).Classes("gallery-live-surface"));
                break;
            case "document":
                content.Add(UI.Text("Requires the optional presentation.documents.v1 permission. The host sanitizes HTML and blocks scripts, forms, and background network access.", "gallery.document.help"));
                content.Add(UI.Button("Create sample document", "gallery.document.create", "gallery.document.create").Busy(Operations.IsBusy("gallery.service")));
                if (state.Document is { } document)
                    content.Add(UI.ProviderContent(document, BrowserInteractionMode.ActivateToInteract, "gallery.document", "Sample provider document").Classes("gallery-live-surface"));
                break;
            case "capture":
                content.Add(UI.Text("Requires the optional capture permission. The host explains and confirms capture before hiding the overlay. Reopen it to review the result. Nothing is uploaded.", "gallery.capture.help"));
                content.Add(UI.Row("gallery.capture.permissions",
                    UI.Button("Check access", "gallery.permission.check", "gallery.permission.check").Busy(Operations.IsBusy("gallery.service")),
                    UI.Button("Request access", "gallery.permission.request", "gallery.permission.request").Busy(Operations.IsBusy("gallery.service"))));
                content.Add(UI.Text(_demo.Value.Status, "gallery.capture.status"));
                content.Add(UI.Button("Inspect capture options", "gallery.capture.capabilities", "gallery.capture.capabilities").Busy(Operations.IsBusy("gallery.service")));
                if (state.CaptureCapabilities is { AudioModes.Count: > 0 } available)
                    content.Add(UI.SettingsField("gallery.capture.audio.row", "Recording audio",
                        UI.Select("Recording audio", available.AudioModes.Select(mode => new SelectOption("audio." + mode,
                            mode == CaptureAudioMode.None ? "No audio" : "Application audio", "gallery.capture.audio." + mode,
                            state.CaptureAudio == mode)).ToArray(), "gallery.capture.audio").Busy(Operations.IsBusy("gallery.service")),
                        "Only host-advertised modes are offered. This demo records five seconds at 720p/15 fps."));
                content.Add(UI.Row("gallery.capture.actions",
                    UI.Button("Take screenshot", "gallery.capture.image", "gallery.capture.image").Busy(Operations.IsBusy("gallery.service")),
                    UI.Button("Record five seconds", "gallery.capture.video", "gallery.capture.video").Busy(Operations.IsBusy("gallery.service"))));
                if (state.Capture is { } capture)
                {
                    content.Add(capture.ContentType == "video/mp4"
                        ? UI.MediaPlayer(MediaPlayerSource.FromCapture(capture), "gallery.capture.player", "Recorded clip").Classes("gallery-live-surface")
                        : UI.CapturedMedia(capture, "gallery.capture.image-preview", "Captured screenshot").Classes("gallery-live-surface"));
                    content.Add(UI.Button("Discard capture", "gallery.capture.discard", "gallery.capture.discard").Busy(Operations.IsBusy("gallery.service")));
                }
                break;
        }
        return UI.Stack("gallery.surfaces", content.ToArray()).Classes("gallery-page");
    }

    private static WidgetIntentRequest WebRequest(string url, WidgetIntentRouting routing = WidgetIntentRouting.WidgetPreferred) =>
        WidgetIntentRequest.Create(WidgetIntentContracts.Web, JsonSerializer.SerializeToElement(new { url }), routing,
            WidgetIntentPresentation.PreferExistingSurface) with { ReportsResult = true };

    private StackElement IntentExamples() => UI.Stack("gallery.intents",
        UI.SectionHeader("Intents and pins", "gallery.intents.header", description: "Launch another widget through declared contracts; prefer its existing pinned surface."),
        UI.Button("Browse catalogue", "unused", "gallery.intent.marketplace").OpenIntent(WidgetIntentContracts.Marketplace,
            JsonSerializer.SerializeToElement(new { }),WidgetIntentPresentation.OpenWidget).WithIntentFeedback(),
        UI.Row("gallery.intents.actions",
            UI.Button("Open example.com", "unused", "gallery.intent.web").OpenIntent(WidgetIntentContracts.Web,
                JsonSerializer.SerializeToElement(new { url = "https://example.com" })).WithIntentFeedback(),
            UI.Button("Open in Windows browser", "unused", "gallery.intent.windows").OpenIntent(WidgetIntentContracts.Web,
                JsonSerializer.SerializeToElement(new { url = "https://example.com" }), WidgetIntentRouting.Windows).WithIntentFeedback()),
        UI.Button("Open sample YouTube video", "unused", "gallery.intent.video").OpenIntent(WidgetIntentContracts.Video,
            JsonSerializer.SerializeToElement(new { provider = "youtube", videoId = "aqz-KE-bpKQ", startTimeSeconds = 0 })).WithIntentFeedback(),
        UI.Button("Search YouTube for controller tutorials", "unused", "gallery.intent.search").OpenIntent(WidgetIntentContracts.VideoSearch,
            JsonSerializer.SerializeToElement(new { provider = "youtube", query = "controller tutorials" }))
            .WithIntentFeedback(WebRequest("https://www.youtube.com/results?search_query=controller%20tutorials")),
        UI.Text(_demo.Value.Status, "gallery.intent.status"),
        UI.Text("Open the tray command menu to try the Summary or Level controls pin layouts, full-widget pinning, or Replace pin. Pin controls use the same live state as the main view.", "gallery.pin.help"));

    private WidgetView ApplyDemoModal(WidgetView view)
    {
        var state = _demo.Value;
        if (!state.ModalOpen) return view;
        return view.WithModal(new("gallery.media.modal", "Native media preview", InitialFocusId: "gallery.media.modal.close", DismissActionId: "gallery.media.modal.close", Content:
            UI.MediaPlayer(state.MediaUrl.Length == 0 ? MediaPlayerSource.PackageAsset("payload/media/sample.mp4") : MediaPlayerSource.WebUrl(state.MediaUrl),
                "gallery.media.modal.player", "Modal media player", new(Loop: state.Loop), state.MediaRevision).Classes("gallery-live-surface")));
    }

    private IReadOnlyList<PinnedPresentationLayout> PinExamples() =>
    [
        WidgetView.PinnedLayout("gallery.summary", "Summary", new() { PreferredWidth = 360, PreferredHeight = 180 },
            UI.Stack("gallery.pin.summary", UI.Text("SDK Gallery", "gallery.pin.title"), UI.Text($"Level: {_demo.Value.Level:0}%", "gallery.pin.value"),
                UI.Button("Show feedback", "gallery.toast.show", "gallery.pin.feedback")), "gallery.pin.feedback"),
        WidgetView.PinnedLayout("gallery.level", "Level controls", new() { PreferredWidth = 420, PreferredHeight = 180 },
            UI.Stack("gallery.pin.controls", UI.Text("Shared level", "gallery.pin.label"),
                UI.Slider(_demo.Value.Level, 0, 100, 5, "gallery.level", "gallery.pin.slider", "Shared level")), "gallery.pin.slider"),
    ];

    public override ValueTask<bool> OnIntentCompletedAsync(WidgetIntentFeedback feedback, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _demo.Update(s => s with { Status = $"Intent result: {feedback.Status}." });
        return ValueTask.FromResult(feedback.Request.Fallback is not null && feedback.Status is WidgetIntentStatus.Unavailable or WidgetIntentStatus.Rejected);
    }

    private bool TryDemoAction(WidgetActionEvent action)
    {
        if (action.ActionId.StartsWith("gallery.capture.audio.", StringComparison.Ordinal))
        {
            if (Enum.TryParse<CaptureAudioMode>(action.ActionId["gallery.capture.audio.".Length..], out var mode) &&
                _demo.Value.CaptureCapabilities?.AudioModes.Contains(mode) == true)
                _demo.Update(s => s with { CaptureAudio = mode });
            return true;
        }
        if (action.ActionId.StartsWith("gallery.surface.", StringComparison.Ordinal))
        {
            var mode = action.ActionId["gallery.surface.".Length..];
            if (mode is "none" or "browser" or "webapp" or "player" or "document" or "capture") _demo.Update(s => s with { Surface = mode });
            return true;
        }
        switch (action.ActionId)
        {
            case "gallery.text.commit": _demo.Update(s => s with { Text = action.CommittedText ?? "" }); break;
            case "gallery.secret.commit": _demo.Update(s => s with { SecretReceived = action.CommittedText is { Length: > 0 } }); break;
            case "gallery.details": _demo.Update(s => s with { Expanded = !s.Expanded }); break;
            case "gallery.message": _demo.Update(s => s with { Messages = s.Messages + 1, Reveal = new((s.Reveal?.RequestId ?? 0) + 1, "gallery.page-scroll", "gallery.message." + (s.Messages + 1)) }); break;
            case "gallery.level" when action.RequestedValue is { } value && double.IsFinite(value): _demo.Update(s => s with { Level = Math.Clamp(value, 0, 100) }); break;
            case "gallery.browser.mode": _demo.Update(s => s with { ActivateBrowser = !s.ActivateBrowser }); break;
            case "gallery.webapp.account": _demo.Update(s => s with { WebAppWorkProfile = !s.WebAppWorkProfile }); break;
            case "gallery.webapp.background": _demo.Update(s => s with { WebAppKeepAlive = !s.WebAppKeepAlive }); break;
            case "gallery.media.modal.open": _demo.Update(s => s with { ModalOpen = true }); break;
            case "gallery.media.modal.close": _demo.Update(s => s with { ModalOpen = false }); break;
            case "gallery.media.loop": _demo.Update(s => s with { Loop = !s.Loop, MediaRevision = s.MediaRevision + 1 }); break;
            case "gallery.media.reset": _demo.Update(s => s with { MediaUrl = "", MediaRevision = s.MediaRevision + 1 }); break;
            case "gallery.media.url":
                var url = action.CommittedText?.Trim() ?? "";
                _demo.Update(s => url.Length == 0 || WebBrowserDocument.IsWebUrl(url)
                    ? s with { MediaUrl = url, MediaRevision = s.MediaRevision + 1 }
                    : s with { Status = "Use a direct HTTP(S) media URL." }); break;
            case "gallery.busy":
                if (!Operations.IsBusy("gallery.busy")) Operations.RunLatest("gallery.busy", async context =>
                { Invalidate(); try { await Task.Delay(2000, context.CancellationToken); } finally { Invalidate(); } });
                break;
            case "gallery.notifications.configure": RunService(async token => { await HostServices.Notifications.ConfigureAsync("examples", token); }); break;
            case "gallery.notifications.show": RunService(async token => { await HostServices.Notifications.ShowAsync(DemoNotification(), token); }); break;
            case "gallery.notifications.update": RunService(async token => { await HostServices.Notifications.UpdateAsync(DemoNotification() with { Body = "Content changed without extending the banner lifetime." }, token); }); break;
            case "gallery.notifications.dismiss": RunService(async token => { await HostServices.Notifications.DismissAsync("gallery-example", token); }); break;
            case "gallery.document.create": RunService(CreateDocumentAsync); break;
            case "gallery.permission.check": RunService(async token => {
                var states = await HostServices.Permissions.CheckAsync([WidgetCaptureCapabilities.Permission], token);
                _demo.Update(s => s with { Status = "Capture access: " + states.Single().State + ". Checking never opens a dialog." }); }); break;
            case "gallery.permission.request": RunService(async token => {
                var outcome = await HostServices.Permissions.RequestAsync([WidgetCaptureCapabilities.Permission], token);
                _demo.Update(s => s with { Status = "Permission request: " + outcome + ". This does not start a capture." }); }); break;
            case "gallery.capture.image": RunService(token => CaptureAsync(WindowCaptureKind.Screenshot, token)); break;
            case "gallery.capture.capabilities": RunService(async token => {
                var capabilities = await ReadCaptureCapabilitiesAsync(token);
                _demo.Update(s => s with { Status = capabilities.Available ? $"{capabilities.RecordingProfiles.Count} video profiles available. Capture validates the chosen settings before starting." : "Capture is unavailable on this PC." }); }); break;
            case "gallery.capture.video": RunService(token => CaptureAsync(WindowCaptureKind.Video, token)); break;
            case "gallery.capture.discard": RunService(DiscardCaptureAsync); break;
            default: return false;
        }
        return true;
    }

    private static WidgetNotification DemoNotification() => new("gallery-example", "SDK Gallery")
    {
        CategoryId = "examples", Body = "A quiet, host-rendered banner.", Visibility = WidgetNotificationVisibility.Always,
        Retention = WidgetNotificationRetention.BannerOnly, Interaction = WidgetNotificationInteraction.Informational,
    };

    private void RunService(Func<CancellationToken, Task> work)
    {
        if (Operations.IsBusy("gallery.service")) return;
        Operations.RunLatest("gallery.service", async context =>
        {
            _demo.Update(s => s with { Status = "Waiting for the host..." });
            try { await work(context.CancellationToken); }
            catch (WidgetCapabilityException error) { _demo.Update(s => s with { Status = $"Host service unavailable ({error.ErrorCode}). Check the gallery permissions in Settings." }); }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested) { }
            catch (Exception) { _demo.Update(s => s with { Status = "The demo could not complete. Try again." }); }
            finally { Invalidate(); }
        }, WidgetOperationLifetime.Widget);
    }

    private async Task CreateDocumentAsync(CancellationToken token)
    {
        if (_demo.Value.Document is { } prior)
        { _demo.Update(s => s with { Document = null }); await HostServices.Documents.DiscardAsync(prior, token); }
        var document = await HostServices.Documents.CreateAsync(["<h2>Provider document</h2><p>This is <strong>HTML</strong> with native host placement.</p><ul><li>Bounded, sanitized content</li><li>No browser toolbar</li></ul><p><a href='https://example.com'>Open an example page</a></p>"], token);
        _demo.Update(s => s with { Document = document, Status = "Document ready. Focus it, then press A to interact." });
    }

    private async Task DiscardCaptureAsync(CancellationToken token)
    {
        if (_demo.Value.Capture is not { } capture) return;
        await HostServices.Capture.DiscardAsync(capture, token);
        _demo.Update(s => s with { Capture = null, Status = "Capture discarded." });
    }

    private async Task CaptureAsync(WindowCaptureKind kind, CancellationToken token)
    {
        var capabilities = await ReadCaptureCapabilitiesAsync(token);
        var video = new RecordingOptions(Audio: _demo.Value.CaptureAudio);
        var hardware = video with { Encoder = CaptureEncoderMode.Hardware };
        if (capabilities.RecordingProfiles.Any(profile => profile.Supports(hardware))) video = hardware;
        var request = new CaptureSessionRequest { Screenshot = kind == WindowCaptureKind.Screenshot ? new() : null,
            Recording = kind == WindowCaptureKind.Video ? video : null, Preparation = new(5) };
        var validation = await HostServices.Capture.ValidateAsync(request, token);
        if (!validation.Supported)
        { _demo.Update(s => s with { Status = $"Capture settings unavailable ({validation.ErrorCode}). Your existing preview was kept." }); return; }
        await DiscardCaptureAsync(token);
        WindowCaptureTicket? ticket = null;
        var complete = false;
        try
        {
            ticket = await HostServices.Capture.StartAsync(request, token);
            while (true)
            {
                await Task.Delay(300, token);
                var status = await HostServices.Capture.GetStatusAsync(ticket, token);
                if (status.Phase == WindowCapturePhase.Ready)
                { complete = true; _demo.Update(s => s with { Capture = status.Attachment, Status = "Capture ready. This preview stays on your PC." }); return; }
                if (status.Phase is WindowCapturePhase.Failed or WindowCapturePhase.Cancelled)
                { _demo.Update(s => s with { Status = $"Capture {status.Phase.ToString().ToLowerInvariant()}. Try again when ready." }); return; }
            }
        }
        finally
        {
            if (!complete && ticket is not null)
                try { await HostServices.Capture.CancelAsync(ticket, WidgetLifetimeToken); }
                catch (Exception error) when (error is WidgetCapabilityException or OperationCanceledException) { }
        }
    }

    private async Task<CaptureCapabilities> ReadCaptureCapabilitiesAsync(CancellationToken token)
    {
        var available = await HostServices.Capture.GetCapabilitiesAsync(token);
        _demo.Update(s => s with { CaptureCapabilities = available });
        return available;
    }
}
