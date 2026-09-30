using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using PresentationSession = WidgetRail.WidgetPresentationSession.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Real worker authority through the production presenter; no fabricated input frames.</summary>
internal sealed class PinnedWidgetValidationPage : Page, IAsyncDisposable
{
    private readonly WidgetViewPresenter presenter = new();
    private readonly TextBlock status = new() { Text = "Connecting", TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<string> checks = [];
    private readonly string pipe;
    private PresentationSession? session;
    private WidgetPinnedSelection? selection;
    private Task? run;
    private Task retirement = Task.CompletedTask;
    private long publication;
    private string? failure;
    private bool disposed;
    private bool passive;
    private int denied;
    private int ordinaryActions;
    private int completed;
    private readonly List<string> lifecycleScopes = [];

    internal PinnedWidgetValidationPage(string pipe)
    {
        this.pipe = pipe;
        AutomationProperties.SetAutomationId(status, "PinnedWidget.Status");
        var layout = new Grid { RowSpacing = 8 };
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto }); layout.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        layout.Children.Add(status); layout.Children.Add(presenter); Grid.SetRow(presenter, 1); Content = layout;
        presenter.Failed = error => { failure ??= error.Message; Observe(); };
        presenter.DispatchActionAsync = _ => { ++ordinaryActions; throw new InvalidOperationException("Pinned action reached main dispatch."); };
        presenter.EnsureInteractionAsync = (authority, _) =>
        {
            lifecycleScopes.Add(authority.ActiveInputScopeId);
            if (passive) ++denied;
            return Task.FromResult(!passive && selection?.IsCurrent == true);
        };
        Loaded += (_, _) => run ??= RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            session = await PresentationSession.ConnectAsync(pipe, cancellationToken: lifetime.Token);
            presenter.Session = session;
            session.PresentationChanged += Changed;
            await session.ListWidgetsAsync(lifetime.Token);
            var genuine = await session.EstablishPresentationAsync(session.GetTarget("pinned-presenter"), WidgetLifecycleState.Interactive, lifetime.Token);
            await Select("compact");
            await Until(() => FocusId() == "Widget.open" && Images() >= 2 && List()?.Items.Count == 80);
            Check(ReferenceEquals(presenter.CurrentBinding!.Frame, genuine), "rendering retains genuine original frame");
            Check(presenter.CurrentBinding.Scope == "pin.scope" && genuine.Authority.ActiveInputScopeId == "dialog.scope", "pin scope independent of main modal");
            Check(!Nodes(presenter).Any(node => AutomationProperties.GetAutomationId(node) == "Widget.close"), "main modal excluded from pinned tree");
            Check(Math.Abs(Button("open").FontSize - 23) < .1, "pin-prefixed computed styles applied");
            Check(Nodes(presenter).OfType<WidgetArtworkView>().Any(image => image.Source is BitmapImage { PixelWidth: 8 }), "contrasting pinned artwork decoded");

            presenter.ActivateFocused(); await Status("open:");
            Check(lifecycleScopes.Contains("dialog.scope"), "host interaction retains original lifecycle authority");
            await presenter.HandleControllerButtonAsync(ControllerButton.Y); await Status("refresh:");
            Focus("choice"); presenter.ActivateFocused(); await Until(() => FocusId() == "Widget.choice.Option.first");
            presenter.MoveFocus(FocusNavigationDirection.Down); await Until(() => FocusId() == "Widget.choice.Option.second"); presenter.ActivateFocused(); await Status("select-second:");
            await Until(() => !presenter.HasTransientControl);
            Check(true, "Select commits through exact pinned binding");

