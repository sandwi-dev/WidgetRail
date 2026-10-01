using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

internal sealed partial class WidgetViewPresenter
{
    private readonly Dictionary<Binding, WidgetPackageIconView> buttonIcons = [];
    private readonly HashSet<string> iconDiagnostics = new(StringComparer.Ordinal);
    internal Func<string, CancellationToken, Task<WidgetPresentationPackageIcon>>? ResolvePackageIconAsync { get; set; }
    internal Action<string>? PackageIconDiagnostic { get; set; }
    private string PackageGeneration => frame is null ? string.Empty :
        $"{frame.Authority.WidgetId}:{frame.Authority.RuntimeGeneration}:{frame.Authority.PresentationGeneration}:{frame.Authority.SessionGeneration}:{frame.Descriptor.PackageContentDigest}";
    private Func<string, CancellationToken, Task<WidgetPresentationPackageIcon>>? IconResolver()
    {
        if (ResolvePackageIconAsync is { } resolver) return resolver;
        if (Session is not { } session || frame is not { } current) return null;
        return async (asset, token) =>
        {
            var target = session.GetTarget(current.Authority.WidgetId);
            if (target.Descriptor.RuntimeGeneration != current.Authority.RuntimeGeneration ||
                target.Descriptor.PresentationGeneration != current.Authority.PresentationGeneration ||
                target.Descriptor.InstanceId != current.Authority.WidgetInstanceId ||
                target.Descriptor.PackageContentDigest != current.Descriptor.PackageContentDigest)
                throw new WidgetPresentationSessionException("package_icon_stale", "The icon descriptor is no longer current.");
            try { return await session.ResolvePackageIconAsync(target, asset, token); }
            catch (WidgetPresentationSessionException error) when (error.Code == "catalog_stale")
            {
                var latest = session.GetTarget(current.Authority.WidgetId);
                if (latest.Descriptor.RuntimeGeneration != target.Descriptor.RuntimeGeneration ||
                    latest.Descriptor.PresentationGeneration != target.Descriptor.PresentationGeneration ||
                    latest.Descriptor.InstanceId != target.Descriptor.InstanceId ||
                    latest.Descriptor.PackageContentDigest != target.Descriptor.PackageContentDigest) throw;
                return await session.ResolvePackageIconAsync(latest, asset, token);
            }
        };
    }
    private void IconDiagnostic(string code)
    {
        if (iconDiagnostics.Add(code))
        {
            System.Diagnostics.Trace.TraceWarning("WinUI {0}: semantic icon fallback remains active.", code);
            PackageIconDiagnostic?.Invoke(code);
        }
    }
    private void UpdateNativeIcon(WidgetPackageIconView icon, ViewNode node) => icon.Update(node, IconResolver(), PackageGeneration, IconDiagnostic);
    private void UpdateButtonContent(Binding binding, Button button, ViewNode node)
    {
        if (node.Glyph is null)
        {
            if (buttonIcons.Remove(binding, out var previous)) previous.Dispose();
            UpdateButtonLabel(button, node.Text ?? string.Empty); return;
        }
        if (!buttonIcons.TryGetValue(binding, out var icon))
        {
            icon = new(); buttonIcons.Add(binding, icon);
            var label = new TextBlock();
            Grid.SetColumn(label, 1);
            button.Content = new Grid { ColumnSpacing = 8,
                ColumnDefinitions = { new() { Width = GridLength.Auto }, new() { Width = new(1, GridUnitType.Star) } },
                Children = { icon, label } };
        }
        var panel = (Grid)button.Content;
        var labelPresent = !string.IsNullOrEmpty(node.Text);
        var currentLabel = (TextBlock)panel.Children[1];
        WidgetTextStyleAdapter.SetSource(currentLabel, node.Text ?? string.Empty);
        currentLabel.Visibility = labelPresent ? Visibility.Visible : Visibility.Collapsed;
        panel.ColumnSpacing = labelPresent ? 8 : 0;
        panel.ColumnDefinitions[0].Width = labelPresent ? GridLength.Auto : new(1, GridUnitType.Star);
        panel.ColumnDefinitions[1].Width = labelPresent ? new(1, GridUnitType.Star) : new(0);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        UpdateNativeIcon(icon, node);
    }
    private static void UpdateButtonLabel(Button button, string source)
    {
        if (button.Content is not TextBlock label) button.Content = label = new TextBlock();
        WidgetTextStyleAdapter.SetSource(label, source);
    }
    private WidgetNativePackageIcon? CreateSelectIcon(ToggleMenuFlyoutItem item, WidgetSelectOption option)
    {
        if (option.Glyph is not { } glyph) return null;
        var icon = new WidgetNativePackageIcon(value => item.Icon = value, () => XamlRoot);
        icon.Update(glyph, option.PackageIcon, option.AccessibilityLabel ?? option.Label, IconResolver(), PackageGeneration, IconDiagnostic);
        return icon;
    }
}
