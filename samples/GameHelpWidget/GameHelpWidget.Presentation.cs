using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.GameHelp;

public sealed partial class GameHelpWidget
{
    public override WidgetView Render()
    {
        var state = model.Value;
        var view = state.HasKey ? ChatView(state) : SetupView(state);
        if (state.ConfirmNew)
            return view.WithModal(new("help.new-confirm", "Start a new chat?",
                UI.Stack("help.new-confirm.content",
                    UI.Text("This clears this conversation and its captures. Your preferences are kept.", "help.new-confirm.copy"),
                    UI.Row("help.new-confirm.actions", UI.Button("Keep chatting", "new.cancel", "help.new-cancel"),
                        UI.Button("Start new chat", "new.confirm", "help.new-proceed"))), "help.new-cancel", "new.cancel"));
        if (state.ConfirmDelete)
            return view.WithModal(new("help.delete-confirm", "Remove your API key?",
                UI.Stack("help.delete-confirm.content",
                    UI.Text("You'll need to add a key before asking another question. This does not delete your Google account or API key at Google.", "help.delete-confirm.copy"),
                    UI.Row("help.delete-confirm.actions", UI.Button("Keep key", "key.delete.cancel", "help.delete-cancel"),
                        UI.Button("Remove key", "key.delete.confirm", "help.delete-proceed"))), "help.delete-cancel", "key.delete.cancel"));
        if (state.Settings) return SettingsView(view, state);
        if (state.CaptureOptions) return CaptureOptionsView(view, state);
        if (state.Preview is { } preview && !state.Capturing && !state.PreviewReadOnly)
            return PreviewView(view, state, preview);
        return view;
    }

    private static WidgetSurfaceHints Surface => new()
    {
        Mode = WidgetSurfaceMode.Standard, WidthMode = WidgetSurfaceAxisMode.Preferred,
        HeightMode = WidgetSurfaceAxisMode.Preferred, PreferredWidth = 1040, PreferredHeight = 800,
        MinimumWidth = 560, MinimumHeight = 440,
    };

    private static WidgetView SetupView(State state)
    {
        var content = new List<WidgetElement>
        {
            UI.Text("Your companion in the game", "help.welcome").Classes("help-hero"),
            UI.Text("Ask a question, share what you're seeing, and pick up where you left off.", "help.setup-copy").Classes("help-muted"),
            UI.Stack("help.setup.account",
                UI.Text("Connect Gemini", "help.setup-label").Classes("help-section-title"),
                UI.Text("Add your API key to get started. It is encrypted for your Windows account.", "help.key-info").Classes("help-muted"),
                UI.SensitiveTextEntry("Add Gemini API key", "key.save", "help.key", 96))
                .Classes("help-section"),
            UI.Button("Get a key from Google AI Studio", "key.link", "help.key-link")
                .OpenIntent(WidgetIntentContracts.Web, JsonSerializer.SerializeToElement(new { url = "https://aistudio.google.com/apikey" }), routing: WidgetIntentRouting.Windows)
                .Classes("help-secondary"),
            UI.Text("Screenshots and clips are optional. Nothing is sent until you choose to send it. Your Google project's limits and pricing apply.", "help.setup-privacy").Classes("help-caption"),
        };
        if (!state.KeyReady) content.Add(UI.Text("Checking your saved key…", "help.setup-loading").Classes("help-muted"));
        if (state.Error is { } error) content.Add(ErrorPanel(error));
        var root = UI.Grid("help.root", [GridTrack.Auto(), GridTrack.Star()],
            [GridTrack.Star(), GridTrack.Star(10, maximum: 720), GridTrack.Star()],
            UI.Text("Game Help", "help.title").Classes("help-title").InGrid(0, 1),
            UI.VerticalScroll("help.setup-scroll", UI.Stack("help.setup-stage",
                UI.Stack("help.setup", content.ToArray()).Classes("help-setup-card")).Classes("help-setup-stage"))
                .Classes("help-setup-scroll").InGrid(1, 1))
            .Spacing(16, 0).Classes("help-root");
        return new(root, "help.key", Surface: Surface);
    }

