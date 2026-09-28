using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>
/// Real bridge/worker round-trip fixture. Deliberately limited to the Clock
/// sample's Stack/Row/Text/Button vocabulary; not the production view adapter.
/// </summary>
internal sealed class BridgeWidgetValidationPage : Page, IAsyncDisposable
{
    internal sealed record Options(string InstallationRoot, string SettingsRoot,
        string InstalledCatalogRoot, string WidgetId);

    private readonly Options options;
    private readonly CancellationTokenSource lifetime = new();
    private readonly TextBlock status = new() { Text = "Connecting to widget service…", TextWrapping = TextWrapping.Wrap };
    private readonly ContentControl surface = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly Dictionary<string, FrameworkElement> elements = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ViewNode> nodes = new(StringComparer.Ordinal);
    private OwnedBridgeProcess? owner;
    private WidgetPresentationFrame? frame;
    private Task? startup;
    private Task? disposal;
    private bool retired;
    private long publication;
    private long actionSequence;
    private int applied;

    public BridgeWidgetValidationPage(string configurationPath)
    {
        options = JsonSerializer.Deserialize<Options>(File.ReadAllText(configurationPath))
            ?? throw new InvalidDataException("Widget validation options are missing.");
        // This fixture must never dispatch arbitrary installed-widget actions.
        if (options.WidgetId != "widgetrail.samples.clock")
            throw new InvalidDataException("This validation fixture accepts only the Clock sample.");
        AutomationProperties.SetAutomationId(status, "Bridge.Status");
        AutomationProperties.SetAutomationId(surface, "Bridge.Surface");
        Content = new StackPanel { Spacing = 16, Children = { status, surface } };
        Loaded += (_, _) => startup ??= StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            owner = await OwnedBridgeProcess.StartAsync(new(options.InstallationRoot,
                options.SettingsRoot, options.InstalledCatalogRoot), lifetime.Token);
            owner.Session.PresentationChanged += Changed;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            WidgetPresentationCatalog catalog;
            do
            {
                catalog = await owner.Session.ListWidgetsAsync(deadline.Token);
                if (catalog.IsComplete) break;
                await Task.Delay(50, deadline.Token);
            } while (true);
            await owner.Session.EstablishPresentationAsync(owner.Session.GetTarget(options.WidgetId),
                WidgetLifecycleState.Visible, deadline.Token);
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { if (!retired) status.Text = $"Widget connection failed: {error.Message}"; }
    }

    private void Changed(object? sender, WidgetPresentationChangedEventArgs args)
    {
        var state = args.State;
        if (state.WidgetId != options.WidgetId) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (retired || state.PublicationRevision <= publication) return;
            publication = state.PublicationRevision;
            if (state.Failure is { } failure) { status.Text = $"Widget failed: {failure.Code}"; return; }
            if (state.LastGood is not { } next) return;
            try { Apply(next); }
            catch (Exception error) { surface.IsEnabled = false; status.Text = $"Unsupported fixture view: {error.Message}"; }
        });
    }

    private void Apply(WidgetPresentationFrame next)
    {
        var declared = Flatten(next.Snapshot.Root).ToArray();
        if (declared.Any(node => node.Kind is not (ViewNodeKind.Stack or ViewNodeKind.Row or ViewNodeKind.Text or ViewNodeKind.Button)))
            throw new NotSupportedException("Node kind is outside the bridge fixture vocabulary.");
        if (frame is null)
            surface.Content = Create(next.Snapshot.Root);
        else if (declared.Length != nodes.Count || declared.Any(node => !nodes.TryGetValue(node.Id, out var prior)
            || prior.Kind != node.Kind || !prior.Children.Select(child => child.Id).SequenceEqual(node.Children.Select(child => child.Id))))
            throw new NotSupportedException("Structural updates require the production view adapter.");
        foreach (var node in declared)
        {
            nodes[node.Id] = node;
            var element = elements[node.Id];
            AutomationProperties.SetName(element, node.AccessibilityLabel ?? node.Text ?? node.Id);
            if (element is TextBlock text) text.Text = node.Text ?? string.Empty;
            if (element is Button button) { button.Content = node.Text; button.IsEnabled = node.IsDisabled != true && node.IsBusy != true; }
            if (next.RenderStyles.TryGetValue(node.Id, out var styles)
                && styles.Base.TryGetValue("font-size", out var size) && size.Number is > 0 and <= 512)
            {
                if (element is TextBlock label) label.FontSize = size.Number.Value;
                if (element is Control control) control.FontSize = size.Number.Value;
            }
        }
        var initial = frame is null;
        frame = next;
        status.Text = $"Ready: {next.Descriptor.Name}; snapshot {next.Authority.SnapshotSequence}; publications {++applied}; bridge {owner?.ProcessId}";
        if (initial) DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!retired && next.Snapshot.InitialFocusId is { } id && elements.TryGetValue(id, out var element) && element is Control control)
                control.Focus(FocusState.Keyboard);
        });
    }

    private FrameworkElement Create(ViewNode node)
    {
        FrameworkElement element = node.Kind switch
        {
            ViewNodeKind.Stack or ViewNodeKind.Row => new StackPanel { Spacing = 12,
                Orientation = node.Kind == ViewNodeKind.Row ? Orientation.Horizontal : Orientation.Vertical },
            ViewNodeKind.Text => new TextBlock { TextWrapping = TextWrapping.Wrap },
            ViewNodeKind.Button => new Button { Command = new AsyncRelayCommand(() => InvokeAsync(node.Id)) },
            _ => throw new NotSupportedException(node.Kind.ToString()),
        };
        elements.Add(node.Id, element);
        AutomationProperties.SetAutomationId(element, $"Widget.{node.Id}");
        if (element is StackPanel panel)
            foreach (var child in node.Children) panel.Children.Add(Create(child));
        return element;
    }

    private async Task InvokeAsync(string id)
    {
        if (retired || frame is not { } current || owner is null || !nodes.TryGetValue(id, out var node)
            || node.IsDisabled == true || node.IsBusy == true || node.ActionId is null) return;
        try
        {
            await owner.Session.SendActionAsync(current.Authority, new(node.ActionId, id,
                Sequence: ++actionSequence, MonotonicTimestampMicroseconds: Environment.TickCount64 * 1000,
                InputScopeId: current.Authority.ActiveInputScopeId) { FocusedElementId = id }, lifetime.Token);
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { if (!retired) status.Text = $"Action failed: {error.Message}"; }
    }

    private static IEnumerable<ViewNode> Flatten(ViewNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Flatten(child)) yield return descendant;
    }

    public ValueTask DisposeAsync() => new(disposal ??= StopAsync());
    private async Task StopAsync()
    {
        retired = true;
        lifetime.Cancel();
        if (startup is not null) await startup;
        if (owner is not null)
        {
            owner.Session.PresentationChanged -= Changed;
            await owner.DisposeAsync();
        }
        lifetime.Dispose();
    }
}
