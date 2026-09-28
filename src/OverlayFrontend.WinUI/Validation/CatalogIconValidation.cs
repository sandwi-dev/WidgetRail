using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Shell;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Actual native item content, including a cancellation-ignoring recycled icon response.</summary>
internal static class CatalogIconValidation
{
    internal static async Task<IDisposable> RunAsync(StackPanel host, Action<bool, string> check)
    {
        var late = new TaskCompletionSource<WidgetPresentationPackageIcon>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unloading = new TaskCompletionSource<WidgetPresentationPackageIcon>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken lateToken = default, unloadToken = default;
        var calls = new Dictionary<string, int>();
        var content = new WidgetCatalogItemContent(async (descriptor, asset, token) =>
        {
            calls[descriptor.Id] = calls.GetValueOrDefault(descriptor.Id) + 1;
            if (descriptor.Id == "old") { lateToken = token; return await late.Task; }
            if (descriptor.Id == "unload") { unloadToken = token; return await unloading.Task; }
            if (descriptor.Id == "missing") throw new InvalidDataException();
            return Bytes(asset);
        });
        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true };
        var container = new ListViewItem { Content = content };
        AutomationProperties.SetName(container, "Catalog item");
        ToolTipService.SetToolTip(container, "Catalog item");
        list.Items.Add(container); host.Children.Add(list);
        try
        {
            content.DataContext = Item("old");
            await Until(() => calls.ContainsKey("old"));
            check(Current(content) is FontIcon && !content.IsTabStop && !content.IsHitTestVisible,
                "tray pending icon has immediate semantic fallback without an extra focus target");
            list.SelectedItem = container; container.Focus(FocusState.Keyboard);
            var template = container.Template;
            content.DataContext = Item("new");
            await Until(() => Current(content) is ImageIcon { Source: SvgImageSource });
            var accepted = Current(content);
            late.SetResult(Bytes("late")); await Task.Delay(80);
            check(lateToken.IsCancellationRequested && ReferenceEquals(accepted, Current(content)),
                "late icon cannot overwrite a recycled native item with the same asset id");
            check(ReferenceEquals(container.Template, template) && ReferenceEquals(list.SelectedItem, container) &&
                ReferenceEquals(FocusManager.GetFocusedElement(host.XamlRoot), container),
                "tray content replacement preserves native item template selection and focus");
            content.DataContext = Item("new"); await Task.Delay(40);
            check(calls["new"] == 1, "unchanged catalog identity does not restart icon demand");
            content.DataContext = Item("new") with { PackageContentDigest = "replacement" };
            await Until(() => calls["new"] == 2 && Current(content) is ImageIcon);
            check(true, "package replacement retires the prior icon identity even with unchanged widget id");
            var original = (ImageIcon)Current(content)!;
            content.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Orange);
            check(original.Source is SvgImageSource && NativePackageIconTint.For(original) is null,
                "original-color tray art keeps its native source when foreground changes");

            content.DataContext = Item("tinted") with { PackageIcon = new("mark", WidgetPackageIconColorMode.ThemeTint) };
            await Until(() => Current(content) is ImageIcon icon && NativePackageIconTint.For(icon) is not null);
            content.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Lime);
            await Until(() => Current(content) is ImageIcon icon && NativePackageIconTint.For(icon)?.CurrentColor == Microsoft.UI.Colors.Lime);
            check(true, "theme-tinted tray icon follows inherited foreground through the shared native mask");
            content.RequestedTheme = ElementTheme.Light; content.RequestedTheme = ElementTheme.Dark;
            check(calls["tinted"] == 1, "tray theme changes reuse the admitted mask instead of refetching package bytes");

            content.DataContext = Item("missing");
            await Until(() => calls.ContainsKey("missing"));
            check(Current(content) is FontIcon && AutomationProperties.GetName(content) == "missing name",
                "unavailable package artwork retains named semantic fallback");
            content.DataContext = Item("unload"); await Until(() => calls.ContainsKey("unload"));
            container.Content = null;
            await Until(() => unloadToken.IsCancellationRequested);
            unloading.SetResult(Bytes("unload")); await Task.Delay(40);
            check(Current(content) is null, "unloaded tray content cancels demand and drops native resources despite late completion");
            content.DataContext = Item("reload"); container.Content = content;
            await Until(() => Current(content) is ImageIcon);
            check(calls["reload"] == 1, "reloaded native tray item resolves only its current catalog identity");
            content.ShowLabel = false;
            check(!content.ShowLabel && Current(content) is ImageIcon,
                "same catalog icon content supports label-free radial use without another input implementation");
            content.ShowLabel = true;
            return content;
        }
        catch { content.Dispose(); host.Children.Remove(list); throw; }
    }

    private static BridgeWidgetDescriptor Item(string id) => new()
    {
        Id = id, Name = id + " name", InstanceId = id + ".instance", RuntimeGeneration = "runtime",
        PresentationGeneration = "presentation", PackageContentDigest = "fixture", Icon = WidgetGlyph.Music,
        PackageIcon = new("mark", WidgetPackageIconColorMode.OriginalColor),
    };
    private static WidgetPresentationPackageIcon Bytes(string asset)
    {
        var bytes = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\"><circle cx=\"12\" cy=\"12\" r=\"10\" fill=\"#268ad8\"/><path d=\"M8 7 L18 12 L8 17 Z\" fill=\"#ffffff\"/></svg>");
        return new(asset, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), bytes);
    }
    private static object? Current(WidgetCatalogItemContent content) =>
        ((StackPanel)content.Content).Children.OfType<WidgetPackageIconView>().FirstOrDefault()?.Content;
    private static async Task Until(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 150; ++attempt) { if (condition()) return; await Task.Delay(20); }
        throw new TimeoutException("Catalog icon condition did not settle.");
    }
}
