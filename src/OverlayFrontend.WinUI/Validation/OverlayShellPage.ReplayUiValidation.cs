using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal OverlayShellOptions OptionsForReplayValidation => options;
    internal void EnableReplayUiValidation(string output, GuardedReplayUiBackend backend, Func<Func<Task>, Task> hotkey)
    {
        var started = false;
        Loaded += async (_, _) =>
        {
            if (started) return; started = true;
            var checks = new List<string>(); var step = "startup"; Exception? failure = null; string? cleanupFailure = null;
            object? failedUiState = null;
            var counts = new List<int>();
            var activationEvidence=new List<object>();
            try
            {
                if (startup is not null) await startup;
                if (owner is null || !catalogItems.Any(item => item.Id == "capture-replay-fixture") || !catalogItems.Any(item => item.Id == "replay-approval"))
                    throw new InvalidOperationException("Replay UI validation requires its isolated bridge fixture.");
                App.Window!.Activate(); await Until(() => App.Window is MainWindow { ValidationOwnsForeground: true });
                await SelectAsync("replay-approval", true); await Until(() => MainFocusEnabled && Node("approval.enable") is not null);
                step = "approval cancel";
                await Activate("approval.enable"); await Until(() => hostChoiceDialog?.IsOpenForValidation == true);
                Check(hostChoiceDialog!.ChoiceIdsForValidation.SequenceEqual(new string[] { "enable" }), "Explicit enabling uses the production bounded approval dialog");
                await Press(ControllerButton.B); await Until(() => hostChoiceDialog is null && Node("approval.result")?.Text == "Cancelled");
                Check(backend.Starts == 0 && ReplayStatusForHost?.Settings.Enabled == false, "Controller B cancels approval without launching a recorder");
                step = "approval accept";
                await Activate("approval.enable"); await Until(() => hostChoiceDialog?.IsOpenForValidation == true);
                await Until(()=>hostChoiceDialog is {} dialog&&Descendants(dialog).OfType<Button>()
                    .Any(button=>button.Name=="PrimaryButton"&&button.IsLoaded&&button.IsEnabled&&button.ActualWidth>0&&button.ActualHeight>0));
                var primary = Descendants(hostChoiceDialog!).OfType<Button>().FirstOrDefault(button => button.Name == "PrimaryButton")
                    ?? throw new InvalidOperationException("Approval has no native primary button.");
                Check(primary.Focus(FocusState.Keyboard), "Native approval primary button accepts focus"); await Press(ControllerButton.A);
                await Until(() => hostChoiceDialog is null && ReplayStatusForHost is { Settings.Enabled: true, Phase: CaptureReplayPhase.Waiting });
                Check(backend.Starts == 0, "Approving settings alone does not start recording");
                await Activate("approval.disable"); await Until(() => ReplayStatusForHost?.Settings.Enabled == false);
                await SelectAsync("capture-replay-fixture", true); await Until(() => MainFocusEnabled && Node("capture-library.more") is not null);
                step = "source cancel";
                await OpenStart(); await Until(() => hostChoiceDialog?.IsOpenForValidation == true);
                await Press(ControllerButton.B); await Until(() => hostChoiceDialog is null && Node("capture-library.replay.start") is { IsDisabled: not true });
                Check(backend.Starts == 0, "Controller cancellation of the real source chooser creates no native worker");
                step="recording settings";
                await Choose("capture-library.more","settings");
                await Until(()=>Node("capture-library.recording.replay.fps") is not null);
                Check(ReplayStatusForHost?.Settings.Recording.Selection==new CaptureRecordingSelection(),"Fresh host preferences are Auto without rewriting explicit legacy settings");
                var settingsRevision=ReplayStatusForHost!.SettingsRevision;
                await Choose("capture-library.recording.replay.fps","fps.30");
                await Until(()=>Node("capture-library.recording.replay.save") is {IsDisabled:not true});
                Check(ReplayStatusForHost.SettingsRevision==settingsRevision&&ReplayStatusForHost.Settings.Recording.Selection?.FramesPerSecond is null,
                    "A qualified native settings draft does not persist before Save");
                await Activate("capture-library.recording.replay.cancel");
                await Until(()=>Node("capture-library.recording.replay.save") is null);
                Check(ReplayStatusForHost.SettingsRevision==settingsRevision,"Native Cancel preserves the authoritative recording preferences");
                await Choose("capture-library.recording.replay.fps","fps.30");
                await Until(()=>Node("capture-library.recording.replay.save") is {IsDisabled:not true});
                await Activate("capture-library.recording.replay.save");
                await Until(()=>ReplayStatusForHost?.Settings.Recording.Selection?.FramesPerSecond==30&&Node("capture-library.recording.replay.save") is null);
                Check(ReplayStatusForHost!.Settings.Recording.Selection is {Resolution:CaptureRecordingResolution.Auto,Codec:CaptureVideoCodec.Auto}&&backend.Starts==0,
                    "Native Save preserves independent Auto choices and does not start recording");
                await Activate("capture-library.settings.back");
                step = "owned source";
                await StartOwned(); var firstSession = ReplayStatusForHost!.SessionId!;
                Check(backend.Starts == 1 && backend.LastTarget is not null, "Real Capture Library starts only the exact owned window through the native source chooser");
                Check(ReplayStatusForHost.Resolved is {Recording.MaximumWidth:1280,Recording.MaximumHeight:720,Recording.FramesPerSecond:30,
                    Recording.Encoding.Codec:CaptureVideoCodec.H264,EditingSupported:true},"Actual admitted Auto values retain native source size and editing support");
                await Choose("capture-library.more","settings");
                await Until(()=>Node("capture-library.replay.using")?.Text?.StartsWith("Using 1280 × 720",StringComparison.Ordinal)==true);
                Check(Node("capture-library.recording.replay.fps") is {IsDisabled:true},"Using summary reflects the admitted source while live encoder changes stay disabled");
                await Activate("capture-library.settings.back");
                step = "tray save";
                var before = SavedCount();
                var saveMenu = await TrayCommand("Overlay.TrayCommand.Shell.SaveReplay");
                Invoke(saveMenu); await Until(() => SavedCount() == before + 1 && backend.CompletedSaves == 1);
                Check(true, "Native overlay tray Save produces an inspected clip in the isolated library"); counts.Add(SavedCount());
                step = "registered shortcut";
                await Until(() => ReplayStatusForHost is { Saving: false, Phase: CaptureReplayPhase.Recording });
                before = SavedCount();
                await hotkey(async () => await Until(() => SavedCount() == before + 1 && backend.CompletedSaves == 2));
                Check(visible && ReplayStatusForHost?.SessionId == firstSession, "Registered host shortcut saves through the production handler without hiding or replacing recording"); counts.Add(SavedCount());
                step = "tray stop";
                var stopMenu = await TrayCommand("Overlay.TrayCommand.Shell.StopReplay");
                Invoke(stopMenu); await Until(() => ReplayStatusForHost is { Settings.Enabled: false, SessionId: null } && backend.Disposals == 1);
                Check(true, "Native overlay tray Stop ends the admitted worker and disables automatic intent");
                await SelectAsync("capture-replay-fixture", true); await Until(() => MainFocusEnabled);
                step = "replacement session";
                await StartOwned(); var secondSession = ReplayStatusForHost!.SessionId!;
                Check(secondSession != firstSession, "A subsequent start has distinct session authority");
                var attempts = backend.Saves;
                await StopReplayFromHostAsync(firstSession); await SaveReplayFromHostAsync(firstSession); await StopReplayFromHostAsync(null);
                Check(ReplayStatusForHost?.SessionId == secondSession && backend.Saves == attempts,
                    "Stale tray IDs and a previously waiting null ID cannot stop or save the new recording");
                stopMenu = await TrayCommand("Overlay.TrayCommand.Shell.StopReplay"); Invoke(stopMenu);
                await Until(() => ReplayStatusForHost?.SessionId is null && backend.Disposals == 2);
                Check(SavedCount() == 2, "Only two explicit saves were created; cancellation and stale commands created none");
                step = "shutdown before unrelated cleanup";
                await SelectAsync("capture-replay-fixture", true); await Until(() => MainFocusEnabled);
                await StartOwned();
                var cleanupGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                utilityRefresh = cleanupGate.Task;
                var shutdown = DisposeAsync().AsTask();
                try
                {
                    await Until(() => backend.Disposals == 3, allowRetired: true);
                    Check(!shutdown.IsCompleted && !cleanupGate.Task.IsCompleted,
                        "Shell shutdown retires the actual replay worker before blocked unrelated utility cleanup");
                    replayCoordinator = null;
                    InitializeReplayCapture();
                    Check(replayCoordinator is null,
                        "Late startup cannot create a replay coordinator after shell retirement");
                }
                finally
                {
                    cleanupGate.TrySetResult();
                    if (replayCoordinator is not null) await replayCoordinator.DisposeAsync();
                    await shutdown.WaitAsync(TimeSpan.FromSeconds(20));
                }
            }
            catch (Exception error)
            {
                failure = error;
                if(!retired && surface is not null)ObserveActivation("capture-library.recording.replay.fps","failure");
                failedUiState = new { status = ReplayStatusForHost, visible, foreground, interactive, MainFocusEnabled, HostChoiceActive,
                    RadialOpen, focusedTray = FocusedTrayWidget()?.Id, menuRequested = trayMenuRequest is not null,
                    menu = trayMenu?.Items.Select(item => new { id = AutomationProperties.GetAutomationId(item), item.IsLoaded, item.IsEnabled }).ToArray(),
                    recording = surface?.CurrentBinding is {} failedBinding ? Nodes(failedBinding.View.Root)
                        .Where(node=>node.Id?.StartsWith("capture-library.recording.",StringComparison.Ordinal)==true)
                        .Select(node=>new {node.Id,node.Text,node.IsDisabled,node.IsBusy,
                            options=node.SelectOptions.Select(option=>new {option.Id,option.IsDisabled}).ToArray()}).ToArray() : null,
                    options = Node("capture-library.more")?.SelectOptions.Select(option => new { option.Id, option.IsDisabled }).ToArray() };
            }
            finally
            {
                try
                {
                    hostChoiceDialog?.Hide(); CloseTrayMenu(false);
                    if (!retired && owner?.Session is { } session)
                    { var state = await session.GetReplayStatusAsync(); if (state.Settings.Enabled || state.SessionId is not null) await session.StopReplayAsync(state.SessionId); }
                }
                catch (Exception error) { cleanupFailure = error.ToString(); }
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { passed = failure is null && cleanupFailure is null, checks, step,
                    failure = failure?.ToString(), cleanupFailure, backend.Starts, backend.Saves, backend.CompletedSaves, backend.Disposals,
                    status = ReplayStatusForHost, activeWidget, focus = FocusId(), savedCounts = counts,
                    failedUiState,activationEvidence,
                    scope = "Production Capture Library, native dialogs/overlay tray/registered hotkey, exact owned WGC fixture, isolated library. Notification-area menu drawing not exercised." },
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            int SavedCount() => Directory.Exists(Path.Combine(options.SettingsRoot, "captures")) ? Directory.GetFiles(Path.Combine(options.SettingsRoot, "captures"), "*.mp4").Length : 0;
            async Task OpenStart()
            {
                await Until(() => Node("capture-library.replay.start") is { IsDisabled: not true });
                await Activate("capture-library.replay.start");
            }
            async Task Choose(string id,string option)
            {
                await Until(()=>Node(id)?.SelectOptions.Any(choice=>choice.Id==option&&!choice.IsDisabled)==true);
                await Activate(id);
                var key="Widget."+id+".Option."+option;
                await Until(()=>Popup(key) is {IsLoaded:true,IsEnabled:true});
                var control=Popup(key)!;control.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});
                if(!control.Focus(FocusState.Keyboard))throw new InvalidOperationException("Recording choice could not receive native focus: "+key);
                await Until(()=>FocusId()==key);
                await Press(ControllerButton.A);
                await Until(()=>Popup(key) is null&&!surface!.HasTransientControl);
            }
            Control? Popup(string id)=>VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).SelectMany(popup=>Descendants(popup.Child))
                .OfType<Control>().FirstOrDefault(control=>AutomationProperties.GetAutomationId(control)==id);
            async Task StartOwned()
            {
                await OpenStart(); await Until(() => hostChoiceDialog?.IsOpenForValidation == true);
                var list = Descendants(hostChoiceDialog!).OfType<ListView>().Single(view => AutomationProperties.GetName(view) == "Available options");
                var row = list.Items.OfType<ListViewItem>().SingleOrDefault(item =>
                    AutomationProperties.GetName(item).Contains("WidgetRail synthetic capture target", StringComparison.Ordinal))
                    ?? throw new InvalidOperationException("The owned synthetic source is absent from the native chooser.");
                list.ScrollIntoView(row); await Until(() => row.IsLoaded && row.ActualHeight > 0);
                Check(row.Focus(FocusState.Keyboard), "Owned source row accepts native controller focus"); await Press(ControllerButton.A);
                await Until(() => hostChoiceDialog is null && ReplayStatusForHost is { Phase: CaptureReplayPhase.Recording, BufferedDurationSeconds: >= 2 });
            }
            async Task<MenuFlyoutItem> TrayCommand(string id)
            {
                await SelectAsync("capture-replay-fixture", false);
                radialRequested = false; RefreshRadialChooser(); FocusTray();
                await Until(() => !interactive && FocusedTrayWidget()?.Id == "capture-replay-fixture" &&
                    Tray.ContainerFromItem(catalogItems.Single(item => item.Id == "capture-replay-fixture")) is Control { IsLoaded: true });
                await Press(ControllerButton.Menu);
                await Until(() => trayMenu?.Items.Any(item => AutomationProperties.GetAutomationId(item) == id && item.IsLoaded) == true);
                return trayMenu!.Items.Single(item => AutomationProperties.GetAutomationId(item) == id);
            }
            static void Invoke(MenuFlyoutItem item) => ((IInvokeProvider)FrameworkElementAutomationPeer.CreatePeerForElement(item).GetPattern(PatternInterface.Invoke)).Invoke();
            async Task Activate(string id)
            {
                await Until(() => MainFocusEnabled && Node(id) is { IsDisabled: not true, IsBusy: not true });
                Control? Target()=>Descendants(surface!).OfType<Control>().FirstOrDefault(value=>AutomationProperties.GetAutomationId(value)=="Widget."+id);
                await Until(()=>Target() is {IsLoaded:true,IsEnabled:true,ActualWidth:>0,ActualHeight:>0});
                var control=Target()!;
                control.StartBringIntoView(new BringIntoViewOptions{AnimationDesired=false});
                if (!surface!.FocusHostTarget(id)) throw new InvalidOperationException("Control is not focusable: " + id);
                await Until(()=>FocusId()=="Widget."+id);
                ObserveActivation(id,"before");
                await Press(ControllerButton.A);
                ObserveActivation(id,"after");
            }
            void ObserveActivation(string id,string phase)
            {
                if(activationEvidence.Count>=40)return;
                var control=Descendants(surface!).OfType<Control>().FirstOrDefault(value=>AutomationProperties.GetAutomationId(value)=="Widget."+id);
                activationEvidence.Add(new{id,phase,focus=FocusId(),MainFocusEnabled,switching,startupPresentationPending,
                    transient=surface?.HasTransientControl,scope=surface?.CurrentBinding?.Frame.Authority.ActiveInputScopeId,
                    surfaceWidth=surface?.ActualWidth,surfaceHeight=surface?.ActualHeight,
                    bounds=control?.TransformToVisual(surface!).TransformBounds(new(0,0,control.ActualWidth,control.ActualHeight)),
                    popups=VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot).SelectMany(popup=>Descendants(popup.Child))
                        .OfType<Control>().Select(value=>AutomationProperties.GetAutomationId(value)).Where(value=>value.StartsWith("Widget.",StringComparison.Ordinal)).Take(12).ToArray()});
            }
            async Task Press(ControllerButton button)
            { Receive(new WidgetRail.OverlayPlatformClient.ControllerFrame { Connected = 1 }); await RouteButtonAsync(button, ControllerEventPhase.Pressed); await RouteButtonAsync(button, ControllerEventPhase.Released); }
            async Task Until(Func<bool> predicate, bool allowRetired = false)
            {
                var until = Environment.TickCount64 + 20000;
                while (!predicate()) { if (retired && !allowRetired || Environment.TickCount64 > until) throw new TimeoutException("Replay UI did not settle: " + step); await Task.Delay(25); }
            }
            void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
            ViewNode? Node(string id) => surface?.CurrentBinding is { } binding ? Nodes(binding.View.Root).FirstOrDefault(node => node.Id == id) : null;
            string? FocusId() => FocusManager.GetFocusedElement(XamlRoot) is DependencyObject element ? AutomationProperties.GetAutomationId(element) : null;
        };
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    { yield return node; for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); ++index) foreach (var child in Descendants(VisualTreeHelper.GetChild(node, index))) yield return child; }
    private static IEnumerable<ViewNode> Nodes(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Nodes));
}
