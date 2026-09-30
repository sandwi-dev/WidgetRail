using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetPresentationSession;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    // RendererFixtureExporter output from the real Spotify test harness. Render
    // only the unmodified transport fragment: library/remote artwork are outside
    // this focus check, and the action callback is a local completion gate.
    private async Task NativeSpotifyBusyAsync(string directory)
    {
        var readyFiles = Directory.GetFiles(directory, "Spotify-Busy-*-ready.renderer.json");
        if (readyFiles.Length == 0) throw new InvalidOperationException("No exported Spotify busy-state fixtures were found.");
        foreach (var path in readyFiles.Order(StringComparer.Ordinal))
        {
            var prefix = path[..^"ready.renderer.json".Length];
            var label = Path.GetFileName(prefix);
            var stages = new[] { "ready", "pending", "acknowledged", "settled" }.ToDictionary(stage => stage,
                stage => Read(prefix + stage + ".renderer.json"));
            Apply(stages["ready"]);
            var id = Toggle(stages["ready"].Snapshot.Root).Id;
            await Wait(() => Find<Button>("Widget." + id) is { IsLoaded: true, IsEnabled: true });
            var button = Find<Button>("Widget." + id)!;
            var command = button.Command ?? throw new InvalidOperationException("Spotify transport has no native command.");
            button.Focus(FocusState.Keyboard);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            presenter.DispatchActionAsync = _ => { ++calls; return release.Task; };
            try
            {
                command.Execute(null);
                await Wait(() => calls == 1);
                Check(button.IsEnabled && command.CanExecute(null) && button.FocusState != FocusState.Unfocused,
                    label + " pending native command preserves focus before the next widget publication");
                foreach (var stage in new[] { "pending", "acknowledged" })
                {
                    var declaration = Toggle(stages[stage].Snapshot.Root);
                    Check(declaration.IsDisabled != true && (stage != "pending" || declaration.IsBusy == true),
                        label + stage + " real declaration blocks action without declaring its focused transport unavailable");
                    Apply(stages[stage]);
                    await Task.Delay(30);
                    Check(ReferenceEquals(button, Find<Button>("Widget." + id)) && button.IsEnabled && button.FocusState != FocusState.Unfocused,
                        label + stage + " retains the exact native transport and focus");
                    if (declaration.IsBusy == true)
                    {
                        command.Execute(null);
                        await Task.Delay(20);
                        Check(calls == 1, label + stage + " busy action admission rejects duplicate invocation");
                    }
                }
                release.TrySetResult();
                Apply(stages["settled"]);
                await Task.Delay(30);
                Check(button.IsEnabled && button.FocusState != FocusState.Unfocused,
                    label + " provider-confirmed state preserves the transport focus");

                // An explicit move during the busy interval supersedes memory;
                // completion must not drag focus back to the initiating button.
                Apply(stages["pending"]);
                if (!presenter.MoveFocus(FocusNavigationDirection.Right)) throw new InvalidOperationException("Spotify transport had no next focus target.");
                await Wait(() => !ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), button));
                var moved = FocusManager.GetFocusedElement(XamlRoot);
                Check(!ReferenceEquals(moved, button), label + " explicit navigation leaves the busy transport");
                Apply(stages["settled"]);
                await Task.Delay(30);
                Check(ReferenceEquals(moved, FocusManager.GetFocusedElement(XamlRoot)),
                    label + " completion preserves the user's explicit new focus target");
            }
            finally
            {
                release.TrySetResult();
                presenter.DispatchActionAsync = _ => { ++actions; return Task.CompletedTask; };
            }
        }

        void Apply((ViewSnapshot Snapshot, Dictionary<string, BridgeNodeRenderStyles> Styles) fixture)
        {
            var snapshot = fixture.Snapshot with { WidgetInstanceId = "spotify-declaration.fixture", Sequence = ++sequence };
            var errors = ViewSnapshotValidator.Validate(snapshot);
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("; ", errors));
            var target = Toggle(snapshot.Root);
            var row = Parent(snapshot.Root, target.Id) ?? throw new InvalidOperationException("Spotify transport fragment was missing.");
            var descriptor = new BridgeWidgetDescriptor { Id = "spotify-declaration", Name = "Spotify declarations", InstanceId = snapshot.WidgetInstanceId,
                RuntimeGeneration = "fixture-runtime", PresentationGeneration = "fixture-presentation", Icon = WidgetGlyph.Music, PackageContentDigest = "" };
            var frame = new WidgetPresentationFrame(new(descriptor.Id, descriptor.RuntimeGeneration, descriptor.PresentationGeneration, 1,
                descriptor.InstanceId, snapshot.Sequence, snapshot.ActiveInputScopeId), descriptor, snapshot, fixture.Styles);
            presenter.Width = snapshot.Surface?.PreferredWidth ?? 840;
            // The extracted row inherits this scope from the production root;
            // make that boundary explicit when testing the fragment alone.
            presenter.ApplyFragment(frame, row with { InputScopeId = snapshot.ActiveInputScopeId }, snapshot.ActiveInputScopeId);
        }
        static ViewNode Toggle(ViewNode node) => FindToggle(node) ?? throw new InvalidOperationException("Spotify declaration has no play-toggle button.");
        static ViewNode? FindToggle(ViewNode node) => node.Kind == ViewNodeKind.Button && node.ActionId == "spotify.play-toggle" ? node :
            node.Children.Select(FindToggle).FirstOrDefault(value => value is not null);
        static ViewNode? Parent(ViewNode node, string id) => node.Children.Any(child => child.Id == id) ? node :
            node.Children.Select(child => Parent(child, id)).FirstOrDefault(value => value is not null);
        static (ViewSnapshot Snapshot, Dictionary<string, BridgeNodeRenderStyles> Styles) Read(string path)
        {
            if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Renderer fixture exceeds its bound.");
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var snapshot = SnapshotJson.Deserialize(System.Text.Encoding.UTF8.GetBytes(document.RootElement.GetProperty("snapshot").GetRawText()));
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            var styles = document.RootElement.GetProperty("renderStyles").Deserialize<Dictionary<string, BridgeNodeRenderStyles>>(options)
                ?? throw new InvalidDataException("Renderer fixture has no styles.");
            return (snapshot, styles);
        }
    }
}
