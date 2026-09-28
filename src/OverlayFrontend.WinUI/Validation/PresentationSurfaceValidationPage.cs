using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

/// <summary>Native presentation-policy checks without keyboard injection or controller ownership.</summary>
internal sealed class PresentationSurfaceValidationPage : Page, IAsyncDisposable
{
    private readonly WidgetViewPresenter presenter = new();
    private readonly TextBlock status = new() { Text = "pending" };
    private readonly TaskCompletionSource releaseSlow = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource enteredSlow = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? running;
    private long sequence;
    private int checks;
    private string? failure;
    internal PresentationSurfaceValidationPage()
    {
        AutomationProperties.SetAutomationId(status, "Surfaces.Result");
        Content = new StackPanel { Spacing = 8, Children = { status, presenter } };
        presenter.Failed = error => failure = error.Message;
        presenter.ResolveArtworkAsync = async (handle, _) =>
        {
            if (handle == "missing") return null;
            if (handle == "slow") { enteredSlow.TrySetResult(); await releaseSlow.Task; }
            return new(WidgetArtworkContentType.Png, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jq1sAAAAASUVORK5CYII="));
        };
        Loaded += (_, _) => running ??= RunAsync();
    }
    private async Task RunAsync()
    {
        try
        {
            Apply();
            await Focus("a");
            await Until(() => Text() == "A" && BackgroundSurface()?.ArtworkSource is not null);
            ++checks;
            await Focus("outside");
            Check(Text() == "A", "focus leaving surface lost retained fragment");
            Apply(label: "A updated");
            await Until(() => Text() == "A updated"); ++checks;
            Apply(removeA: true);
            await Until(() => Text() == "Default"); ++checks;
            Apply(retain: false);
            await Focus("a"); await Until(() => Text() == "A");
            await Focus("outside"); await Until(() => Text() == "Default"); ++checks;
            Apply(); await Focus("a"); await Until(() => Text() == "A");
            Apply(alternate: true); await Focus("other");
            Check(Text() == "A", "inactive parent lost retained presentation");
            Apply(); await Focus("slow");
            await enteredSlow.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Focus("b"); await Until(() => Text() == "B");
            await Task.Delay(100);
            var selected = BackgroundSurface()?.ArtworkSource;
            releaseSlow.TrySetResult(); await Task.Delay(100);
            Check(ReferenceEquals(selected, BackgroundSurface()?.ArtworkSource), "late artwork replaced current source");
            await Focus("missing"); await Until(() => BackgroundSurface()?.ArtworkSource is null);
            Check(Text() == "No artwork", "missing artwork left the previous item's presentation");
            Apply(nested: true); await Focus("a");
            await Until(() => Text() == "A");
            var outer = BackgroundSurface()?.ArtworkSource;
            await Focus("b"); await Task.Delay(100);
            Check(ReferenceEquals(outer, BackgroundSurface()?.ArtworkSource), "nested opt-out leaked contribution to outer background");
            Check(failure is null, failure ?? "unknown presenter error");
            status.Text = $"passed:{checks}";
        }
        catch (Exception error) { releaseSlow.TrySetResult(); status.Text = "failed:" + error.Message; }
    }
    private void Apply(bool retain = true, bool removeA = false, string label = "A", bool alternate = false, bool nested = false)
    {
        ViewNode Item(string id, string text) => new() { Id = id, Kind = ViewNodeKind.Button, Text = text, ActionId = id,
            FocusBackgroundArtworkHandle = id, FocusPresentation = new() { Id = id + ".summary", Kind = ViewNodeKind.Text, Text = text } };
        var items = new List<ViewNode> { Item("b", "B"), Item("slow", "Slow"), Item("missing", "No artwork") };
        if (!removeA) items.Insert(0, Item("a", label));
        ViewNode content = new() { Id = "items", Kind = ViewNodeKind.Row, Children = items };
        if (nested) content = new() { Id = "nested", Kind = ViewNodeKind.BackgroundSurface, Children = [content] };
        var snapshot = new ViewSnapshot
        {
            WidgetInstanceId = "surface.instance", Sequence = ++sequence, ActiveInputScopeId = alternate ? "other-scope" : "root", InitialFocusId = alternate ? "other" : "b",
            Root = new() { Id = "root", Kind = ViewNodeKind.Stack, Children = [
                new() { Id = "background", Kind = ViewNodeKind.BackgroundSurface, ArtworkHandle = "fallback", ImageFit = ImageFit.Cover, UsesFocusedDescendantArtwork = true, Children = [
                    new() { Id = "fragment", Kind = ViewNodeKind.FocusPresentationSurface, RetainLastPresentation = retain,
                        DefaultFocusPresentation = new() { Id = "fallback-summary", Kind = ViewNodeKind.Text, Text = "Default" }, Children = [content] }] },
                new() { Id = "outside", Kind = ViewNodeKind.Button, Text = "Outside", ActionId = "outside" },
                new() { Id = "other-scope", Kind = ViewNodeKind.Stack, InputScopeId = "other-scope", Children = [
                    new() { Id = "other", Kind = ViewNodeKind.Button, Text = "Other scope", ActionId = "other" }] }] },
        };
        var descriptor = new BridgeWidgetDescriptor { Id = "surface", Name = "Surface", InstanceId = snapshot.WidgetInstanceId,
            RuntimeGeneration = "runtime", PresentationGeneration = "presentation", Icon = WidgetGlyph.Connection, PackageContentDigest = "" };
        var errors = ViewSnapshotValidator.Validate(snapshot);
        if (errors.Count != 0) throw new InvalidOperationException(string.Join("; ", errors.Select(error => error.Path + ":" + error.Message)));
        presenter.Apply(new(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
            snapshot.WidgetInstanceId, sequence, snapshot.ActiveInputScopeId), descriptor,
            SnapshotJson.Deserialize(SnapshotJson.Serialize(snapshot)), new Dictionary<string, BridgeNodeRenderStyles>()));
    }
    private async Task Focus(string id)
    {
        await Until(() => Descendants(presenter).OfType<Button>().Any(button => AutomationProperties.GetAutomationId(button) == "Widget." + id && button.IsLoaded));
        Descendants(presenter).OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "Widget." + id).Focus(FocusState.Keyboard);
        await Task.Delay(50);
    }
    private string? Text() => Descendants(presenter).OfType<WidgetPresentationSurface>().SingleOrDefault(surface => surface.Fragment is not null) is { Fragment: { } fragment }
        ? Descendants(fragment).OfType<TextBlock>().FirstOrDefault()?.Text : null;
    private WidgetPresentationSurface? BackgroundSurface() => Descendants(presenter).OfType<WidgetPresentationSurface>().FirstOrDefault(surface => AutomationProperties.GetAutomationId(surface) == "Widget.background");
    private void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); ++checks; }
    private static async Task Until(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (!condition()) { if (Environment.TickCount64 > deadline) throw new TimeoutException("Surface condition did not settle."); await Task.Delay(20); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); ++index)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    public async ValueTask DisposeAsync() { releaseSlow.TrySetResult(); if (running is not null) await running; await presenter.DisposeAsync(); }
}
