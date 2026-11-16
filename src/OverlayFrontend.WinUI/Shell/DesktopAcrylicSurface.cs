using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WinRT;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

/// <summary>A bounded desktop material behind ordinary content, never a focus or input owner.</summary>
internal sealed partial class DesktopAcrylicSurface : Grid
{
    private SystemBackdropElement? backdrop;
    internal Border Paint { get; } = new();
    private SurfaceDesktopAcrylic? material;
    private bool requested;
    private double materialOpacity = 1;
    private Color tint;
    internal UIElement Child { get => Paint.Child; set => Paint.Child = value; }
    internal CornerRadius SurfaceCornerRadius => Paint.CornerRadius;
    internal bool HasDesktopAcrylic => backdrop?.SystemBackdrop is not null;
    internal SystemBackdropElement Backdrop => backdrop ?? throw new InvalidOperationException("Desktop material has not been requested.");
    internal double MaterialOpacity
    {
        set { materialOpacity=Math.Clamp(value,0,1); if(backdrop is not null)backdrop.Opacity=materialOpacity; }
    }

    public DesktopAcrylicSurface()
    {
        Children.Add(Paint);
        Loaded += (_, _) => Connect();
        Unloaded += (_, _) => Disconnect();
    }

    internal bool ApplyMaterial(bool enabled, Color color)
    {
        requested = enabled && DesktopAcrylicController.IsSupported();
        tint = color with { A = 255 };
        if (requested)
        {
            if (IsLoaded) Connect();
            ConfigureBackdrop();
            material?.SetTint(tint);
        }
        else Disconnect();
        return requested;
    }

    private void Connect()
    {
        if (!requested || material is not null) return;
        if(backdrop is null)
        {
            backdrop=new(){IsHitTestVisible=false,Opacity=materialOpacity};
            AutomationProperties.SetAccessibilityView(backdrop,AccessibilityView.Raw);
            Children.Insert(0,backdrop);
        }
        ConfigureBackdrop();
        material = new(tint);
        backdrop.Visibility = Visibility.Visible;
        backdrop.SystemBackdrop = material;
    }
    private void Disconnect()
    {
        if(backdrop is not null)
        {
            backdrop.SystemBackdrop = null;
            backdrop.Visibility = Visibility.Collapsed;
        }
        material = null;
    }
    private void ConfigureBackdrop()
    {
        if(backdrop is null)return;
        backdrop.CornerRadius=Paint.CornerRadius;
    }
    internal void Retire(){requested=false;Disconnect();}
}

/// <summary>Owns passive material configuration, independently of native window activation.</summary>
internal sealed partial class SurfaceDesktopAcrylic(Color tint) : SystemBackdrop
{
    private DesktopAcrylicController? controller;
    private SystemBackdropConfiguration? configuration;
    private ICompositionSupportsSystemBackdrop? target;
    private IDisposable? targetLifetime;
    internal void SetTint(Color color)
    {
        tint=color;
        if(controller is null || controller.IsClosed)return;
        controller.TintColor=controller.FallbackColor=color;
        controller.TintOpacity=.55f;
        controller.LuminosityOpacity=.65f;
    }
    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget,XamlRoot root)
    {
        if(controller is not null)throw new InvalidOperationException("A material cannot be shared across surfaces.");
        // Do not initialize the default activation listener. Passive peers never
        // activate; the host supplies theme/accessibility policy and native acrylic
        // still owns system transparency/power fallbacks.
        target=connectedTarget;
        // SystemBackdropElement supplies a ContentExternalBackdropLink. Its COM
        // wrappers can survive the element's native reference; close this retired
        // link on the UI thread instead of leaving thread-affine cleanup to GC.
        try
        {
            targetLifetime=connectedTarget.As<IDisposable>();
            configuration=new(){IsInputActive=true};
            controller=new(){Kind=DesktopAcrylicKind.Thin};
            controller.SetSystemBackdropConfiguration(configuration);
            SetTint(tint);
            controller.AddSystemBackdropTarget(target);
        }
        catch { RetireTarget();throw; }
    }
    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
        => RetireTarget();
    private void RetireTarget()
    {
        var retiredController=controller;var retiredTarget=target;var retiredLifetime=targetLifetime;
        controller=null;configuration=null;target=null;targetLifetime=null;
        try
        {
            if(retiredTarget is not null && retiredController is {IsClosed:false})
                retiredController.RemoveSystemBackdropTarget(retiredTarget);
        }
        finally
        {
            try { retiredController?.Dispose(); }
            finally { retiredLifetime?.Dispose(); }
        }
    }
}

