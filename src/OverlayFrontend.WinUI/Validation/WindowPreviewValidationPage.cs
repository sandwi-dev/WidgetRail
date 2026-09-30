using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Previews;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;
using PresentationSession = WidgetRail.WidgetPresentationSession.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Explicit fixture: only creates/captures windows owned by this process. No widget/provider launch.</summary>
internal sealed partial class WindowPreviewValidationPage : Page, IAsyncDisposable
{
    private readonly TextBlock status = new() { Text = "Starting owned GPU capture", TextWrapping = TextWrapping.Wrap };
    private readonly Grid canvas = new() { Width = 380, Height = 220, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 106, 21, 106)) };
    private readonly StackPanel probes = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly NamedPipeServerStream pipe;
    private readonly string pipeName = "wrail-preview-fixture-" + Guid.NewGuid().ToString("N");
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<string> checks = [];
    private readonly Dictionary<string, WidgetHostWindowTarget> targets = new(StringComparer.Ordinal);
    private readonly List<WindowPreviewSurface> invalid = [];
    private Window? source;
    private PresentationSession? session;
    private WindowPreviewRenderer? renderer;
    private WindowPreviewCaptureService? captures;
    private WidgetPresentationFrame? fixtureFrame;
    private WindowPreviewSurface? preview;
    private Task? run, serving;
    private Exception? serverFailure;
    private long sequence;
    private bool allowed = true, retired, busy;
    private readonly Border occluder = new() { Width = 110, Height = 70, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 160, 0)),
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Child = new TextBlock { Text = "XAML above", Foreground = new SolidColorBrush(Colors.Black), Margin = new(8) } };
    private static BridgeWidgetDescriptor Descriptor => new() { Id = "preview-fixture", Name = "Owned preview", InstanceId = "preview-fixture.instance",
        RuntimeGeneration = new('a', 32), PresentationGeneration = new('b', 32), PackageContentDigest = new('c', 64), Icon = WidgetGlyph.Play };
    internal WindowPreviewValidationPage()
    {
        pipe = new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        AutomationProperties.SetAutomationId(status, "Preview.Status");
        AutomationProperties.SetAutomationId(canvas, "Preview.Viewport");
        var buttons = new VariableSizedWrapGrid { Orientation = Orientation.Horizontal, MaximumRowsOrColumns = 7, ItemWidth = 105, ItemHeight = 38 };
        foreach (var command in new[] { "Resize", "Transform", "Hide", "Show", "Expire", "Resume", "Deny", "Allow", "Exclude", "Restore", "Device", "Unload", "Reload", "Budget", "Collapse", "Expand", "Viewport", "Presenter", "PresenterUpdate", "PresenterReplace", "PresenterRemove", "PresenterRestore" })
        {
            var button = new Button { Content = command, Margin = new(2) };
            AutomationProperties.SetAutomationId(button, "Preview." + command);
            button.Click += async (_, _) => await CommandAsync(command);
            buttons.Children.Add(button);
        }
        Content = new StackPanel { Spacing = 8, Children = { status, canvas, probes, buttons } };
        canvas.Clip = new RectangleGeometry { Rect = new(0, 0, 380, 220) };
        Loaded += (_, _) => run ??= StartAsync();
    }
    private async Task StartAsync()
    {
        try
        {
            // Physical pixels: leave room for all fixture controls at 125%
            // rasterization scale as well as the clipped preview.
            App.Window.AppWindow.ResizeClient(new(1100, 920));
            App.Window.AppWindow.Move(new(0, 0));
            Check(Marshal.SizeOf<NativePreviewTarget>() == 544 && Marshal.SizeOf<NativePreviewStats>() == 64, "exact native ABI sizes");
            source = new Window { Title = "WidgetRail owned preview source", ExtendsContentIntoTitleBar = true,
                Content = new Grid { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 208, 96)) } };
            if (source.AppWindow.Presenter is OverlappedPresenter frame) frame.SetBorderAndTitleBar(false, false);
            source.AppWindow.ResizeClient(new(480, 300)); source.AppWindow.Move(new(1200, 100)); source.Activate();
            App.Window.Activate();
            var native = NativeIdentity(WinRT.Interop.WindowNative.GetWindowHandle(source));
            targets.Add("owned.preview", native.ToTarget("owned.preview"));
            targets.Add("wrong.pid", native.ToTarget("wrong.pid") with { ProcessId = native.ProcessId + 1 });
            targets.Add("wrong.created", native.ToTarget("wrong.created") with { ProcessCreated = native.ProcessCreated + 1 });
            targets.Add("wrong.class", native.ToTarget("wrong.class") with { ClassName = "wrong.window.class" });
            targets.Add("wrong.host", native.ToTarget("wrong.host") with { Handle = (ulong)App.WindowHandle });
            serving = ServeAsync();
            session = await PresentationSession.ConnectAsync(pipeName, new() { WindowPreviews = true });
            await session.ListWidgetsAsync();
            var establishment = session.EstablishPresentationAsync(session.GetTarget(Descriptor.Id), WidgetLifecycleState.Interactive);
            if (await Task.WhenAny(establishment, serving) == serving) throw serverFailure ?? new InvalidOperationException("Fixture server stopped.");
            var displayed = await establishment.WaitAsync(TimeSpan.FromSeconds(10));
            fixtureFrame = displayed;
            captures = new();
            renderer = captures.CreateRenderer(session, (ulong)App.WindowHandle);
            if (Environment.GetCommandLineArgs().Contains("--validate-preview-resume"))
            { await ValidatePresenterResumeAsync(); return; }
            renderer.Apply(displayed);
            renderer.Failed += error => serverFailure = error;
            preview = renderer.CreateSurface("owned.preview", ImageFit.Cover);
            preview.Width = 360; preview.Height = 200; preview.HorizontalAlignment = HorizontalAlignment.Left; preview.VerticalAlignment = VerticalAlignment.Top;
            preview.Margin = new(40, 30, 0, 0); canvas.Children.Add(preview); canvas.Children.Add(occluder);
            foreach (var id in targets.Keys.Where(id => id != "owned.preview"))
            {
                var bad = renderer.CreateSurface(id, ImageFit.Contain); bad.Width = 30; bad.Height = 30;
                probes.Children.Add(bad); invalid.Add(bad);
            }
            await Until(() => preview.InspectNative().State == 2 && preview.InspectNative().Frames > 0);
            await Until(() => invalid.All(surface => surface.InspectNative().Error < 0));
            Check(invalid.All(surface => surface.InspectNative().Error < 0), "PID creation class and self-window mismatches rejected before capture");
            Check(preview.InspectNative().ActiveCount == 1, "only the exact owned target is captured");
            Check(preview.InspectNative().TotalBytes <= 192UL * 1024 * 1024, "aggregate GPU storage stays bounded");
            Update("Live");
        }
        catch (Exception error) { Fail(error); }
    }
    private async Task CommandAsync(string command)
    {
        if (busy || retired || renderer is null || preview is null || source is null) return;
        busy = true;
        try
        {
            var before = preview.InspectNative();
            if (command.StartsWith("Presenter", StringComparison.Ordinal))
            {
                await PresenterCommandAsync(command);
                Check(true, command + " completed"); Update(command); return;
            }
            switch (command)
            {
                case "Resize":
                    source.AppWindow.ResizeClient(new(640, 240));
                    // WGC ContentSize includes the source's native frame. It is
                    // deliberately not equated to the requested client size.
                    await Until(() => preview.InspectNative().SourceWidth > before.SourceWidth &&
                        preview.InspectNative().SourceHeight < before.SourceHeight && preview.InspectNative().State == 2);
                    break;
                case "Transform": preview.RenderTransform = new CompositeTransform { ScaleX = 1.15, ScaleY = 1.15, TranslateX = 18 }; await Task.Delay(300); break;
                case "Viewport":
                    preview.Width = 310; preview.Height = 170;
                    await Until(() => preview.InspectNative().State == 2 &&
                        preview.InspectNative().Width == (uint)Math.Ceiling(310 * XamlRoot.RasterizationScale) &&
                        preview.InspectNative().Height == (uint)Math.Ceiling(170 * XamlRoot.RasterizationScale));
                    break;
                case "Hide": renderer.SetVisible(false); await Until(() => preview.InspectNative().ActiveCount == 0); await CheckIdleAsync("hidden"); break;
                case "Show": renderer.SetVisible(true); await Until(() => preview.InspectNative().State == 2); break;
                case "Collapse": preview.Visibility = Visibility.Collapsed; await Until(() => preview.InspectNative().ActiveCount == 0); await CheckIdleAsync("collapsed"); break;
                case "Expand": preview.Visibility = Visibility.Visible; await Until(() => preview.InspectNative().State == 2); break;
                case "Expire":
                    renderer.PauseRenewalForValidation(true);
                    Update("Expiring");
                    await Task.Delay(100);
                    // Deliberately freeze BOTH UI/source dispatchers. Native expiry
                    // must clear presented pixels even without layout or new source work.
                    Thread.Sleep(2800);
                    Check(preview.InspectNative().ActiveCount == 0 && preview.InspectNative().State == 3, "expiry retires capture while the UI dispatcher is blocked");
                    break;
                case "Resume": renderer.PauseRenewalForValidation(false); await Until(() => preview.InspectNative().State == 2); break;
                case "Deny": allowed = false; await Until(() => preview.InspectNative().ActiveCount == 0); break;
                case "Allow": allowed = true; await Until(() => preview.InspectNative().State == 2); break;
                case "Exclude":
                    Check(SetWindowDisplayAffinity(WinRT.Interop.WindowNative.GetWindowHandle(source), 0x11) != 0, "owned source accepts capture exclusion");
                    await Until(() => preview.InspectNative().ActiveCount == 0); break;
                case "Restore":
                    Check(SetWindowDisplayAffinity(WinRT.Interop.WindowNative.GetWindowHandle(source), 0) != 0, "owned source exclusion restored");
                    await Until(() => preview.InspectNative().State == 2); break;
                case "Device": renderer.ResetDeviceForValidation(); await Until(() => preview.LastStats.SurfaceGeneration > before.SurfaceGeneration && preview.InspectNative().State == 2); break;
                case "Unload": canvas.Children.Remove(preview); await Until(() => preview.InspectNative().ActiveCount == 0 && preview.InspectNative().TotalBytes == 0); await CheckIdleAsync("unloaded"); break;
                case "Reload": canvas.Children.Insert(0, preview); await Until(() => preview.InspectNative().State == 2); break;
                case "Budget":
                    // Two renderer instances share one native budget. This fixture
                    // shares its one fake session, so issue one explicit grant to
                    // both while their automatic renewal is paused.
                    renderer.PauseRenewalForValidation(true);
                    await Task.Delay(450);
                    var other = captures!.CreateRenderer(session!, (ulong)App.WindowHandle);
                    other.PauseRenewalForValidation(true); other.Apply(fixtureFrame!);
                    var additional = Enumerable.Range(0, 9).Select(_ => other.CreateSurface("owned.preview", ImageFit.Contain)).ToArray();
                    try
                    {
                        foreach (var extra in additional) { extra.Width = 24; extra.Height = 30; probes.Children.Add(extra); }
                        await Task.Delay(100);
                        var started = Stopwatch.GetTimestamp();
                        var grant = await session!.RefreshWindowPreviewPermissionsAsync(fixtureFrame!);
                        preview.ApplyGrant(grant, started + 2 * Stopwatch.Frequency);
                        foreach (var extra in additional) extra.ApplyGrant(grant, started + 2 * Stopwatch.Frequency);
                        await Until(() => preview.InspectNative().ActiveCount == 8);
                        Check(preview.InspectNative().TotalBytes <= 192UL * 1024 * 1024, "eight-source GPU budget shared across renderer instances");
                        await Until(() => additional.Any(extra => extra.InspectNative().Error < 0));
                        Check(additional.Any(extra => extra.InspectNative().Error < 0), "excess sources remain harmless placeholders");
                    }
                    finally
                    {
                        foreach (var extra in additional) { probes.Children.Remove(extra); other.RemoveSurface(extra); }
                        await other.DisposeAsync(); renderer.PauseRenewalForValidation(false);
                    }
                    await Until(() => preview.InspectNative().ActiveCount == 1); break;
            }
            Check(true, command + " completed"); Update(command);
        }
        catch (Exception error) { Fail(error); }
        finally { busy = false; }
    }
    private void Update(string stage)
    {
        status.Text = "Preview " + stage;
        var snapshot = new { passed = true, stage, checks, stats = preview?.InspectNative(), scale = XamlRoot?.RasterizationScale,
            clip = canvas.TransformToVisual(App.Window.Content).TransformBounds(new(0, 0, canvas.ActualWidth, canvas.ActualHeight)) };
        AutomationProperties.SetHelpText(status, JsonSerializer.Serialize(snapshot, JsonOptions));
        Write(snapshot);
    }
    private void Fail(Exception error)
    { status.Text = "Preview failed: " + error.Message; Write(new { passed = false, checks, error = error.ToString() }); }
    private void Check(bool passed, string name)
    { if (!passed) throw new InvalidOperationException(name); checks.Add(name); }
    private async Task CheckIdleAsync(string state)
    {
        await Task.Delay(100);
        var inspections = preview!.InspectionCount;
        await Task.Delay(300);
        Check(!preview.IsBindingTimerRunning && inspections == preview.InspectionCount,
            state + " preview stops UI timer and native inspection polling");
    }
    private async Task Until(Func<bool> condition)
    {
        for (int count = 0; count < 500; ++count)
        {
            lifetime.Token.ThrowIfCancellationRequested(); if (serverFailure is not null) throw serverFailure;
            if (condition()) return; await Task.Delay(20, lifetime.Token);
        }
        throw new TimeoutException("Preview condition did not settle: " + JsonSerializer.Serialize(preview?.InspectNative(), JsonOptions));
    }
    private static unsafe NativePreviewTarget NativeIdentity(nint hwnd)
    {
        var target = new NativePreviewTarget { Size = (uint)sizeof(NativePreviewTarget), Version = 1 };
        Marshal.ThrowExceptionForHR(PreviewNative.ReadIdentity((ulong)hwnd, ref target)); return target;
    }
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int SetWindowDisplayAffinity(nint hwnd, uint affinity);

    private async Task ServeAsync()
    {
        try
        {
            await pipe.WaitForConnectionAsync(lifetime.Token);
            while (!lifetime.IsCancellationRequested)
            {
                var request = await ReadAsync(); object body = new { }; string type = "acknowledged";
                switch (request.Type)
                {
                    case "hello": type = "hello-accepted"; break;
                    case "list-widgets": type = "widgets"; body = new { revision = 1, isComplete = true, widgets = new[] { Descriptor } }; break;
                    case "window-preview-permissions": type = "window-preview-permissions"; body = new { allowedWindowIds = allowed ? targets.Keys.ToArray() : [] }; break;
                    case "set-widget-lifecycle":
                        type = "snapshot";
                        var snapshot = new ViewSnapshot { Sequence = ++sequence, WidgetInstanceId = Descriptor.InstanceId, ActiveInputScopeId = "root",
                            Root = presenterRoot ?? new() { Id = "root", Kind = ViewNodeKind.Stack, Children = targets.Keys.Select(id => new ViewNode { Id = id, Kind = ViewNodeKind.WindowPreview,
                                WindowId = id, PreviewAspectRatio = 1.6, ImageFit = ImageFit.Cover, AccessibilityLabel = "Owned fixture window" }).ToArray() } };
                        using (var serialized = JsonDocument.Parse(SnapshotJson.Serialize(snapshot)))
                            body = new { widgetId = Descriptor.Id, transactionKind = "ordinaryCheckpoint", baseSequence = 0, recoveryOriginSequence = 0,
                                snapshot = serialized.RootElement.Clone(), renderStyles = PreviewFixtureStyles(snapshot),
                                windowPreviews = targets.Where(pair => snapshot.Root.Children.Any(node => node.WindowId == pair.Key)).ToDictionary(pair => pair.Key, pair => new { handle = pair.Value.Handle.ToString("x", CultureInfo.InvariantCulture),
                                    processId = pair.Value.ProcessId, processCreated = pair.Value.ProcessCreated.ToString("x", CultureInfo.InvariantCulture), className = pair.Value.ClassName }) };
                        break;
                    case "stop": await ReplyAsync(request.RequestId, type, body); return;
                    default: throw new InvalidOperationException("Unexpected preview fixture request " + request.Type);
                }
                await ReplyAsync(request.RequestId, type, body);
            }
        }
        catch (OperationCanceledException) when (retired) { }
        catch (Exception error) { serverFailure = error; }
    }
    private sealed record Envelope(string Type, long RequestId, JsonElement Payload);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, IncludeFields = true, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    private async Task<Envelope> ReadAsync()
    {
        var prefix = new byte[4]; await pipe.ReadExactlyAsync(prefix, lifetime.Token);
        var count = BinaryPrimitives.ReadInt32LittleEndian(prefix); if (count is <= 0 or > 1048576) throw new InvalidDataException("Fixture request bound exceeded.");
        var bytes = new byte[count]; await pipe.ReadExactlyAsync(bytes, lifetime.Token);
        return JsonSerializer.Deserialize<Envelope>(bytes, JsonOptions) ?? throw new InvalidDataException("Missing fixture request.");
    }
    private async Task ReplyAsync(long id, string type, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { protocolVersion = BridgeProtocol.CurrentVersion, type, requestId = id, payload }, JsonOptions);
        var prefix = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(prefix, bytes.Length);
        await pipe.WriteAsync(prefix, lifetime.Token); await pipe.WriteAsync(bytes, lifetime.Token); await pipe.FlushAsync(lifetime.Token);
    }
    private static void Write<T>(T value)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
        Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "window-preview-result.json"), JsonSerializer.Serialize(value, JsonOptions));
    }
    public async ValueTask DisposeAsync()
    {
        if (retired) return; retired = true;
        if (widgetPresenter is not null) await widgetPresenter.DisposeAsync();
        if (renderer is not null) await renderer.DisposeAsync();
        if (captures is not null) await captures.DisposeAsync();
        if (session is not null) await session.DisposeAsync();
        lifetime.Cancel();
        if (run is not null) await run;
        if (serving is not null) await serving;
        await pipe.DisposeAsync(); source?.Close(); lifetime.Dispose();
    }
}