    private WidgetView ChatView(State state)
    {
        var contextLabel = state.Context?.SourceApplication is { } source ? source.ApplicationName + " · Context"
            : state.Context is not null ? "Capture attached" : "Add context";
        var header = UI.Grid("help.header", [GridTrack.Auto()], [GridTrack.Auto(), GridTrack.Star(), GridTrack.Auto(), GridTrack.Auto()],
            UI.Text("Game Help", "help.title").Classes("help-title"),
            UI.Button(contextLabel, "context.open", "help.context").Classes("help-context-button").InGrid(0, 1),
            UI.Button("New chat", "new", "help.header-new").Classes("help-secondary").InGrid(0, 2),
            UI.Button("Options", "settings", "help.settings").Classes("help-secondary").InGrid(0, 3)).Spacing(0, 12);
        var messages = Transcript(state);
        var footer = Composer(state);
        var conversation = UI.Grid("help.conversation", [GridTrack.Star(), GridTrack.Auto()], [GridTrack.Star()],
            UI.VerticalScroll("help.transcript", UI.Stack("help.messages", messages.ToArray()).Classes("help-messages"))
                .Classes("help-transcript"), footer.InGrid(1)).Classes("help-conversation");
        var root = UI.Grid("help.root", [GridTrack.Auto(), GridTrack.Star()],
            [GridTrack.Star(), GridTrack.Star(16, maximum: 960), GridTrack.Star()],
            header.InGrid(0, 1), conversation.InGrid(1, 1))
            .Spacing(12, 0).Classes("help-root")
            .Shortcut(ControllerButton.Y, "settings", "Options")
            .Shortcut(ControllerButton.X, "context.open", "Add context")
            .Shortcut(ControllerButton.LeftBumper, "new", "New chat")
            .Shortcut(ControllerButton.RightStick, "compose.focus", "Write a question");
        return new(root, "help.composer", Surface: Surface)
        {
            ScrollRevealRequest = state.Reveal,
            FocusGroupEntryRequest = state.FocusRequest > 0 &&
                (state.FocusGroup == "help.compose-focus" || state.FocusGroup == "help.context-choice" && state.OfferContext)
                ? new() { RequestId = state.FocusRequest, GroupId = state.FocusGroup! } : null,
        };
    }

