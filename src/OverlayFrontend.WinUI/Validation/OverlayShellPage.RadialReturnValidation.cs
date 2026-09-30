using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // Read-only production browsing: never activate a game or a widget command.
    // Exercise noninitial bottom rows, including the partial final grid row.
    internal void EnableRadialReturnValidation(string path, Func<bool> ownsNativeForeground)
    {
        Loaded += async (_, _) =>
        {
            var checks = new List<string>();
            var samples = new List<object>();
            var originalSwitcher = Appearance.WidgetSwitcher;
            try
            {
                if (startup is not null) await startup;
                Appearance = Appearance with { WidgetSwitcher = WidgetSwitcherLayout.Radial };
                await SelectAsync("games-apps", true);
                await Until(() => MainFocusEnabled && ownsNativeForeground());
                await Until(() => Collection() is { NativeView.Items.Count: > 2 });
                var collection = Collection()!;
                foreach (var index in new[] { collection.NativeView.Items.Count - 1, collection.NativeView.Items.Count - 2, 2 })
                {
                    collection.NativeView.ScrollIntoView(collection.NativeView.Items[index]);
                    await Until(() => collection.NativeView.ContainerFromIndex(index) is Control { IsLoaded: true, IsEnabled: true } target &&
                        collection.NativeView.Items[index] is IndexedItem<WidgetIndexedRow> { Value: { Lease.IsCurrent: true } expected } &&
                        target.Focus(FocusState.Keyboard) && collection.FocusedRow() is { Lease.IsCurrent: true } focused &&
                        focused.Item.Key == expected.Item.Key);
                    var key = collection.FocusedRow()!.Item.Key;
                    Sample("focused", key);
                    foreach (var exit in new[] { ControllerButton.B, ControllerButton.A, ControllerButton.B })
                    {
                        // Use the actual widget-to-shell Back route too: the previous
                        // regression opened the radial by directly changing shell flags.
                        await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                        await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                        await Until(() => RadialOpen && !interactive && FocusedTrayWidget()?.Id == "games-apps");
                        await Until(() => trayGuide.ContextualReady);
                        if (!surface!.IsGuidePresentationReady(requireFocus: false))
                            throw new InvalidOperationException("Preview guide published before visible native rows updated");
                        Sample("radial-open", key);
                        if (surface!.CapturePresentationState()?.IndexedFocus.Any(item => item.Target.ItemKey == key) != true)
                            throw new InvalidOperationException("Radial entry replaced the remembered row " + key);
                        await RouteButtonAsync(exit, ControllerEventPhase.Pressed);
                        await RouteButtonAsync(exit, ControllerEventPhase.Released);
                        await Until(() => !switching && interactive && !RadialOpen && collection.FocusedRow()?.Item.Key == key);
                        await Until(() => trayGuide.ContextualReady);
                        if (!surface!.IsGuidePresentationReady(requireFocus: true))
                            throw new InvalidOperationException("Interactive guide published before focused native rows settled");
                        Sample("returned-" + exit, key);
                        // Native fallback and provider/lifecycle publications can arrive
                        // after the initial successful restore. Sample every frame.
                        var moved = false;
                        void Observe(object? sender, object args) => moved |= collection.FocusedRow()?.Item.Key != key;
                        CompositionTarget.Rendering += Observe;
                        try { await Task.Delay(700); }
                        finally { CompositionTarget.Rendering -= Observe; }
                        if (moved) throw new InvalidOperationException("Focus drifted after radial " + exit + " returned to row " + key);
                        checks.Add($"Radial {exit} returns to index {index} and retains its exact row through subsequent frames");
                    }
                }
                // Match the hardware consumer: press/release frames do not await
                // async lifecycle/input work before the next frame is delivered.
                // Unlike the steady-state checks above, subsequent cycles use
                // only D-pad and B, never Focus() to repair the user's target.
                await CheckControllerReturnsAsync(collection);
                Write(true, null);
            }
            catch (Exception error) { Write(false, error.ToString()); }
            finally { Appearance = Appearance with { WidgetSwitcher = originalSwitcher }; }

            WidgetIndexedCollectionView? Collection() => surface is null ? null : Descendants(surface)
                .OfType<WidgetIndexedCollectionView>().FirstOrDefault(element => element.IsLoaded && element.Visibility == Visibility.Visible);
            async Task CheckControllerReturnsAsync(WidgetIndexedCollectionView collection)
            {
                var starting = collection.CaptureFocusedItem() ?? throw new InvalidOperationException("No collection focus before controller replay");
                var navigation = ControllerFrame.Create();
                navigation.Connected = 1;
                navigation.DpadNavigation = new() { Direction = NavigationDirection.Down, Phase = NavigationPhase.Pressed };
                Receive(navigation);
                await Until(() => collection.CaptureFocusedItem() is { } item && item.ItemKey != starting.ItemKey &&
                    collection.FocusedRow()?.Lease.IsCurrent == true);
                var expected = collection.CaptureFocusedItem()!;
                Sample("controller-navigated", expected.ItemKey);
                string? lastFrame = null;
                void Observe(object? sender, object args)
                {
                    var item = collection.CaptureFocusedItem();
                    // Rendering is a reentrancy-protected XAML callback. Observe
                    // dispatcher-owned identities only; IsCurrent takes the
                    // cross-thread session lock and an STA wait pumps messages.
                    var signature = $"{interactive}|{switching}|{RadialOpen}|{item?.Index}|{item?.ItemKey}|{surface?.CurrentBinding?.Frame.Authority.SnapshotSequence}";
                    if (signature == lastFrame || samples.Count >= 400) return;
                    lastFrame = signature;
                    Sample("controller-frame", expected.ItemKey);
                }
                CompositionTarget.Rendering += Observe;
                try
                {
                    for (var cycle = 0; cycle < 12; ++cycle)
                    {
                        await TapBackAsync();
                        await Until(() => RadialOpen && !interactive);
                        // Cover early return and a Visible publication arriving
                        // while the wheel has focus. Never change widget selection.
                        await Task.Delay(cycle % 3 == 0 ? 0 : cycle % 3 == 1 ? 120 : 280);
                        Sample("controller-radial-" + cycle, expected.ItemKey);
                        if (surface!.CapturePresentationState()?.IndexedFocus.Any(item =>
                                item.Target.CollectionId == expected.CollectionId && item.Target.ItemKey == expected.ItemKey) != true)
                            throw new InvalidOperationException("Controller radial entry lost the remembered item on cycle " + cycle);
                        await TapBackAsync();
                        await Until(() => MainFocusEnabled && !switching && !RadialOpen &&
                            collection.CaptureFocusedItem()?.ItemKey == expected.ItemKey);
                        if (shellOwnedReleases.Contains(ControllerButton.B))
                            throw new InvalidOperationException("B release was lost during entry on cycle " + cycle);
                        // A frame being correct once is not success: sample the
                        // interval through the next fast user gesture as well.
                        var until = Environment.TickCount64 + (cycle % 2 == 0 ? 120 : 280);
                        while (Environment.TickCount64 < until)
                        {
                            if (collection.CaptureFocusedItem()?.ItemKey != expected.ItemKey)
                                throw new InvalidOperationException("Controller radial return drifted on cycle " + cycle);
                            await Task.Delay(16);
                        }
                        checks.Add($"Controller D-pad then radial B cycle {cycle} preserves exact index {expected.Index}");
                    }
                }
                finally { CompositionTarget.Rendering -= Observe; }
            }
            async Task TapBackAsync()
            {
                var input = ControllerFrame.Create();
                input.Connected = 1;
                input.PressedButtons = input.State.Buttons = 0x2000;
                Receive(input);
                await Task.Delay(45);
                input.PressedButtons = input.State.Buttons = 0;
                input.ReleasedButtons = 0x2000;
                Receive(input);
            }
            async Task Until(Func<bool> ready,
                [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(ready))] string? waitingFor = null)
            {
                var deadline = Environment.TickCount64 + 15000;
                while (!ready())
                {
                    if (validationFailure is not null || RecoveryVisible) throw new InvalidOperationException("Production widget failed", validationFailure);
                    if (Environment.TickCount64 >= deadline) throw new TimeoutException(waitingFor + ": " + surface?.FocusDiagnostics());
                    await Task.Delay(20);
                }
            }
            void Write(bool passed, string? error)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { passed, checks, samples, error, focus = surface?.FocusDiagnostics() }));
            }
            void Sample(string phase, string key) => samples.Add(new { phase, key, atMilliseconds = Environment.TickCount64, interactive, foreground, switching,
                nativeForeground = ownsNativeForeground(), mainFocusEnabled = MainFocusEnabled,
                row = Collection()?.CaptureFocusedItem(),
                focus = surface?.FocusDiagnostics() });
            static IEnumerable<DependencyObject> Descendants(DependencyObject root)
            {
                yield return root;
                for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); ++index)
                    foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
            }
        };
    }
}
