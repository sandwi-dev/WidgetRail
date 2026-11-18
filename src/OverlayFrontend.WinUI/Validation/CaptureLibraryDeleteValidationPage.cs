using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.FirstPartyWidgets.CaptureLibrary;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.PlatformBroker;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using WidgetRail.WidgetStyling;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Real widget/renderer deletion with native file leases; only synthetic fixture copies are recycled.</summary>
internal sealed partial class CaptureLibraryDeleteValidationPage : Page
{
    private readonly Grid host = new();
    private readonly List<object> checks = [];
    private bool started;
    internal CaptureLibraryDeleteValidationPage(string output, string video, string style)
    {
        Content = host; PresentationLayers.Attach(host);
        Loaded += async (_, _) =>
        {
            if (started) return; started = true;
            Exception? failure = null;
            try { await Run(false); await Run(true); await Run(false, true); }
            catch (Exception error) { failure = error; }
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { passed = failure is null, checks, error = failure?.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
        };
        async Task Run(bool holdScene, bool retainForeignLease = false)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60)); var token = deadline.Token;
            var directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, retainForeignLease ? "foreign-lease" : holdScene ? "delayed" : "normal", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var identity = new BrokerWidgetIdentity("widgetrail.tests.delete", "widgetrail.tests", "native"); var owner = new object();
            using var registry = new WindowCaptureRegistry();
            using var library = new CaptureLibraryStore(Path.Combine(directory, "metadata"), Path.Combine(directory, "owned"),
                WindowsCaptureImageCodec.CreateContextAsync, WindowsCaptureImageCodec.CreateThumbnailAsync, WindowsCaptureImageCodec.InspectAsync, WindowsFileOperations.RecycleAsync);
            var facts = await WindowsCaptureImageCodec.InspectAsync(Path.GetFullPath(video),
                new(Guid.NewGuid().ToString("N"), "video/mp4", 1, 1, new FileInfo(video).Length, 1, long.MaxValue) { Origin = CaptureAttachmentOrigin.SavedLibrary }, token);
            var saved = await library.SaveAsync(new(facts, Path.GetFullPath(video)), identity, token);
            var originalHash = System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(video, token));
            var presenter = new WidgetViewPresenter { Width = 1180, Height = 700 }; host.Children.Add(presenter);
            var resolver = new ThemeCatalog(new PlatformSettingsPaths(directory));
            var document = WrssParser.Parse(await File.ReadAllTextAsync(style, token), style).Document;
            var theme = ThemeLayerCompiler.Compile(resolver.BuiltInDefault.Package, new((WrssDocument[])[document], []), resolver.Load(ThemeIdentity.BuiltInMonoInk, "1.0.0").Package).Theme;
            var foreignOwner = new object(); var foreignIdentity = identity with { InstanceId = "other" }; CaptureAttachment? foreignAttachment = null;
            var releaseSeen = false; var recycleSeen = false; var hold = false; var originalPath = ""; var beforeRecyclePlayers = -1; string? recycleError = null;
            var recycleWatch = new Stopwatch(); var recycleDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var builder = new WidgetTestHostServicesBuilder().WithPrivateState(new())
                .WithEvents(WidgetCaptureLibraryCapabilities.Changed, Array.Empty<CaptureLibraryChanged>())
                .WithResponse(WidgetPermissionCapabilities.Request, new WidgetPermissionRequestState(WidgetPermissionOutcome.Granted))
                .WithResponse(WidgetCaptureCapabilities.Capabilities, CaptureCapabilities.Unavailable)
                .WithHandler(WidgetCaptureLibraryCapabilities.Query, async (request, ct) => await library.QueryAsync(request, ct))
                .WithHandler(WidgetCaptureLibraryCapabilities.Applications, async (request, ct) => await library.ApplicationsAsync(request, ct))
                .WithHandler(WidgetCaptureLibraryCapabilities.Groups, async (request, ct) => await library.GroupsAsync(request, ct))
                .WithHandler(WidgetCaptureLibraryCapabilities.Find, async (request, ct) => await library.FindAsync(request, ct))
                .WithHandler(WidgetCaptureLibraryCapabilities.GetMetadata, async (request, ct) => await library.GetMetadataAsync(request.Id, ct))
                .WithHandler(WidgetCaptureLibraryCapabilities.Open, async (request, ct) =>
                {
                    var lease = await library.OpenAsync(request.Id, request.Resolution, ct);
                    try { var result = registry.AdoptLibraryMedia(owner, identity, lease, ct); originalPath = registry.Resolve(identity, result.Id).Path; return result; }
                    catch { await lease.DisposeAsync(); throw; }
                })
                .WithHandler(WidgetCaptureLibraryCapabilities.Release, (request, _) =>
                { registry.Discard(owner, request.AttachmentId, PlatformCapabilities.CaptureLibraryReadV1); releaseSeen = true; return ValueTask.FromResult(new WidgetCapabilityAcknowledgement(true)); })
                .WithHandler(WidgetCaptureLibraryCapabilities.Recycle, async (request, ct) =>
                {
                    recycleSeen = true; beforeRecyclePlayers = MediaPlayerView.ResidentPlayers;
                    if (!releaseSeen) throw new InvalidOperationException("Widget recycled before releasing its broker lease.");
                    recycleWatch.Start();
                    try { var result = await library.RecycleAsync(request, ct); recycleError = result.Results.Single().ErrorCode; return result; }
                    finally { recycleWatch.Stop(); recycleDone.TrySetResult(); }
                });
            var widget = WidgetTestHost.Attach(new CaptureLibraryWidget(), builder.Build());
            using var indexed = WidgetTestHost.CreateIndexedCollectionHost(widget, "capture-delete-native", 100);
            var disposed = false;
            presenter.ValidationMediaResolver = (definition, ct) =>
            {
                ct.ThrowIfCancellationRequested(); var current = registry.Resolve(identity, definition.Source.Attachment!.Id);
                return Task.FromResult(new HostMediaPlayerSource(definition, new Uri(current.Path).AbsoluteUri, false,
                    current.Attachment.Width, current.Attachment.Height, current.Attachment.DurationSeconds,
                    current.LeaseExpiresAtUnixMilliseconds ?? current.Attachment.ExpiresAtUnixMilliseconds, RequiresRevalidation: true));
            };
            presenter.DispatchActionAsync = request => widget.OnActionAsync(request.Action, token).AsTask();
            widget.Invalidated += (_, _) => DispatcherQueue.TryEnqueue(() => { if (!disposed && !hold) Publish(); });
            try
            {
                await WidgetTestHost.InitializeAsync(widget); await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);
                await Until(() => Walk(indexed.PublishSnapshot().Root).Any(n => n.IndexedCollection?.Count == 1) && !Busy());
                using (var rows = await indexed.AcquireAsync("capture-library.entries", 0, 1, cancellationToken: token))
                    if (rows.RouteAction(saved.Id, ControllerButton.A) is null) throw new InvalidOperationException("Production gallery did not route opening.");
                await Until(() => Node("capture-library.preview") is not null && !Busy()); Publish();
                await Until(() => Descendants(presenter).OfType<MediaPlayerView>().FirstOrDefault()?.CanControl == true);
                var media = Descendants(presenter).OfType<MediaPlayerView>().First(); media.Toggle(); await Until(() => media.IsPlaying);
                if (retainForeignLease)
                    foreignAttachment = registry.AdoptLibraryMedia(foreignOwner, foreignIdentity,
                        await library.OpenAsync(saved.Id, CaptureImageResolution.Original, token), token);
                await widget.OnActionAsync(new("capture-library.delete", "capture-library.delete"), token); Publish();
                await Until(() => Node("capture-library.delete.confirm") is not null && Descendants(host).OfType<Button>().Any(b => b.Content?.ToString() == "Remove" || Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(b) == "Widget.capture-library.delete.confirm"));
                hold = holdScene;
                await widget.OnActionAsync(new("capture-library.delete.confirm", "capture-library.delete.confirm"), token);
                await recycleDone.Task.WaitAsync(token);
                checks.Add(new { phase = retainForeignLease ? "other-widget-lease" : holdScene ? "delayed-scene" : "normal-scene", releaseSeen, recycleSeen, beforeRecyclePlayers, recycleError, milliseconds = recycleWatch.ElapsedMilliseconds,
                    originalExists = File.Exists(originalPath), rendererPlayers = MediaPlayerView.ResidentPlayers });
                hold = false; Publish();
                if (recycleError is not null)
                {
                    if (!File.Exists(originalPath) || !System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(originalPath, token)).SequenceEqual(originalHash))
                        throw new InvalidOperationException("Failed recycling changed the original bytes.");
                    checks.Add(new { phase = "failed-recycle", originalPreserved = true });
                    if (!retainForeignLease) throw new InvalidOperationException("Native recycle failed with " + recycleError + " while " + beforeRecyclePlayers + " decoder(s) remained; delayed=" + holdScene);
                    if (registry.Resolve(foreignIdentity, foreignAttachment!.Id).Path != originalPath)
                        throw new InvalidOperationException("Deleting widget retired another widget's read lease.");
                    await Until(() => !Busy() && Node("capture-library.preview") is not null);
                    checks.Add(new { phase = "failed-recycle-recovery", previewReopened = true, otherWidgetStillAuthorized = true });
                    registry.Discard(foreignOwner, foreignAttachment.Id, PlatformCapabilities.CaptureLibraryReadV1);
                    return;
                }
                if (retainForeignLease) throw new InvalidOperationException("Recycling ignored another widget's active read-only original lease.");
                if (File.Exists(originalPath)) throw new InvalidOperationException("Successful recycling left its original in place.");
                await Until(() => !Busy() && Node("capture-library.empty") is not null);
            }
            finally { disposed = true; await WidgetTestHost.DestroyAsync(widget); await presenter.DisposeAsync(); host.Children.Remove(presenter); }
            bool Busy() => Walk(indexed.PublishSnapshot().Root).Any(n => n.IsBusy == true);
            ViewNode? Node(string id) => Walk(indexed.PublishSnapshot().Root).FirstOrDefault(n => n.Id == id);
            void Publish()
            {
                var snapshot = indexed.PublishSnapshot();
                // This probe starts at the production preview; collection realization is covered by its native gallery fixture.
                if (Walk(snapshot.Root).Any(n => n.Kind == ViewNodeKind.IndexedCollection)) return;
                var descriptor = new BridgeWidgetDescriptor { Id = "capture-library", Name = "Capture Library deletion", InstanceId = snapshot.WidgetInstanceId,
                    RuntimeGeneration = "delete-native", PresentationGeneration = "delete-native", Icon = WidgetGlyph.Fullscreen, PackageContentDigest = "" };
                presenter.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                    snapshot.WidgetInstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId ?? snapshot.Root.Id), descriptor, snapshot, BridgeRenderStyleResolver.Resolve(snapshot, theme)));
                presenter.UpdateLayout();
            }
            async Task Until(Func<bool> condition) { while (!condition()) await Task.Delay(20, token); }
        }
    }
    private static IEnumerable<ViewNode> Walk(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Walk));
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
}