    private static List<WidgetElement> Transcript(State state)
    {
        var messages = new List<WidgetElement>();
        if (state.Messages.Count == 0)
        {
            string[] titles = ["Explore this area", "Get past a boss", "Improve my build", "Find an item"];
            string[] descriptions = ["Understand your surroundings", "Hints before the full solution", "Equipment, stats and playstyle", "A useful direction to look"];
            var prompts = Starters.Select((_, i) => UI.ActionSurface("starter." + i, "help.starter." + i,
                titles[i], ActionSurfaceOrientation.Vertical,
                UI.Stack("help.starter." + i + ".copy",
                    UI.Text(titles[i], "help.starter." + i + ".title").Classes("help-section-title"),
                    UI.Text(descriptions[i], "help.starter." + i + ".description").Classes("help-caption"))
                    .Classes("help-prompt-copy"))
                .Busy(state.Busy || state.Capturing).Classes("help-prompt").InGrid(i / 2, i % 2)).ToArray();
            messages.Add(UI.Stack("help.empty",
                UI.Stack("help.empty-heading",
                    UI.Text("Where do you need a hand?", "help.empty-title").Classes("help-hero"),
                    UI.Text("Choose a starting point, or ask your own question below.", "help.empty-copy").Classes("help-muted"))
                    .Classes("help-section-heading"),
                UI.Stack("help.starter-section",
                    UI.Text("START A CONVERSATION", "help.starters-label").Classes("help-eyebrow"),
                    UI.Grid("help.starters", [GridTrack.Auto(), GridTrack.Auto()], [GridTrack.Star(), GridTrack.Star()], prompts).Spacing(12, 12))
                    .Classes("help-starter-section")).Classes("help-empty"));
        }
        foreach (var message in state.Messages)
        {
            var id = "help.message." + message.Id;
            var parts = new List<WidgetElement>
            { UI.Text(message.Role == "user" ? "You" : "Game Help", id + ".author").Classes("help-author") };
            parts.AddRange(CitedText(message.Text, message.Answer, "hint", id + ".text", "help-message-text"));
            if (message.ContextLabel is { } label) parts.Add(UI.Text(label, id + ".attachment").Classes("help-caption"));
            if (message.Answer is { } answer)
            {
                if (!string.IsNullOrWhiteSpace(answer.DetailedSolution))
                {
                    parts.Add(UI.Button(message.SolutionExpanded ? "Hide detailed solution" : "Show detailed solution",
                        "solution." + message.Id, id + ".solution-toggle").Classes("help-followup"));
                    if (message.SolutionExpanded)
                        parts.Add(UI.Stack(id + ".solution",
                            new[] { UI.Text("Detailed solution", id + ".solution-title").Classes("help-section-title") }
                                .Concat(CitedText(answer.DetailedSolution, answer, "detailedSolution", id + ".solution-text", "help-message-text"))
                                .ToArray()).Classes("help-answer-section"));
                }
                for (var observation = 0; observation < answer.Observations.Count; ++observation)
                    parts.AddRange(CitedText(answer.Observations[observation], answer, "observations." + observation, id + ".observations." + observation, "help-muted"));
                if (!string.IsNullOrWhiteSpace(answer.Uncertainty))
                    parts.AddRange(CitedText(answer.Uncertainty, answer, "uncertainty", id + ".uncertainty", "help-caption"));
                if (answer.Sources.Count > 0)
                {
                    parts.Add(UI.Button((message.SourcesExpanded ? "Hide sources" : "Show sources") + $" ({answer.Sources.Count})",
                        "sources." + message.Id, id + ".sources-toggle").Classes("help-followup"));
                    if (message.SourcesExpanded)
                        parts.Add(UI.ResponsiveGrid(id + ".sources.links", 240, 3,
                            answer.Sources.Select((source, index) => Source(source, id + ".source." + index, $"[{index + 1}] {source.Title}").Classes("help-link")).ToArray())
                            .Classes("help-answer-section"));
                }
                // Search suggestions are separate from the collapsible aggregate citation list.
                if (answer.Attribution is { } attribution)
                    parts.Add(UI.ProviderContent(attribution, BrowserInteractionMode.ActivateToInteract,
                        id + ".attribution", "Google Search suggestions").Classes("help-attribution"));
                var searches = new List<WidgetElement>();
                if (answer.SearchQueries.Count > 0) searches.Add(UI.Text("Explore further", id + ".search-label").Classes("help-section-title"));
                for (var i = 0; i < answer.SearchQueries.Count; i++)
                {
                    var query = answer.SearchQueries[i];
                    searches.Add(UI.Stack(id + ".search." + i,
                        UI.Stack(id + ".query." + i, CitedText(query, answer, "searchQueries." + i, id + ".query-text." + i, "help-caption").ToArray()),
                        UI.Grid(id + ".search-actions." + i, [GridTrack.Auto()], [GridTrack.Star(), GridTrack.Star()],
                            UI.Button("Search web", "unused", id + ".web." + i).OpenIntent(WidgetIntentContracts.Web,
                                JsonSerializer.SerializeToElement(new { url = "https://www.google.com/search?q=" + Uri.EscapeDataString(query) })),
                            UI.Button("Search YouTube", "unused", id + ".youtube." + i).OpenIntent(WidgetIntentContracts.VideoSearch,
                                JsonSerializer.SerializeToElement(new { provider = "youtube", query }))
                                .WithIntentFeedback(WidgetIntentRequest.Create(WidgetIntentContracts.Web,
                                JsonSerializer.SerializeToElement(new { url = "https://www.youtube.com/results?search_query=" + Uri.EscapeDataString(query) }))).InGrid(0, 1)).Spacing(0, 8)));
                }
                if (searches.Count > 0) parts.Add(UI.Stack(id + ".searches", searches.ToArray()).Classes("help-answer-section"));
                var followups = new List<WidgetElement>();
                if (answer.ReplyOptions.Count > 0) followups.Add(UI.Text("Suggested replies", id + ".followup-label").Classes("help-section-title"));
                for (var i = 0; i < answer.ReplyOptions.Count; i++)
                    followups.Add(UI.Button(answer.ReplyOptions[i].Label, "suggest." + message.Id + "." + i, id + ".suggest." + i)
                        .Busy(state.Busy || state.Capturing).Classes("help-followup"));
                if (followups.Count > 0) parts.Add(UI.Stack(id + ".followups", followups.ToArray()).Classes("help-answer-section"));
            }
            messages.Add(UI.Stack(id, parts.ToArray()).Classes(message.Role == "user" ? "help-message-user" : "help-message-answer"));
        }
        if (state.OfferContext)
            messages.Add(UI.Stack("help.context-choice",
                UI.Text("Would a view of the game help?", "help.context-prompt").Classes("help-section-title"),
                UI.Text("Share a screenshot or short clip, or continue with just your question.", "help.context-choice-copy").Classes("help-muted"),
                UI.Grid("help.context-actions", [GridTrack.Auto(), GridTrack.Auto()], [GridTrack.Star(), GridTrack.Star()],
                    UI.Button("Screenshot", "capture.image", "help.capture-image").Busy(state.Capturing),
                    UI.Button("Record 5 seconds", "capture.video", "help.capture-video").Busy(state.Capturing).InGrid(0, 1),
                    UI.Button("Answer without capture", "ask.plain", "help.answer-plain").Busy(state.Capturing).InGrid(1, 0, columnSpan: 2)).Spacing(8, 8))
                .RememberChildFocus("help.answer-plain").Classes("help-context-choice"));
        if (state.Capturing)
            messages.Add(UI.Stack("help.capture-status", UI.Text("Capture in progress", "help.capture-title").Classes("help-section-title"),
                UI.Text("Return to the game. Reopen WidgetRail afterward to review your capture.", "help.capture-copy").Classes("help-muted"),
                UI.Button("Cancel capture", "capture.cancel", "help.capture-cancel")).Classes("help-status"));
        if (state.Busy)
            messages.Add(UI.Grid("help.answer-status", [GridTrack.Auto()], [GridTrack.Star(), GridTrack.Auto()],
                UI.Text("Preparing your answer…", "help.answer-copy").Classes("help-muted"),
                UI.Button("Cancel", "answer.cancel", "help.answer-cancel").InGrid(0, 1)).Spacing(0, 12).Classes("help-status"));
        return messages;
    }

