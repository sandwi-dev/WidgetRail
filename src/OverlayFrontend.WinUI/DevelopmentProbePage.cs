using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI;

/// <summary>Hidden CLI admission probe. No visible windows or input registrations.</summary>
internal sealed partial class DevelopmentProbePage(OverlayShellOptions options) : Page, IAsyncDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private OwnedBridgeProcess? owner;
    private Task? startup;
    private Task? disposal;
    internal event Action? Failed;

    internal void Start() => startup ??= InitializeAsync();

    private async Task InitializeAsync()
    {
        try
        {
            var development = options.Development ?? throw new InvalidOperationException("Development launch is missing.");
            owner = await OwnedBridgeProcess.StartAsync(new(options.InstallationRoot,
                options.SettingsRoot, options.InstalledCatalogRoot)
                { ProcessOwnerJobName = development.JobName }, lifetime.Token);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            WidgetPresentationCatalog catalog;
            do
            {
                catalog = await owner.Session.ListWidgetsAsync(deadline.Token);
                if (catalog.IsComplete) break;
                await Task.Delay(50, deadline.Token);
            } while (true);
            var descriptor = catalog.Widgets.SingleOrDefault(widget => widget.Id == development.WidgetId &&
                widget.InstanceId == development.InstanceId)
                ?? throw new InvalidDataException("The development catalog does not contain the requested widget instance.");
            var frame = await owner.Session.EstablishPresentationAsync(owner.Session.GetTarget(descriptor.Id),
                WidgetLifecycleState.Visible, deadline.Token);
            var errors = WinUiPresentationContract.Validate(frame.Snapshot);
            if (frame.Descriptor.InstanceId != development.InstanceId || errors.Count != 0)
                throw new InvalidDataException("The development widget did not publish an admissible WinUI snapshot.");
            deadline.Token.ThrowIfCancellationRequested();
            development.PublishReady(inspectorReady: false);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            Diagnostics.FrontendFailureLog.Current.Write("development-probe", error);
            DispatcherQueue.TryEnqueue(() => Failed?.Invoke());
        }
    }

    public ValueTask DisposeAsync() => new(disposal ??= DisposeCoreAsync());
    private async Task DisposeCoreAsync()
    {
        lifetime.Cancel();
        if (startup is not null) await startup;
        if (owner is not null) await owner.DisposeAsync();
        lifetime.Dispose();
    }
}
