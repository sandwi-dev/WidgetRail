using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using PresentationSession = WidgetRail.WidgetPresentationSession.WidgetPresentationSession;

namespace WidgetRail.OverlayFrontend.WinUI.Previews;

/// <summary>
/// WinUI only publishes desired state and binds published composition surfaces.
/// All native capture work, including teardown, stays on the background pump.
/// </summary>
internal sealed class WindowPreviewSurface : Grid, IDisposable
{
    private sealed record Published(NativePreviewStats Stats);
    private readonly object gate = new();
    private readonly NativePreviewEngine engine;
    private readonly PresentationSession session;
    private readonly WidgetHostWindowTarget target;
    private readonly ulong hostWindow;
    private readonly ImageFit fit;
    private readonly SpriteVisual visual;
    private readonly CompositionSurfaceBrush brush;
    private ICompositionSurface? boundSurface;
    private readonly DispatcherTimer bindingTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly SemaphoreSlim changed = new(0, 1);
    private readonly Task completion;
    private readonly long visibilityToken;
    private GCHandle callbackRoot;
    private WidgetWindowPreviewGrant? grant;
    private long deadline;
    private uint pixelWidth = 1, pixelHeight = 1;
    private double rasterScale = 1;
    private nint pendingSurface;
    private bool hasPendingSurface;
    private int disposed, demanded, ownerVisible = 1, elementVisible = 1, viewportVisible;
    private int presentationUpdateQueued;
    private long inspectionCount;
    private Published published = new(NativePreviewStats.Empty);
    internal NativePreviewStats LastStats => Volatile.Read(ref published).Stats;
    internal Task Completion => completion;
    internal long InspectionCount => Interlocked.Read(ref inspectionCount);
    internal bool IsBindingTimerRunning => bindingTimer.IsEnabled;
    internal string DemandState => $"loaded={demanded};viewport={viewportVisible};owner={ownerVisible};element={elementVisible};disposed={disposed}";
    internal bool HasNativeCallbackRoot { get { lock (gate) return callbackRoot.IsAllocated; } }
    internal string WindowId => target.WindowId;
    internal bool HasDemand => Volatile.Read(ref demanded) != 0 && Volatile.Read(ref viewportVisible) != 0 &&
        Volatile.Read(ref ownerVisible) != 0 && Volatile.Read(ref elementVisible) != 0 && Volatile.Read(ref disposed) == 0;

