using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Browser;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnableTemporaryBrowserValidation(string resultPath)
    {
        BrowserSurface.FixturePage = _ => "<!doctype html><title>Temporary browser fixture</title><body><input id='entry' aria-label='Test field'><a href='https://example.com/next'>Next</a></body>";
        Loaded += async (_, _) =>
        {
            var checks = new List<string>(); string? error = null;
            void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
            try
            {
                if (startup is not null) await startup;
                await Until(() => foreground && interactive && !switching && surface?.CurrentBinding is not null);
                await Until(() => surface?.CurrentBinding?.Frame.Snapshot.Root is { } root && !Busy(root));
                surface!.Enter(restoreNativeFocus: true);
                await Until(() => FocusManager.GetFocusedElement(XamlRoot) is Control);
                var source = activeWidget ?? throw new InvalidOperationException("The browser fixture requires a source widget.");
                var original = surface;
                var focus = FocusManager.GetFocusedElement(XamlRoot) as Control;
                var authority = surface!.CurrentBinding!.Frame.Authority;
                using (var cancelled = new CancellationTokenSource())
                {
                    cancelled.Cancel(); var rejected = false;
                    try { await OpenTemporaryWebPageAsync("https://example.com/cancelled", BrowserStorage.Private, authority, cancelled.Token); }
                    catch (OperationCanceledException) { rejected = true; }
                    Check(rejected && expandedBrowser is null && !fullscreenLayer.IsOpen && MainFocusEnabled, "Cancelled page creation leaves the source usable");
                }
                var invalidRejected = false;
                try { await OpenTemporaryWebPageAsync("file:///not-a-web-page", BrowserStorage.Private, authority, lifetime.Token); }
                catch (ArgumentException) { invalidRejected = true; }
                Check(invalidRejected && expandedBrowser is null && MainFocusEnabled, "Invalid destination fails without replacing the source");
                async Task<BrowserSurface> Open(BrowserStorage storage)
                {
                    Check(await OpenTemporaryWebPageAsync("https://example.com/temporary", storage, authority, lifetime.Token), "Opened " + storage + " page without a Browser widget receiver");
                    await Until(() => expandedBrowser?.Surface is { IsReady: true, IsNavigationInProgress: false, CurrentTitle: "Temporary browser fixture" });
                    Check(activeWidget == source && ReferenceEquals(surface, original), "Source widget stays resident under " + storage + " page");
                    return expandedBrowser!.Surface!;
                }
                async Task Close()
                {
                    await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                    await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                    await Until(() => expandedBrowser is null && !fullscreenLayer.IsOpen);
                    Check(activeWidget == source && MainFocusEnabled, "Back restores source input");
                    if (focus is { IsLoaded: true })
                    {
                        // The fullscreen layer restores focus on its low-priority settled callback.
                        await Until(() => ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), focus));
                        Check(true, "Back restores the exact source control after the close transition");
                    }
                }
                var persistent = await Open(BrowserStorage.Persistent);
                Check(!await OpenTemporaryWebPageAsync("https://example.com/nested", BrowserStorage.Private, authority, lifetime.Token) &&
                    ReferenceEquals(expandedBrowser!.Surface, persistent), "A second temporary page cannot replace the currently open page");
                var canonical = new WebBrowserDocument("browser.primary", 1, "https://example.com", "Browser") { InteractionMode = BrowserInteractionMode.InteractOnFocus, Options = new() { Storage = BrowserStorage.Persistent } };
                Check(persistent.ProfileName == BrowserProfileIdentity.Name("widgetrail.samples.browser", canonical), "Persistent page uses the Browser widget profile identity");
                var extensionFolder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(resultPath))!, "storage-probe-extension");
                Directory.CreateDirectory(extensionFolder);
                await File.WriteAllTextAsync(Path.Combine(extensionFolder, "manifest.json"), """{"manifest_version":3,"name":"WidgetRail isolated storage probe","version":"1.0","content_scripts":[{"matches":["https://example.com/*"],"js":["probe.js"],"run_at":"document_end"}]}""");
                await File.WriteAllTextAsync(Path.Combine(extensionFolder, "probe.js"), "document.documentElement.dataset.widgetrailStorageProbe='present';");
                var probe = await persistent.NativeProfile!.AddBrowserExtensionAsync(extensionFolder);
                await probe.EnableAsync(true);
                var probeId = probe.Id;
                persistent.NavigateForValidation("https://example.com/temporary?extension=1");
                await UntilAsync(async () => await persistent.EvaluateFixtureAsync("document.documentElement.dataset.widgetrailStorageProbe") == "\"present\"");
                Check(true, "Persistent page runs an enabled extension in the shared Browser profile");
                await persistent.EvaluateFixtureAsync("localStorage.setItem('temporary-browser-test','saved'); true");
                await Close();
                persistent = await Open(BrowserStorage.Persistent);
                Check(await persistent.EvaluateFixtureAsync("localStorage.getItem('temporary-browser-test')") == "\"saved\"", "Persistent storage survives page disposal and reopen");
                await Close();
                var privatePage = await Open(BrowserStorage.Private); var privateProfile = privatePage.ProfileName;
                Check(await privatePage.EvaluateFixtureAsync("localStorage.getItem('temporary-browser-test')") == "null", "Private session cannot read persistent storage");
                await privatePage.EvaluateFixtureAsync("localStorage.setItem('temporary-browser-test','private'); true");
                var privateExtensions = await privatePage.NativeProfile!.GetBrowserExtensionsAsync();
                checks.Add("Private profile native state: " + privatePage.NativeProfile.ProfileName + "; private=" + privatePage.NativeProfile.IsInPrivateModeEnabled + "; extensions=" + string.Join(", ", privateExtensions.Select(e => e.Name + " (" + e.Id + ")")));
                Check(privatePage.NativeProfile.IsInPrivateModeEnabled && privatePage.RegisterExtensionProfile is null, "Private session is native InPrivate and does not register for shared extensions");
                Check(!privateExtensions.Any(e => e.Id == probeId) && await privatePage.EvaluateFixtureAsync("document.documentElement.dataset.widgetrailStorageProbe || null") == "null", "Private page neither inherits nor runs the persistent test extension");
                await Close();
                privatePage = await Open(BrowserStorage.Private);
                Check(privatePage.ProfileName != privateProfile && await privatePage.EvaluateFixtureAsync("localStorage.getItem('temporary-browser-test')") == "null", "A new private page cannot read the previous private session");
                await Close();
                persistent = await Open(BrowserStorage.Persistent);
                Check(await persistent.EvaluateFixtureAsync("localStorage.getItem('temporary-browser-test')") == "\"saved\"", "Private browsing leaves persistent storage unchanged");
                await UntilAsync(async () => await persistent.EvaluateFixtureAsync("document.documentElement.dataset.widgetrailStorageProbe") == "\"present\"");
                var installedProbe = (await persistent.NativeProfile!.GetBrowserExtensionsAsync()).Single(e => e.Id == probeId);
                await installedProbe.RemoveAsync();
                await persistent.EvaluateFixtureAsync("localStorage.removeItem('temporary-browser-test'); true");
                await Close();
                // PoB does not declare full-widget pinning. Exercise the shared
                // return path with a source that actually advertises that surface.
                var pinSource = source;
                if (surface!.CurrentBinding!.Frame.Descriptor.FullWidgetPinningSupported != true)
                {
                    pinSource = "media-sessions";
                    await SelectAsync(pinSource, enterWidget: true);
                    await Until(() => activeWidget == pinSource && !switching && surface?.CurrentBinding is not null);
                }
                Check(surface!.CurrentBinding!.Frame.Descriptor.FullWidgetPinningSupported, "Pinned fixture declares full-widget pinning");
                await PinAsync(pinSource, WidgetRail.WidgetPresentationSession.WidgetPinnedProjection.FullWidgetLayoutId);
                Check(pinned is not null, "Created the source pinned surface");
                var sourcePin = pinned!;
                await EnterPinnedAsync();
                await Until(() => PinnedInputActive && sourcePin.Presenter?.CurrentBinding is not null);
                var pinAuthority = sourcePin.Presenter!.CurrentBinding!.Frame.Authority;
                await ReturnIntentToMainAsync(lifetime.Token);
                Check(await OpenTemporaryWebPageAsync("https://example.com/pinned", BrowserStorage.Persistent, pinAuthority, lifetime.Token, sourcePin),
                    "Pinned source opens a host page after handing input to the main window");
                await Until(() => expandedBrowser?.Surface is { IsReady: true, IsNavigationInProgress: false });
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                await Until(() => expandedBrowser is null && PinnedInputActive);
                Check(ReferenceEquals(pinned, sourcePin) && sourcePin.IsCurrent, "Back returns to the original pinned surface");
                await ReturnIntentToMainAsync(lifetime.Token);
                await UnpinAsync(save: false);
            }
            catch (Exception exception) { error = exception.ToString(); }
            finally
            {
                if (expandedBrowser is not null) fullscreenLayer.Close(restoreFocus: false);
                BrowserSurface.FixturePage = null;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultPath))!);
                await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(new { passed = error is null, checks, error }, new JsonSerializerOptions { WriteIndented = true }));
            }
        };
        static async Task Until(Func<bool> ready)
        {
            var deadline = Environment.TickCount64 + 20_000;
            while (!ready()) { if (Environment.TickCount64 >= deadline) throw new TimeoutException("Temporary browser condition timed out."); await Task.Delay(25); }
        }
        static bool Busy(ViewNode node) => node.Id == "pob.cancel-operation" || node.Children.Any(Busy);
        static async Task UntilAsync(Func<Task<bool>> ready)
        {
            var deadline = Environment.TickCount64 + 20_000;
            while (!await ready()) { if (Environment.TickCount64 >= deadline) throw new TimeoutException("Temporary browser script timed out."); await Task.Delay(25); }
        }
    }
}