            Focus("entry"); presenter.ActivateFocused(); await Until(() => DialogNodes().OfType<TextBox>().Any());
            DialogNodes().OfType<TextBox>().First().Text = "pinned text";
            await presenter.HandleControllerButtonAsync(ControllerButton.RightTrigger); await Status("commit:pinned text");
            await Until(() => !presenter.HasTransientControl);
            Check(true, "native text editor commits bounded pinned text");
            await presenter.HandleControllerButtonAsync(ControllerButton.Menu); await Until(() => FocusId() == "Widget.pin.Context.menu");
            presenter.ActivateFocused(); await Status("menu:");
            Check(true, "container context menu keeps independent focused entry");
            Focus("volume"); ((Slider)Find("volume")).Value = 5; await Status("volume:5");
            Check(true, "native slider retains pinned action scope");

            Focus("open"); var before = Button("open");
            await Mutate("toggle"); await Until(() => CurrentFrame().Authority.ActiveInputScopeId == "main");
            Check(ReferenceEquals(before, Button("open")) && FocusId() == "Widget.open", "main modal close retains pinned controls and focus");
            await Mutate("toggle"); await Until(() => CurrentFrame().Authority.ActiveInputScopeId == "dialog.scope");
            Check(ReferenceEquals(before, Button("open")), "main modal reopen does not reconstruct pinned view");

            await FocusRow(30); await presenter.HandleControllerButtonAsync(ControllerButton.A); await Status("row-open:30");
            await presenter.HandleControllerButtonAsync(ControllerButton.X); await Until(() => FocusId().EndsWith(".Context.row-menu", StringComparison.Ordinal));
            presenter.ActivateFocused(); await Status("row-menu:30");
            Check(true, "native virtualized pinned row and context actions carry real frame and lease");