    private static WidgetElement Composer(State state)
    {
        var controls = new List<WidgetElement>();
        if (state.Error is { } error) controls.Add(ErrorPanel(error));
        if (state.Preview is not null && !state.Capturing)
            controls.Add(UI.Button("Review attached capture", "preview.open", "help.preview-open").Classes("help-attachment"));
        var entry = UI.Stack("help.compose-focus",
            UI.TextEntry(state.Draft, "Ask about the game…", "draft", "help.composer", 2048).Classes("help-composer"))
            .RememberChildFocus("help.composer");
        var compose = UI.Grid("help.composer-row", [GridTrack.Auto()], [GridTrack.Star(), GridTrack.Auto()], entry,
            UI.Button("Send", "ask", "help.send").Disabled(string.IsNullOrWhiteSpace(state.Draft)).Busy(state.Busy || state.Capturing).InGrid(0, 1)).Spacing(0, 12);
        if (!state.Busy && !state.Capturing && !string.IsNullOrWhiteSpace(state.Draft))
            compose = compose.Shortcut(ControllerButton.RightTrigger, "ask", "Send question");
        controls.Add(compose);
        return UI.Stack("help.footer", controls.ToArray()).Classes("help-footer");
    }

    private static WidgetElement ErrorPanel(string message, string id = "help.error") => UI.Stack(id + ".panel",
        UI.Text("Couldn't complete that", id + ".label").Classes("help-section-title"),
        UI.Text(message, id)).Classes("help-error-panel");

    private static WidgetView SettingsView(WidgetView view, State state)
    {
        var content = new List<WidgetElement>
        {
            UI.Stack("help.options.answers", UI.Text("Answers", "help.options.answers-title").Classes("help-section-title"),
                UI.Text("Spoiler level", "help.spoilers-label").Classes("help-muted"),
                UI.Select(state.Spoilers, Spoilers.Select((value, index) => new SelectOption("spoiler." + index, value, "spoiler." + index, state.Spoilers == value)).ToArray(), "help.spoilers"),
                UI.Text("Hints first keeps the visible hint spoiler-light. Detailed solutions expand only when you choose.", "help.spoilers-copy").Classes("help-caption"))
                .Classes("help-section"),
            UI.Stack("help.options.search", UI.Text("Live web search", "help.options.search-title").Classes("help-section-title"),
                UI.Select("Google Search", [new("off", "Off · model knowledge", "grounding.off", !state.Grounding),
                    new("on", "On · paid API quota", "grounding.on", state.Grounding)], "help.grounding"),
                UI.Text("Optional. Uses eligible paid Google API quota. Google retains search prompts and context for 30 days.", "help.search-info").Classes("help-caption"))
                .Classes("help-section"),
            UI.Stack("help.options.account", UI.Text("Gemini account", "help.options.account-title").Classes("help-section-title"),
                UI.Text(state.HasKey ? "API key saved on this PC" : "No API key saved", "help.key-status").Classes("help-muted"),
                UI.SensitiveTextEntry("Replace API key", "key.save", "help.options-key", 96).Disabled(state.Busy || state.Capturing),
                UI.Button("Remove saved key", "key.delete", "help.delete-key").Busy(state.Busy || state.Capturing))
                .Classes("help-section"),
            UI.Stack("help.options.chat", UI.Text("This conversation", "help.options.chat-title").Classes("help-section-title"),
                UI.Button("New chat", "new", "help.new"),
                UI.Button("Retry last question", "answer.retry", "help.retry").Disabled(state.PendingQuestion is null).Busy(state.Busy || state.Capturing),
                UI.Text("Chat stays in this session. Captures are sent only when you select Send to Gemini and expire locally after 30 minutes.", "help.privacy").Classes("help-caption"))
                .Classes("help-section"),
        };
        if (state.Error is { } error) content.Add(ErrorPanel(error, "help.options.error"));
        return view.WithModal(new("help.options", "Chat options", UI.Stack("help.options-content", content.ToArray()).Classes("help-dialog"),
            "help.spoilers", "settings.close"));
    }

