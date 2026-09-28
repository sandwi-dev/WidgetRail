using System.Text.Json;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>
/// Real bridge/worker round-trip fixture. Deliberately limited to the Clock
/// sample; uses the shared presenter and real asynchronous action path.
/// </summary>
internal sealed class BridgeWidgetValidationPage : Page, IAsyncDisposable
{
    internal sealed record Options(string InstallationRoot, string SettingsRoot,
        string InstalledCatalogRoot, string WidgetId);

    private readonly Options options;
    private readonly CancellationTokenSource lifetime = new();
    private readonly TextBlock status = new() { Text = "Connecting to widget service…", TextWrapping = TextWrapping.Wrap };
    private readonly WidgetViewPresenter surface = new();
    private OwnedBridgeProcess? owner;
    private Task? startup;
    private Task? disposal;
    private bool retired;
    private long publication;
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
        surface.DispatchActionAsync = InvokeAsync;
        Content = new StackPanel { Spacing = 16, Children = { status, surface } };
        Loaded += (_, _) => startup ??= StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            owner = await OwnedBridgeProcess.StartAsync(new(options.InstallationRoot,
                options.SettingsRoot, options.InstalledCatalogRoot), lifetime.Token);
            surface.Session = owner.Session;
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
        surface.Apply(next);
        status.Text = $"Ready: {next.Descriptor.Name}; snapshot {next.Authority.SnapshotSequence}; publications {++applied}; bridge {owner?.ProcessId}";
    }

    private async Task InvokeAsync(WidgetActionRequest request)
    {
        if (retired || owner is null) return;
        try
        {
            await owner.Session.SendActionAsync(request.Authority, request.Action, lifetime.Token);
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { if (!retired) status.Text = $"Action failed: {error.Message}"; }
    }

    public ValueTask DisposeAsync() => new(disposal ??= StopAsync());
    private async Task StopAsync()
    {
        retired = true;
        surface.DispatchActionAsync = null;
        lifetime.Cancel();
        if (startup is not null) await startup;
        if (owner is not null)
        {
            owner.Session.PresentationChanged -= Changed;
        }
        try { await surface.DisposeAsync(); }
        finally
        {
            try { if (owner is not null) await owner.DisposeAsync(); }
            finally { lifetime.Dispose(); }
        }
    }
}