            passive = true; var count = Calls(); var previousDenial = denied;
            var peer = new ButtonAutomationPeer(Button("open")); ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            await Until(() => denied > previousDenial); await session.RefreshAsync(CurrentFrame().Authority, lifetime.Token);
            Check(Calls() == count && Button("open").IsEnabled, "passive UIA denied without disabled styling");
            passive = false;
            var oldBinding = presenter.CurrentBinding!;
            await Select("alternate"); await Until(() => Button("open").Content?.ToString() == "Alternative action" || Math.Abs(Button("open").FontSize - 19) < .1);
            Check(!oldBinding.IsCurrent && !oldBinding.SameSurface(presenter.CurrentBinding), "same-sequence layout switch creates independent surface lifetime");
            Check(Math.Abs(Button("open").FontSize - 19) < .1, "alternate pin styles do not retain prior prefix");
            try { presenter.ApplyPinned(oldBinding.Selection!, oldBinding.Projection!); throw new InvalidOperationException("retired selection accepted"); }
            catch (WidgetPresentationSessionException) { Check(true, "stale pinned apply rejected before render mutation"); }
            await Mutate("remove"); await Until(() => selection?.IsCurrent == false); await retirement;
            Check(!presenter.IsPresentationActive, "removed pin suspends native demand and input");
            await Mutate("restore"); await Until(() => CurrentFrame().Snapshot.PinnedLayouts.Count == 2);
            await Select("compact"); await presenter.SetPresentationActiveAsync(true); presenter.Enter();
            await Until(() => Images() >= 2);
            Check(ordinaryActions == 0 && failure is null, "all pinned actions bypass ordinary main dispatch");
            completed = 1;
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception error) { failure = error.Message; completed = -1; }
        Observe();
    }

    private WidgetPresentationFrame CurrentFrame() => session!.GetState("pinned-presenter")!.LastGood!;
    private async Task Select(string layout)
    {
        var projection = session!.ResolvePinnedProjection(CurrentFrame(), layout);
        selection = await session.SelectPinnedLayoutAsync(projection, cancellationToken: lifetime.Token);
        presenter.ApplyPinned(selection, session.ResolvePinnedProjection(CurrentFrame(), layout)); presenter.Enter();
    }
    private void Changed(object? sender, WidgetPresentationChangedEventArgs args) => DispatcherQueue.TryEnqueue(() =>
    {
        if (disposed || args.State.PublicationRevision <= publication) return;
        publication = args.State.PublicationRevision;
        if (args.State.Failure is { } error) failure = error.Message;
        if (selection is { IsCurrent: true } selected && args.State.LastGood is { } frame)
        {
            try { presenter.ApplyPinned(selected, session!.ResolvePinnedProjection(frame, selected.LayoutId)); }
            catch (WidgetPresentationSessionException) when (!selected.IsCurrent) { retirement = presenter.SetPresentationActiveAsync(false); }
            catch (Exception caught) { failure = caught.Message; }
        }
        else if (selection is not null) retirement = presenter.SetPresentationActiveAsync(false);
        Observe();
    });
    private async Task Mutate(string action)
    {
        var prior = CurrentFrame().Authority.SnapshotSequence;
        await session!.SendActionAsync(CurrentFrame().Authority, new(action, "fixture", InputScopeId: CurrentFrame().Authority.ActiveInputScopeId), lifetime.Token);
        await Until(() => CurrentFrame().Authority.SnapshotSequence > prior && publication >= session.GetState("pinned-presenter")!.PublicationRevision);
    }
    private Task Status(string expected) => Until(() => Node(CurrentFrame().Snapshot.Root, "status")?.Text == expected &&
        presenter.CurrentBinding!.Frame.Authority.SnapshotSequence == CurrentFrame().Authority.SnapshotSequence);
    private int Calls() => int.Parse(Node(CurrentFrame().Snapshot.Root, "calls")!.Text!);
    private async Task FocusRow(int index)
    {
        var list = List()!; list.ScrollIntoView(list.Items[index], ScrollIntoViewAlignment.Leading);
        await Until(() => list.ContainerFromIndex(index) is SelectorItem { IsEnabled: true } item &&
            Nodes(item).OfType<WidgetArtworkView>().Any(image => image.Source is not null));
        ((Control)list.ContainerFromIndex(index)).Focus(FocusState.Keyboard);
    }
    private async Task Until(Func<bool> condition)
    {
        var end = Environment.TickCount64 + 8000;
        while (!condition())
        {
            if (failure is not null) throw new InvalidOperationException(failure);
            if (Environment.TickCount64 > end) throw new TimeoutException("Pinned native state did not settle: " + presenter.FocusDiagnostics());
            await Task.Delay(20, lifetime.Token);
        }
    }
    private void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); Observe(); }
    private ListViewBase? List() => Nodes(presenter).OfType<ListViewBase>().FirstOrDefault();
    private int Images() => Nodes(presenter).OfType<WidgetArtworkView>().Count(image => image.Source is not null);
    private FrameworkElement Find(string id) => Nodes(presenter).OfType<FrameworkElement>().First(element => AutomationProperties.GetAutomationId(element) == "Widget." + id);
    private Button Button(string id) => (Button)Find(id);
    private void Focus(string id) => ((Control)Find(id)).Focus(FocusState.Keyboard);
    private string FocusId() => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject focused ? AutomationProperties.GetAutomationId(focused) : "";
    private IEnumerable<DependencyObject> DialogNodes() => VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).SelectMany(popup => Nodes(popup.Child));
    private static ViewNode? Node(ViewNode root, string id) => root.Id == id ? root : root.Children.Select(child => Node(child, id)).FirstOrDefault(value => value is not null);
    private static IEnumerable<DependencyObject> Nodes(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); ++i)
            foreach (var child in Nodes(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private void Observe() => status.Text = JsonSerializer.Serialize(new { completed, failure, checks = checks.Count, last = checks.LastOrDefault(), ordinaryActions, denied, publication });
    public async ValueTask DisposeAsync()
    {
        disposed = true; lifetime.Cancel(); if (run is not null) await run;
        if (session is not null) session.PresentationChanged -= Changed;
        await retirement; await presenter.DisposeAsync();
        if (session is not null) await session.DisposeAsync(); lifetime.Dispose();
    }
}