    private static WidgetView CaptureOptionsView(WidgetView view, State state)
    {
        var content = new List<WidgetElement>
        {
            UI.Stack("help.capture-source",
                UI.Text("Capture what's in front", "help.capture-source-label").Classes("help-section-title"),
                UI.Text("WidgetRail will hide. You have five seconds to bring your game to the foreground before capture starts.", "help.capture-instructions").Classes("help-muted"))
                .Classes("help-section"),
            UI.Grid("help.capture-choices", [GridTrack.Auto()], [GridTrack.Star(), GridTrack.Star()],
                UI.Button("Take screenshot", "capture.image", "help.add-image").Busy(state.Busy || state.Capturing),
                UI.Button("Record 5 seconds", "capture.video", "help.add-video").Busy(state.Busy || state.Capturing).InGrid(0, 1)).Spacing(0, 12),
            UI.Text("Recordings contain video only. Reopen WidgetRail to review the capture before sending anything to Gemini.", "help.capture-privacy").Classes("help-caption"),
        };
        if (state.Error is { } actionError) content.Add(ErrorPanel(actionError, "help.capture.error"));
        return view.WithModal(new("help.capture-options", "Add game context", UI.Stack("help.capture-options.content", content.ToArray()).Classes("help-dialog"),
            "help.add-image", "context.close"));
    }

    private static WidgetView PreviewView(WidgetView view, State state, CaptureAttachment preview)
    {
        var content = UI.Stack("help.preview-content",
            UI.Text(preview.SourceApplication is { } captured ? "Captured from " + captured.ApplicationName : "Captured application unavailable", "help.preview-application").Classes("help-caption"),
            UI.MediaPlayer(MediaPlayerSource.FromCapture(preview), "help.preview-media", preview.ContentType == "video/mp4" ? "Recorded game context" : "Game screenshot", new(Muted: true)).Classes("help-preview-media"),
            UI.Stack("help.preview-summary", UI.Text("Your question", "help.preview-question-label").Classes("help-section-title"),
                UI.Stack("help.preview-question-group",
                    UI.TextEntry(state.PendingQuestion ?? "", "Ask about this capture…", "preview.question", "help.preview-question", 2048).Disabled(state.Busy))
                    .RememberChildFocus("help.preview-question"),
                UI.Text("Send this capture and your question to Google Gemini?", "help.preview-consent").Classes("help-caption")),
            UI.Grid("help.preview-actions", [GridTrack.Auto(), GridTrack.Auto()], [GridTrack.Star(), GridTrack.Star()],
                UI.Button("Send to Gemini", "preview.send", "help.preview-send").Disabled(string.IsNullOrWhiteSpace(state.PendingQuestion)).Busy(state.Busy).InGrid(0, 0, columnSpan: 2),
                UI.Button("Retry capture", "preview.retry", "help.preview-retry").Busy(state.Busy).InGrid(1),
                UI.Button("Discard", "preview.discard", "help.preview-discard").Busy(state.Busy).InGrid(1, 1)).Spacing(8, 12))
            .Classes("help-dialog");
        if (!state.Busy)
        {
            content = content.Shortcut(ControllerButton.X, "preview.retry", "Retry capture")
                .Shortcut(ControllerButton.RightStick, "preview.question.focus", "Edit question");
            if (!string.IsNullOrWhiteSpace(state.PendingQuestion)) content = content.Shortcut(ControllerButton.Y, "preview.send", "Send to Gemini");
        }
        return view.WithModal(new("help.preview", "Review your capture", content,
            string.IsNullOrWhiteSpace(state.PendingQuestion) ? "help.preview-question" : "help.preview-send", "preview.close")) with
        {
            FocusGroupEntryRequest = state.FocusGroup == "help.preview-question-group" && !state.Busy
                ? new() { RequestId = state.FocusRequest, GroupId = "help.preview-question-group" } : null,
        };
    }
}
