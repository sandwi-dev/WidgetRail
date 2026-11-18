using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayFrontend.WinUI.Collections;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    // Explicit own-app smoke test: real widget/provider/authority. The opt-in
    // favorite lane requires staged copied metadata; originals stay read-only.
    internal void EnableCaptureGalleryValidation(string output)
    {
        var started = false;
        Loaded += async (_, _) =>
        {
            if (started) return;
            started = true;
            var path = Path.GetFullPath(output);
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            var checks = new List<string>();
            var step = "startup";
            Exception? failure = null;
            object? failedState = null;
            object? actionGeometry = null;
            MediaPlayerView? testedMedia = null;
            MediaPresentationStage? testedPlane = null;
            var originalPlayers = 0;
            var appliedFilter = false;
            var favoriteOnly = Environment.GetCommandLineArgs().Contains("--capture-favorite-continuity-only");
            string[]? favoriteIds = null;
            Func<object>? favoriteDiagnostic = null;
            WidgetIndexedCollectionView? favoriteGallery = null;
            var favoriteObservations = new List<object>();
            var favoriteTrace = new List<string>();
            try
            {
                if (startup is not null) await startup;
                if (favoriteOnly)
                {
                    using var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(options.SettingsRoot, "capture-favorite-fixture.json")));
                    var guard = fixture.RootElement;
                    if (guard.GetProperty("Schema").GetInt32() != 1 || !guard.GetProperty("AllowMetadataMutation").GetBoolean() ||
                        !Path.GetFullPath(guard.GetProperty("ProfilePath").GetString()!).Equals(Path.GetFullPath(options.SettingsRoot), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Favorite validation requires its explicitly staged isolated profile.");
                    favoriteIds = guard.GetProperty("CaptureIds").EnumerateArray().Select(value => value.GetString()!).ToArray();
                    if (favoriteIds.Length < 2 || favoriteIds.Any(id => !CaptureLimits.ValidToken(id)) || favoriteIds.Distinct().Count() != favoriteIds.Length)
                        throw new InvalidOperationException("Favorite fixture needs at least two exact copied captures.");
                }
                const string widgetId = "widgetrail.firstparty.capture-library";
                if (owner is null || !catalogItems.Any(item => item.Id == widgetId))
                    throw new InvalidOperationException("The production Capture Library must be installed for this smoke test.");
                await SelectAsync(widgetId, true);
                await Until(() => MainFocusEnabled && Node("capture-library.entries")?.IndexedCollection is { Count: > 0 });
                await Until(() => !startupPresentationPending && startupIndicator?.IsShowing != true && StartupContentStage.IsHitTestVisible);
                App.Window!.Activate();
                await Until(() => App.Window is MainWindow { ValidationOwnsForeground: true });
                Check(activeWidget == widgetId, "The real first-party Capture Library is active with its actual broker data");
                Check(Node("capture-library.active.type") is null, "Smoke starts with the existing All captures media filter");
                if(favoriteOnly) await FavoriteContinuityAsync();
                else if(Environment.GetCommandLineArgs().Contains("--capture-detail-focus-only"))
                    await DetailFocusAsync();
                else
                {
                originalPlayers = MediaPlayerView.ResidentPlayers;

                step = "native clip filter";
                Check(surface!.FocusHostTarget("capture-library.filters.toggle"), "Filters and view is reachable through native host focus");
                await Button(ControllerButton.A);
                await Until(() => surface.IsPreviewEnteredForValidation && PreviewControl("capture-library.filter") is Button);
                var select = (Button)PreviewControl("capture-library.filter")!;
                await Until(() => select.IsLoaded && select.ActualWidth > 0 && select.ActualHeight > 0);
                Check(select.Focus(FocusState.Keyboard), "The real preview Type selector accepts focus");
                ((IExpandCollapseProvider)FrameworkElementAutomationPeer.CreatePeerForElement(select).GetPattern(PatternInterface.ExpandCollapse)).Expand();
                await Until(() => surface.PreviewMenuItemsForValidation.Any(item => item.IsLoaded &&
                    AutomationProperties.GetAutomationId(item) == "Widget.capture-library.filter.Option.clips"));
                var clips = surface.PreviewMenuItemsForValidation.Single(item =>
                    AutomationProperties.GetAutomationId(item) == "Widget.capture-library.filter.Option.clips");
                ((IToggleProvider)FrameworkElementAutomationPeer.CreatePeerForElement(clips).GetPattern(PatternInterface.Toggle)).Toggle();
                appliedFilter = true;
                await Until(() => Node("capture-library.active.type") is not null && Node("capture-library.entries")?.IndexedCollection is { Count: > 0 });
                Check(true, "The native Type option applies Clips through the widget's normal indexed query");
                if (surface.IsPreviewEnteredForValidation) await Button(ControllerButton.B);
                Check(surface.FocusHostTarget("capture-library.search"), "Search receives focus after leaving filter interaction");
                await Until(() => !surface.IsFocusPreviewOpen);

                step = "real saved clip preview";
                var gallery = Walk(surface).OfType<WidgetIndexedCollectionView>().Single(view =>
                    AutomationProperties.GetAutomationId(view) == "Widget.capture-library.entries");
                await Until(() => gallery.Enter());
                await Until(() => surface.FocusPreviewChrome is { } chrome && Walk(chrome).OfType<MediaPlayerView>().Any());
                testedMedia = Walk(surface.FocusPreviewChrome!).OfType<MediaPlayerView>().Single();
                await Until(() => testedMedia.IsPlaying && testedMedia.HasPlaybackLeaseForValidation);
                Check(testedMedia.Definition?.Source.Kind == MediaPlayerSourceKind.SavedCapture,
                    "The preview resolves the actual indexed saved-capture reference through its scoped playback lease");
                Check(testedMedia.PreviewMutedForValidation && testedMedia.PreviewSystemControlsDisabledForValidation && !surface.IsPreviewEnteredForValidation,
                    "The actual passive clip plays muted without taking system media controls or preview focus");
                Check(MediaPlayerView.ResidentPlayers == originalPlayers + 1, "The gallery starts exactly one additional native decoder");
                testedPlane = testedMedia.PreviewStageForValidation ?? throw new InvalidOperationException("The actual clip has no native presentation stage.");
                Check(Canvas.GetZIndex(testedPlane) == (int)PresentationLayer.ContentPreviewMedia,
                    "The actual gallery clip uses the preview media band above its frame");
                var fragment = Walk(surface.FocusPreviewChrome!).OfType<WidgetViewPresenter>().Single();
                var actions = Walk(fragment).OfType<Button>().Where(button => button.Tag is WidgetElementIdentity { Kind: ViewNodeKind.Button }).ToArray();
                Check(actions.Length == 3 && actions.Any(button => AutomationProperties.GetName(button) == "Share capture"),
                    "The actual clip preview contains Open capture, Share and Favorite actions");
                await Until(() => actions.All(button => button.IsLoaded && button.ActualWidth > 0 && button.ActualHeight > 0));
                var contentViewport = Bounds(fragment);
                // Measure sibling host chrome independently of authored content;
                // do not infer the footer from a visible label or child index.
                var siblings = (VisualTreeHelper.GetParent(fragment) as Panel)?.Children.OfType<FrameworkElement>()
                    .Where(element => !ReferenceEquals(element, fragment) && element.Visibility == Visibility.Visible && element.ActualHeight > 0)
                    .Select(Bounds).ToArray() ?? [];
                var actionBounds = actions.Select(Bounds).ToArray();
                actionGeometry = new { contentViewport, siblingChrome = siblings, actions = actionBounds };
                Check(actionBounds.All(rect => rect.Left >= contentViewport.Left - .5 && rect.Top >= contentViewport.Top - .5 &&
                    rect.Right <= contentViewport.Right + .5 && rect.Bottom <= contentViewport.Bottom + .5),
                    "All preview actions fit fully inside the actual content viewport");
                Check(siblings.Length > 0 && actionBounds.All(rect => rect.Bottom <= siblings.Min(chrome => chrome.Top) + .5),
                    "All preview actions remain above the independently measured Interact footer");
                await Pixels("playing");
                try
                {
                    testedPlane.SetCovered(true);
                    await NativeFrame();
                    Check(ReferenceEquals(testedPlane, testedMedia.PreviewStageForValidation) && testedPlane.Visibility == Visibility.Collapsed,
                        "Negative pixel control still owns the collapsed current media plane");
                    await Pixels("covered");
                }
                finally { testedPlane.SetCovered(false); }
                await Pixels("restored");
                Check(true, "Actual window pixels distinguish the playing/restored clip from its covered negative control");

                step = "clip fullscreen";
                await Button(ControllerButton.RightStick);
                await Until(() => surface.IsPreviewEnteredForValidation && testedMedia.PreviewExpandControlForValidation.IsEnabled);
                var retainedPlayer = testedMedia.PlaybackPlayerForValidation;
                ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(testedMedia.PreviewExpandControlForValidation)
                    .GetPattern(PatternInterface.Invoke)).Invoke();
                await Until(() => testedMedia.IsExpanded);
                Check(ReferenceEquals(testedMedia.PlaybackPlayerForValidation, retainedPlayer) && testedMedia.HasPlaybackLeaseForValidation,
                    "Fullscreen keeps the same saved-clip decoder and playback lease");
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Pressed);
                await RouteButtonAsync(ControllerButton.B, ControllerEventPhase.Released);
                await Until(() => !testedMedia.IsExpanded && surface.IsPreviewEnteredForValidation);
                Check(ReferenceEquals(testedMedia.PlaybackPlayerForValidation, retainedPlayer), "Controller Back returns fullscreen to the same preview player");
                await Button(ControllerButton.B);

                step = "preview withdrawal";
                Check(surface.FocusHostTarget("capture-library.search"), "Moving to search leaves the actual clip source");
                await Until(() => !surface.IsFocusPreviewOpen && !testedMedia.HasPlaybackLeaseForValidation &&
                    MediaPlayerView.ResidentPlayers == originalPlayers);
                await testedMedia.PlaybackRetirement.WaitAsync(TimeSpan.FromSeconds(15));
                Check(true, "Leaving the gallery preview retires its decoder and completes the playback-lease release task");

                step = "screenshot preview";
                Check(surface.FocusHostTarget("capture-library.filters.toggle"), "Filters reopen after clip interaction");
                await Button(ControllerButton.A);
                await Until(() => surface.IsPreviewEnteredForValidation && PreviewControl("capture-library.filter") is Button);
                var imageSelect = (Button)PreviewControl("capture-library.filter")!;
                await Until(() => imageSelect.IsLoaded && imageSelect.ActualWidth > 0);
                ((IExpandCollapseProvider)FrameworkElementAutomationPeer.CreatePeerForElement(imageSelect).GetPattern(PatternInterface.ExpandCollapse)).Expand();
                await Until(() => surface.PreviewMenuItemsForValidation.Any(item => item.IsLoaded &&
                    AutomationProperties.GetAutomationId(item) == "Widget.capture-library.filter.Option.screenshots"));
                var screenshots = surface.PreviewMenuItemsForValidation.Single(item =>
                    AutomationProperties.GetAutomationId(item) == "Widget.capture-library.filter.Option.screenshots");
                ((IToggleProvider)FrameworkElementAutomationPeer.CreatePeerForElement(screenshots).GetPattern(PatternInterface.Toggle)).Toggle();
                await Until(() => Node("capture-library.entries")?.IndexedCollection is { Count: > 0 } &&
                    Node("capture-library.active.type")?.Text == "Screenshots ×");
                if (surface.IsPreviewEnteredForValidation) await Button(ControllerButton.B);
                Check(surface.FocusHostTarget("capture-library.search"), "Screenshot filter returns to the toolbar");
                await Until(() => !surface.IsFocusPreviewOpen);
                gallery = Walk(surface).OfType<WidgetIndexedCollectionView>().Single(view =>
                    AutomationProperties.GetAutomationId(view) == "Widget.capture-library.entries");
                await Until(() => gallery.Enter());
                await Until(() => surface.FocusPreviewChrome is { } chrome && Walk(chrome).OfType<MediaPlayerView>().Any());
                testedMedia = Walk(surface.FocusPreviewChrome!).OfType<MediaPlayerView>().Single();
                await Until(() => testedMedia.IsImage && testedMedia.CanControl && testedMedia.HasPlaybackLeaseForValidation);
                Check(MediaPlayerView.ResidentPlayers == originalPlayers, "A saved screenshot preview acquires its image lease without a video decoder");
                Check(Walk(surface.FocusPreviewChrome!).OfType<Button>().Any(button => AutomationProperties.GetName(button) == "Share capture"),
                    "Screenshot previews expose the same Share action");
                await Pixels("screenshot", Bounds(testedMedia));
                await Button(ControllerButton.LeftStick);
                await Until(() => FocusId() == "Widget.capture-library.search" && !surface.IsFocusPreviewOpen);
                await testedMedia.PlaybackRetirement.WaitAsync(TimeSpan.FromSeconds(15));
                Check(!testedMedia.HasPlaybackLeaseForValidation, "L3 returns to Search and retires the screenshot preview lease");
                step = "restore media filter";
                Check(surface.FocusHostTarget("capture-library.active.type"), "The existing active filter chip can restore All captures");
                await Button(ControllerButton.A);
                await Until(() => Node("capture-library.active.type") is null && Node("capture-library.entries")?.IndexedCollection is { Count: > 0 });
                appliedFilter = false;
                Check(true, "The smoke returns the gallery to All captures without altering media or metadata");
                step = "capture detail navigation";
                gallery = Walk(surface).OfType<WidgetIndexedCollectionView>().Single(view =>
                    AutomationProperties.GetAutomationId(view) == "Widget.capture-library.entries");
                Check(gallery.Enter(), "Detail entry admits the current indexed gallery");
                await Until(() => gallery.CaptureFocusedItem() is not null);
                await Button(ControllerButton.A);
                await Until(() => Node("capture-library.back") is { Text: "Back to library", IsBusy: not true } &&
                    Node("capture-library.preview")?.MediaPlayer?.Source.Attachment is not null);
                Check(Node("capture-library.back")?.Glyph is null &&
                    Node("capture-library.viewer.previous")?.Glyph == WidgetGlyph.ChevronLeft &&
                    Node("capture-library.viewer.next")?.Glyph == WidgetGlyph.ChevronRight,
                    "Detail uses a labeled library return and distinct paired capture chevrons");
                Check(Node("capture-library.share")?.Text == "Share" && Node("capture-library.edit-copy")?.Text == "Edit a copy" &&
                    Node("capture-library.detail.more") is { IsCommandMenu: true } commands &&
                    commands.SelectOptions.Any(option => option.ActionId == "capture-library.copy") &&
                    commands.SelectOptions.Any(option => option.ActionId == "capture-library.delete"),
                    "Detail groups labeled primary actions and keeps secondary file commands in the native menu");
                await Until(() =>
                {
                    testedMedia = Walk(surface).OfType<MediaPlayerView>().SingleOrDefault();
                    return testedMedia is { IsLoaded: true, CanControl: true };
                });
                var detailPlayer = testedMedia!;
                await Pixels("detail", detailPlayer.TransformToVisual(XamlRoot.Content)
                    .TransformBounds(new(0, 0, detailPlayer.ActualWidth, detailPlayer.ActualHeight)));
                var detailMedia = Node("capture-library.preview")!.MediaPlayer!.Source.Attachment!.Id;
                Check(surface.FocusHostTarget("capture-library.back"), "Library return is reachable at the header");
                await Button(ControllerButton.RightBumper);
                await Until(() => Node("capture-library.back") is { IsBusy: not true } &&
                    Node("capture-library.preview")?.MediaPlayer?.Source.Attachment?.Id is { } next && next != detailMedia);
                Check(true,"RB navigates to the next capture when focus is outside the player");
                await Button(ControllerButton.B);
                await Until(() => Node("capture-library.entries") is not null && Node("capture-library.back") is null);
                Check(true,"B returns to the existing gallery without changing saved media");
                }
            }
            catch (Exception error)
            {
                failure = error;
                failedState = new { step, activeWidget, focus = FocusId(), preview = surface?.FocusPreviewDiagnostics(),
                    select = surface?.PreviewSelectReadinessForValidation("capture-library.filter"),
                    menu = surface?.PreviewMenuItemsForValidation.Select(item => new { id = AutomationProperties.GetAutomationId(item), item.IsLoaded, item.ActualWidth, item.ActualHeight }).ToArray(),
                    playing = testedMedia?.IsPlaying, lease = testedMedia?.HasPlaybackLeaseForValidation,
                    players = MediaPlayerView.ResidentPlayers, originalPlayers, appliedFilter,
                    indexed = Node("capture-library.entries")?.IndexedCollection,
                    detailBack = Node("capture-library.back")?.Text, detailBusy = Node("capture-library.back")?.IsBusy,
                    detailHasAttachment = Node("capture-library.preview")?.MediaPlayer?.Source.Attachment is not null,
                    favorite = FavoriteDiagnosticState() };
            }
            finally
            {
                testedPlane?.SetCovered(false);
                if (favoriteGallery is not null) favoriteGallery.NavigationTrace = null;
                if (appliedFilter && surface?.FocusHostTarget("capture-library.active.type") == true)
                {
                    try { await Button(ControllerButton.A); }
                    catch (Exception error) { failure ??= error; }
                }
            }
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { passed = failure is null, checks, failedState, actionGeometry,
                favoriteObservations, favoriteTrace, error = failure?.ToString() }));

            void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
            object? FavoriteDiagnosticState()
            {
                try { return favoriteDiagnostic?.Invoke(); }
                catch (Exception error) { return new { diagnosticError = error.ToString() }; }
            }
            string FocusId() => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject item ? AutomationProperties.GetAutomationId(item) : "";
            Windows.Foundation.Rect Bounds(FrameworkElement element) => element.TransformToVisual(XamlRoot.Content)
                .TransformBounds(new(0, 0, element.ActualWidth, element.ActualHeight));
            ViewNode? Node(string id) => surface?.CurrentBinding is { } binding ? Nodes(binding.View.Root).FirstOrDefault(node => node.Id == id) : null;
            FrameworkElement? PreviewControl(string id) => surface?.FocusPreviewChrome is { } chrome
                ? Walk(chrome).OfType<FrameworkElement>().FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == "Widget." + id) : null;
            async Task Button(ControllerButton button)
            { await Until(() => MainFocusEnabled); await RouteButtonAsync(button, ControllerEventPhase.Pressed); await RouteButtonAsync(button, ControllerEventPhase.Released); }
            async Task DetailFocusAsync()
            {
                async Task Open()
                {
                    var gallery=Walk(surface!).OfType<WidgetIndexedCollectionView>().Single(view=>
                        AutomationProperties.GetAutomationId(view)=="Widget.capture-library.entries");
                    Check(gallery.Enter(),"Owned capture detail admits the current indexed source");
                    await Until(()=>gallery.CaptureFocusedItem() is not null&&!gallery.HasPendingNativeNavigation);
                    await Button(ControllerButton.A);
                    await Until(()=>Node("capture-library.back") is {IsBusy:not true}&&Node("capture-library.preview")?.MediaPlayer?.Source.Attachment is not null);
                }
                bool InActions()=>Node("capture-library.actions") is {} group&&Nodes(group).Any(node=>node.IsFocusable&&FocusId()=="Widget."+node.Id);
                step="detail L3 entry";await Open();
                Check(surface!.FocusHostTarget("capture-library.back"),"Detail header accepts focus before L3");
                await Until(()=>FocusId()=="Widget.capture-library.back");await Button(ControllerButton.LeftStick);
                await Until(()=>InActions());
                var intent=surface.CurrentBinding!.View.FocusGroupEntryRequest;
                Check(intent is {GroupId:"capture-library.actions",TargetId:null},"L3 uses current action-group entry without a retained exact child");
                Check(surface.FocusHostTarget("capture-library.favorite"),"The action group exposes a remembered child");
                await Until(()=>FocusId()=="Widget.capture-library.favorite");
                Check(surface.FocusHostTarget("capture-library.back"),"Focus leaves the action group through its real header");
                await Until(()=>FocusId()=="Widget.capture-library.back");await Button(ControllerButton.LeftStick);
                await Until(()=>FocusId()=="Widget.capture-library.favorite");
                Check(true,"L3 preserves the existing host remembered-child behavior");
                step="detail Back and reopen";await Button(ControllerButton.B);
                await Until(()=>Node("capture-library.entries") is not null&&Node("capture-library.back") is null);
                await Open();
                Check(validationFailure is null&&ViewSnapshotValidator.Validate(surface.CurrentBinding!.View).Count==0,
                    "Back and reopening do not reject the retained group request or enter host recovery");
                await Button(ControllerButton.LeftStick);await Until(()=>InActions());
                Check(surface.CurrentBinding!.View.FocusGroupEntryRequest is {TargetId:null} next&&next.RequestId>intent!.RequestId,
                    "Reopened detail accepts a fresh one-shot L3 group intent");
                await Button(ControllerButton.B);await Until(()=>Node("capture-library.entries") is not null&&Node("capture-library.back") is null);
                Check(validationFailure is null,"Targeted detail focus scenario ends in the gallery without mutations");
            }
            async Task FavoriteContinuityAsync()
            {
                const string id = "capture-library.entries";
                var gallery = Walk(surface!).OfType<WidgetIndexedCollectionView>().Single(view => AutomationProperties.GetAutomationId(view) == "Widget." + id);
                favoriteGallery = gallery;
                gallery.NavigationTrace = value => { if (favoriteTrace.Count == 160) favoriteTrace.RemoveAt(0); favoriteTrace.Add(value); };
                ListViewBase View() => Walk(gallery).OfType<ListViewBase>().Single();
                ScrollViewer Scroll() => Walk(View()).OfType<ScrollViewer>().First();
                IndexedCollectionDescriptor Definition() => Node(id)!.IndexedCollection!;
                WidgetIndexedRow? Row(int index) => index >= 0 && index < View().Items.Count && View().Items[index] is IndexedItem<WidgetIndexedRow> slot ? slot.Value : null;
                FrameworkElement? Container(int index) => View().ContainerFromIndex(index) as FrameworkElement;
                bool Star(int index) => Container(index) is { } container && Walk(container).OfType<TextBlock>().Any(text => text.IsLoaded && text.Text == "★");
                favoriteDiagnostic = () => new
                {
                    definition = Definition(), navigation = gallery.NavigationDiagnosticState(), focus = FocusId(),
                    offset = Scroll().VerticalOffset, maximum = Scroll().ScrollableHeight, viewport = Scroll().ViewportHeight,
                    preview = surface!.FocusPreviewDiagnostics(),
                    rows = Enumerable.Range(0, View().Items.Count).Where(index => Row(index) is not null).Take(32).Select(index => new
                    { index, key = Row(index)!.Item.Key, current = Row(index)!.Lease.IsCurrent, star = Star(index), bounds = Container(index) is { } container ? Bounds(container) : (Windows.Foundation.Rect?)null }).ToArray()
                };
                void Observe(string phase) => favoriteObservations.Add(new { phase, state = favoriteDiagnostic() });
                async Task Enter(int index)
                {
                    View().ScrollIntoView(View().Items[index]);
                    await Until(() => Row(index) is { Lease.IsCurrent: true } && Container(index) is { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 });
                    var key = Row(index)!.Item.Key;
                    Check(favoriteIds!.Contains(key), "Indexed target belongs to the isolated copied-metadata fixture");
                    Check(gallery.Enter(new(id, Definition().SourceId, Definition().QueryGeneration, key, index)), "Current scoped gallery target admits native entry");
                    await Until(() => gallery.CaptureFocusedItem()?.ItemKey == key && !gallery.HasPendingNativeNavigation && !gallery.IsEntryPending);
                    await NativeFrame();
                }
                async Task ContextFavorite()
                {
                    var target = gallery.CaptureContextRow() ?? throw new InvalidOperationException("No current native context row.");
                    Check(target.Row.Item.Root.ContextActions is { Count: 1 } actions && actions[0].ActionId == "capture-library.item.favorite" && !actions[0].IsDisabled,
                        "The native row menu contains only the authorized Favorite action");
                    await Button(ControllerButton.Menu);
                    await Until(() => surface!.HasTransientControl && FocusId().EndsWith(".Context.capture-library.item.favorite", StringComparison.Ordinal));
                    await Button(ControllerButton.A);
                    await Until(() => !surface!.HasTransientControl);
                }

                Check(Definition().Count == favoriteIds!.Length, "The real broker exposes exactly the staged fixture records");
                step = "favorite current scrolled card";
                var firstIndex = Definition().Count - 2;
                await Enter(firstIndex);
                Check(!Star(firstIndex), "The staged card starts without a favorite star");
                var firstKey = Row(firstIndex)!.Item.Key;
                var before = Definition(); var retainedContainer = Container(firstIndex); var offset = Scroll().VerticalOffset;
                Check(Scroll().ScrollableHeight <= 1 || offset > 1, "Favorite checks use a scrolled row whenever the gallery has scrollable content");
                Observe("ordinary-before");
                await ContextFavorite();
                await Until(() => Definition().ContentRevision > before.ContentRevision && Star(firstIndex) && gallery.CaptureFocusedItem()?.ItemKey == firstKey);
                await NativeFrame();
                Check(Definition().QueryGeneration == before.QueryGeneration && ReferenceEquals(Container(firstIndex), retainedContainer),
                    "Favorite changes update the visible star without replacing query generation or native card container");
                Check(Math.Abs(Scroll().VerticalOffset - offset) < 1 && gallery.CaptureFocusedItem()?.ItemKey == firstKey,
                    "Favorite changes retain the exact native row focus and scroll offset");
                Observe("ordinary-after");

                step = "favorite inside entered screenshot preview";
                var secondIndex = firstIndex + 1;
                await Enter(secondIndex);
                var secondKey = Row(secondIndex)!.Item.Key;
                await Until(() => surface!.FocusPreviewChrome is { } chrome && Walk(chrome).OfType<MediaPlayerView>().Any(media =>
                    media.Definition?.Source.SavedCapture?.Id == secondKey && media.HasPlaybackLeaseForValidation));
                testedMedia = Walk(surface!.FocusPreviewChrome!).OfType<MediaPlayerView>().Single();
                await Button(ControllerButton.RightStick);
                await Until(() => surface.IsPreviewEnteredForValidation);
                var previewNode = Nodes(Row(secondIndex)!.Item.Root.FocusPresentation!).Single(node => node.Kind == ViewNodeKind.Button && node.ActionId == "capture-library.item.favorite");
                await Until(() => PreviewControl(previewNode.Id) is Button { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 });
                var favorite = (Button)PreviewControl(previewNode.Id)!;
                Check(favorite.Focus(FocusState.Keyboard), "Entered preview Favorite accepts actual native focus");
                await Until(() => ReferenceEquals(FocusManager.GetFocusedElement(favorite.XamlRoot), favorite));
                var chromeBefore = surface.FocusPreviewChrome; var mediaBefore = testedMedia; before = Definition();
                Observe("preview-before");
                ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(favorite).GetPattern(PatternInterface.Invoke)).Invoke();
                await Until(() => Definition().ContentRevision > before.ContentRevision && Star(secondIndex) &&
                    PreviewControl(previewNode.Id) is Button && surface.IsPreviewEnteredForValidation);
                await NativeFrame();
                Check(Definition().QueryGeneration == before.QueryGeneration && ReferenceEquals(chromeBefore, surface.FocusPreviewChrome) &&
                    ReferenceEquals(mediaBefore, Walk(surface.FocusPreviewChrome!).OfType<MediaPlayerView>().Single()),
                    "Entered preview favorite refresh retains its query, chrome and native media control");
                Check(mediaBefore.HasPlaybackLeaseForValidation && mediaBefore.Definition?.Source.SavedCapture?.Id == secondKey &&
                    ReferenceEquals(PreviewControl(previewNode.Id), favorite) && ReferenceEquals(FocusManager.GetFocusedElement(favorite.XamlRoot), favorite),
                    "Entered preview favorite refresh keeps the current media lease and exact action focus");
                Observe("preview-after");
                await Button(ControllerButton.B);
                await Until(() => !surface.IsPreviewEnteredForValidation && gallery.CaptureFocusedItem()?.ItemKey == secondKey);

                step = "FavoritesOnly removal retains nearest survivor";
                Check(surface.FocusHostTarget("capture-library.filters.toggle"), "The native Filters control admits FavoritesOnly selection");
                await Until(() => FocusId() == "Widget.capture-library.filters.toggle");
                await Button(ControllerButton.A);
                await Until(() => surface.IsPreviewEnteredForValidation && PreviewControl("capture-library.filter") is Button { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 });
                var type = (Button)PreviewControl("capture-library.filter")!;
                ((IExpandCollapseProvider)FrameworkElementAutomationPeer.CreatePeerForElement(type).GetPattern(PatternInterface.ExpandCollapse)).Expand();
                await Until(() => surface.PreviewMenuItemsForValidation.Any(item => item.IsLoaded && AutomationProperties.GetAutomationId(item) == "Widget.capture-library.filter.Option.favorites"));
                var option = surface.PreviewMenuItemsForValidation.Single(item => AutomationProperties.GetAutomationId(item) == "Widget.capture-library.filter.Option.favorites");
                ((IToggleProvider)FrameworkElementAutomationPeer.CreatePeerForElement(option).GetPattern(PatternInterface.Toggle)).Toggle();
                appliedFilter = true;
                await Until(() => Definition().Count == 2 && Node("capture-library.active.type")?.Text == "Favorites ×");
                if (surface.IsPreviewEnteredForValidation) await Button(ControllerButton.B);
                Check(surface.FocusHostTarget("capture-library.search"), "Favorites filter can leave its entered preview");
                await Until(() => !surface.IsFocusPreviewOpen);
                await Enter(0);
                View().ScrollIntoView(View().Items[1]);
                await Until(() => Row(1) is { Lease.IsCurrent: true });
                var survivor = Row(1)!.Item.Key;
                await Enter(0); before = Definition(); Observe("removal-before");
                await ContextFavorite();
                await Until(() => Definition().Count == 1 && Definition().QueryGeneration > before.QueryGeneration &&
                    gallery.CaptureFocusedItem()?.ItemKey == survivor && !gallery.IsEntryPending && !gallery.HasPendingNativeNavigation);
                Check(Definition().RefreshFromQuery is { } origin && origin <= before.QueryGeneration &&
                    gallery.CaptureFocusedItem()?.Index == 0 && FocusId() != "Widget.capture-library.search",
                    "Removing the focused favorite uses proved refresh and focuses the nearest remaining card instead of Search");
                Check(Row(0)?.Lease.IsCurrent == true && Star(0), "The surviving favorite owns a current row lease and visible star");
                Observe("removal-after");
                Check(validationFailure is null, "Favorite continuity completes without host recovery or stale-action dispatch");
            }
            async Task Until(Func<bool> ready)
            {
                var deadline = Environment.TickCount64 + 20000;
                while (!ready())
                {
                    if (validationFailure is not null || retired || Environment.TickCount64 > deadline)
                        throw new InvalidOperationException("Capture gallery smoke did not settle: " + step + "; focus=" + FocusId(), validationFailure);
                    await Task.Delay(25, lifetime.Token);
                }
            }
            async Task Pixels(string phase, Windows.Foundation.Rect? explicitBounds = null)
            {
                // XAML property writes precede compositor presentation. Observe
                // the next actual frame before asking the external pixel reader.
                await NativeFrame();
                var presentationClock = System.Diagnostics.Stopwatch.StartNew();
                // Rendering is a pre-presentation event. Flush only this test
                // process's queued surfaces before the external desktop read.
                await Task.Run(() => System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(FlushGalleryComposition()))
                    .WaitAsync(TimeSpan.FromSeconds(3), lifetime.Token);
                await Until(() => (explicitBounds ?? testedMedia!.PreviewVideoBoundsForValidation) is { Width: > 0, Height: > 0 });
                var rect = explicitBounds ?? testedMedia!.PreviewVideoBoundsForValidation;
                var factor = XamlRoot.RasterizationScale;
                var phasePath = Path.Combine(directory, phase + ".phase.json");
                await File.WriteAllTextAsync(phasePath + ".pending", JsonSerializer.Serialize(new { phase, processId = Environment.ProcessId,
                    window = WinRT.Interop.WindowNative.GetWindowHandle(App.Window).ToInt64(),
                    x = rect.X * factor, y = rect.Y * factor, width = rect.Width * factor, height = rect.Height * factor,
                    currentMediaPlane = testedMedia.PreviewStageForValidation?.Visibility.ToString(),
                    sameMediaPlane = ReferenceEquals(testedPlane, testedMedia.PreviewStageForValidation),
                    startupPresentationPending, startupShowing = startupIndicator?.IsShowing,
                    startupStageOpacity = StartupContentStage.Opacity,
                    startupNativeOpacity = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(StartupContentStage).Opacity,
                    productionOpacity = ProductionLayout.Opacity,
                    productionNativeOpacity = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(ProductionLayout).Opacity,
                    presentationFlushMilliseconds = presentationClock.ElapsedMilliseconds }));
                File.Move(phasePath + ".pending", phasePath);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                while (!File.Exists(phasePath + ".captured")) await Task.Delay(25, deadline.Token);
                using var acknowledgement = JsonDocument.Parse(await File.ReadAllTextAsync(phasePath + ".captured"));
                Check(acknowledgement.RootElement.GetProperty("passed").GetBoolean(), "Actual gallery pixel capture: " + phase);
            }
            async Task NativeFrame()
            {
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Rendered(object? sender, object args) => completion.TrySetResult();
                CompositionTarget.Rendering += Rendered;
                try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(3), lifetime.Token); }
                finally { CompositionTarget.Rendering -= Rendered; }
            }
        };
        static IEnumerable<ViewNode> Nodes(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Nodes));
        static IEnumerable<DependencyObject> Walk(DependencyObject node)
        { yield return node; for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); ++index) foreach (var child in Walk(VisualTreeHelper.GetChild(node, index))) yield return child; }
    }
    [System.Runtime.InteropServices.LibraryImport("dwmapi.dll", EntryPoint = "DwmFlush")]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static partial int FlushGalleryComposition();
}