    internal WindowPreviewSurface(NativePreviewEngine engine, PresentationSession session, WidgetHostWindowTarget target,
        ulong hostWindow, ImageFit fit)
    {
        this.engine = engine; this.session = session; this.target = target; this.hostWindow = hostWindow; this.fit = fit;
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        visual = compositor.CreateSpriteVisual(); visual.CompositeMode = CompositionCompositeMode.SourceOver;
        brush = compositor.CreateSurfaceBrush(); brush.Stretch = CompositionStretch.Fill;
        visual.Brush = brush;
        ElementCompositionPreview.SetElementChildVisual(this, visual);
        callbackRoot = GCHandle.Alloc(this);
        Loaded += LoadedView; Unloaded += UnloadedView; SizeChanged += SizeChangedView;
        EffectiveViewportChanged += ViewportChanged;
        visibilityToken = RegisterPropertyChangedCallback(VisibilityProperty, (_, _) =>
        {
            Volatile.Write(ref elementVisible, Visibility == Visibility.Visible ? 1 : 0);
            if (elementVisible == 0) Revoke(); RefreshDemandPresentation(); Signal();
        });
        bindingTimer.Tick += (_, _) => { if (XamlRoot?.RasterizationScale != rasterScale) UpdateSize(); BindPublishedSurface(); };
        completion = Task.Run(PumpAsync);
    }
    private void Signal()
    {
        try { changed.Release(); }
        catch (SemaphoreFullException) { } // Coalesced desired state, never replayed.
    }
    private void LoadedView(object sender, RoutedEventArgs args)
    { if (disposed != 0) return; Volatile.Write(ref demanded, 1); RefreshDemandPresentation(); Signal(); }
    private void UnloadedView(object sender, RoutedEventArgs args)
    {
        Volatile.Write(ref demanded, 0); Revoke(); RefreshDemandPresentation(); Signal();
    }
    private void ViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args)
    {
        var rect = args.EffectiveViewport;
        var visible = Math.Min(rect.Right, ActualWidth) > Math.Max(rect.Left, 0) &&
            Math.Min(rect.Bottom, ActualHeight) > Math.Max(rect.Top, 0) && Visibility == Visibility.Visible;
        Volatile.Write(ref viewportVisible, visible ? 1 : 0);
        if (!visible) Revoke(); RefreshDemandPresentation(); Signal();
    }
    internal void SetOwnerVisible(bool visible)
    { Volatile.Write(ref ownerVisible, visible ? 1 : 0); if (!visible) Revoke(); RefreshDemandPresentation(); Signal(); }
    private void RefreshDemandPresentation()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            if (Interlocked.Exchange(ref presentationUpdateQueued, 1) == 0 &&
                !DispatcherQueue.TryEnqueue(() => { Volatile.Write(ref presentationUpdateQueued, 0); RefreshDemandPresentation(); }))
                Volatile.Write(ref presentationUpdateQueued, 0);
            return;
        }
        if (disposed != 0) return;
        if (HasDemand)
        {
            if (!bindingTimer.IsEnabled) { UpdateSize(); bindingTimer.Start(); }
        }
        else
        {
            bindingTimer.Stop();
            BindSurface(0); PublishSurface(0);
        }
    }
    private void SizeChangedView(object sender, SizeChangedEventArgs args) => UpdateSize();
    private void UpdateSize()
    {
        if (disposed != 0) return;
        Clip = new RectangleGeometry { Rect = new(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight)) };
        visual.Size = new Vector2((float)Math.Max(0, ActualWidth), (float)Math.Max(0, ActualHeight));
        rasterScale = XamlRoot?.RasterizationScale ?? 1;
        lock (gate)
        {
            pixelWidth = (uint)Math.Clamp(Math.Ceiling(ActualWidth * rasterScale), 1, 16384);
            pixelHeight = (uint)Math.Clamp(Math.Ceiling(ActualHeight * rasterScale), 1, 16384);
        }
        Signal();
    }
    internal void ApplyGrant(WidgetWindowPreviewGrant next, long nativeDeadline)
    {
        if (!HasDemand || next.Targets.GetValueOrDefault(WindowId) != target || !session.IsWindowPreviewCurrent(next, WindowId))
        { Revoke(); return; }
        Interlocked.Exchange(ref deadline, nativeDeadline);
        Volatile.Write(ref grant, next); Signal();
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Authorized(nint context)
    {
        try
        {
            var surface = (WindowPreviewSurface)GCHandle.FromIntPtr(context).Target!;
            var current = Volatile.Read(ref surface.grant);
            return surface.HasDemand && current is not null && surface.session.IsWindowPreviewCurrent(current, surface.WindowId) ? 1 : 0;
        }
        catch { return 0; }
    }
    private unsafe int AddNative(uint width, uint height, out ulong slot)
    {
        var identity = NativePreviewTarget.From(target);
        return PreviewNative.Add(engine, identity, hostWindow, width, height, (uint)fit,
            (nint)(delegate* unmanaged[Stdcall]<nint, int>)&Authorized, GCHandle.ToIntPtr(callbackRoot), out slot);
    }
    internal void Revoke() { Volatile.Write(ref grant, null); Interlocked.Exchange(ref deadline, 0); Signal(); }
    private async Task PumpAsync()
    {
        ulong slot = 0, generation = 0;
        bool wasLive = false;
        (uint Width, uint Height) previousSize = default;
        long appliedDeadline = 0;
        int error = 0;
        try
        {
            while (Volatile.Read(ref disposed) == 0)
            {
                if (slot == 0 && !HasDemand) await changed.WaitAsync().ConfigureAwait(false);
                else await changed.WaitAsync(TimeSpan.FromMilliseconds(50)).ConfigureAwait(false);
                if (Volatile.Read(ref disposed) != 0) break;
                if (!HasDemand)
                {
                    if (slot != 0 && PreviewNative.Remove(engine, slot) >= 0) { slot = 0; generation = 0; }
                    PublishSurface(0);
                    if (slot == 0)
                    {
                        // No native capture remains. Keep this surface dormant
                        // until a demand/disposal signal; no engine Inspect or UI
                        // timer work is needed for an unrealized presentation.
                        Volatile.Write(ref published, new(NativePreviewStats.Empty));
                        wasLive = false;
                        continue;
                    }
                }
                else
                {
                    (uint Width, uint Height) size;
                    lock (gate) size = (pixelWidth, pixelHeight);
                    var current = Volatile.Read(ref grant);
                    var expires = Interlocked.Read(ref deadline);
                    bool allowed = current is not null && session.IsWindowPreviewCurrent(current, WindowId);
                    if (slot == 0 && allowed)
                    {
                        error = AddNative(size.Width, size.Height, out slot);
                        previousSize = size; appliedDeadline = 0;
                    }
                    if (slot != 0)
                    {
                        if (size != previousSize)
                        {
                            error = PreviewNative.Resize(engine, slot, size.Width, size.Height, (uint)fit);
                            if (error >= 0) previousSize = size;
                        }
                        // Keep the native expiry reason when the existing grant
                        // ages out. Explicit revocation clears the grant itself.
                        var desiredDeadline = current is not null ? expires : 0;
                        if (desiredDeadline != appliedDeadline)
                        { error = PreviewNative.Renew(engine, slot, desiredDeadline); if (error >= 0) appliedDeadline = desiredDeadline; }
                    }
                }
                var stats = NativePreviewStats.Empty;
                Interlocked.Increment(ref inspectionCount);
                var inspected = PreviewNative.Inspect(engine, slot, ref stats);
                if (inspected < 0) stats.Error = inspected;
                else if (slot == 0) stats.Error = error;
                Volatile.Write(ref published, new(stats));
                bool live = slot != 0 && stats.State == 2;
                if (live && (generation != stats.SurfaceGeneration || !wasLive) &&
                    PreviewNative.SwapChain(engine, slot, out var surface, out var nextGeneration) >= 0)
                { PublishSurface(surface); generation = nextGeneration; }
                else if (!live && wasLive) PublishSurface(0);
                wasLive = live;
            }
        }
        catch (Exception exception)
        {
            var stats = LastStats; stats.Error = exception.HResult; stats.State = 6;
            Volatile.Write(ref published, new(stats)); Volatile.Write(ref grant, null);
        }
        finally
        {
            if (slot == 0 || PreviewNative.Remove(engine, slot) >= 0)
            { lock (gate) if (callbackRoot.IsAllocated) callbackRoot.Free(); }
            PublishSurface(0);
        }
    }
    // Owns native's AddRef. At most one unbound swapchain survives a UI stall.
    private void PublishSurface(nint surface)
    {
        nint retired; bool reject;
        lock (gate)
        {
            retired = pendingSurface; reject = disposed != 0;
            pendingSurface = reject ? 0 : surface; hasPendingSurface = !reject;
        }
        if (retired != 0) Marshal.Release(retired);
        if (reject && surface != 0) Marshal.Release(surface);
    }
    private void BindPublishedSurface()
    {
        if (disposed != 0) return;
        nint surface;
        lock (gate)
        {
            if (!hasPendingSurface) return;
            surface = pendingSurface; pendingSurface = 0; hasPendingSurface = false;
        }
        try { BindSurface(HasDemand ? surface : 0); }
        catch (Exception error)
        {
            Revoke();
            var stats = LastStats; stats.Error = error.HResult; stats.State = 6;
            Volatile.Write(ref published, new(stats));
            try { BindSurface(0); } catch { }
        }
        finally { if (surface != 0) Marshal.Release(surface); }
    }
    internal NativePreviewStats InspectNative() => LastStats;
    private unsafe void BindSurface(nint swap)
    {
        var previous = boundSurface;
        if (swap == 0) { brush.Surface = null; boundSurface = null; ReleaseProjection(previous); return; }
        var unknown = WinRT.MarshalInspectable<Compositor>.FromManaged(visual.Compositor);
        nint native = 0, surface = 0;
        try
        {
            // ICompositorSwapChainInterop from Microsoft.UI.Composition.Interop.h.
            // Inherits IUnknown and ICompositorInterop.CreateGraphicsDevice.
            var iid = new Guid("FC084699-67D8-40E1-ADE7-08901D84FFDA");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, iid, out native));
            var call = (delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)(*(nint**)native)[5];
            Marshal.ThrowExceptionForHR(call(native, swap, &surface));
            var next = WinRT.MarshalInterface<ICompositionSurface>.FromAbi(surface);
            try { brush.Surface = next; boundSurface = next; }
            catch { if (!ReferenceEquals(previous, next)) ReleaseProjection(next); throw; }
            if (!ReferenceEquals(previous, next)) ReleaseProjection(previous);
        }
        finally { if (surface != 0) Marshal.Release(surface); if (native != 0) Marshal.Release(native); Marshal.Release(unknown); }
    }
    private static void ReleaseProjection(ICompositionSurface? surface)
    {
        // ICompositionSurface is not IClosable. This projection belongs solely
        // to this binding; release its COM reference now instead of retaining
        // swapchain textures until an unrelated managed GC.
        if (surface is WinRT.IWinRTObject projection) projection.NativeObject.Dispose();
    }
    internal void ReleaseRootAfterOwnerStopped()
    {
        lock (gate)
        {
            if (!engine.IsClosed) throw new InvalidOperationException("Native preview owner is still active.");
            if (callbackRoot.IsAllocated) callbackRoot.Free();
        }
    }
    public void Dispose()
    {
        if (!DispatcherQueue.HasThreadAccess) throw new InvalidOperationException("Dispose preview presentation on its WinUI dispatcher.");
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Volatile.Write(ref demanded, 0); bindingTimer.Stop(); Revoke();
        BindSurface(0); PublishSurface(0);
        Loaded -= LoadedView; Unloaded -= UnloadedView; SizeChanged -= SizeChangedView; EffectiveViewportChanged -= ViewportChanged;
        UnregisterPropertyChangedCallback(VisibilityProperty, visibilityToken);
        ElementCompositionPreview.SetElementChildVisual(this, null);
        visual.Dispose(); brush.Dispose(); Signal();
    }
}
