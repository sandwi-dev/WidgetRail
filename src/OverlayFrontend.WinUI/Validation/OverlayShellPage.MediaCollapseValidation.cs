using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.OverlayFrontend.WinUI.Validation;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed partial class OverlayShellPage
{
    internal void EnableMediaCollapseValidation(string output, string png, string mp4)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(output))!;
        var profile = Path.GetFullPath(options.SettingsRoot);
        if (!profile.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            profile == PlatformSettingsPaths.CreateDefault().RootDirectory)
            throw new InvalidOperationException("Media collapse validation requires an isolated profile under its result directory.");
        var started = false;
        Loaded += async (_, _) =>
        {
            if (started) return;
            started = true;
            var checks = new List<string>(); var trace = new List<string>();
            string? error = null;
            MediaPlayerView? media = null;
            var priorDiagnostic = WindowActivationDiagnostic;
            WindowActivationDiagnostic = text => { trace.Add(text); priorDiagnostic?.Invoke(text); };
            fullscreenLayer.TraceForValidation = text => trace.Add(text);
            GettingFocus += (_, args) => trace.Add($"focus from={FocusId(args.OldFocusedElement)} to={FocusId(args.NewFocusedElement)} state={args.FocusState} direction={args.Direction} open={fullscreenLayer.IsOpen} restoring={fullscreenLayer.IsRestoringFocus}");
            try
            {
                Directory.CreateDirectory(directory);
                if (!File.Exists(png))
                {
                    if (Path.GetDirectoryName(Path.GetFullPath(png)) != directory) throw new InvalidOperationException("Synthetic image must be in result directory.");
                    await File.WriteAllBytesAsync(png, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jWZkAAAAASUVORK5CYII="));
                }
                if (!File.Exists(mp4))
                {
                    if (Path.GetDirectoryName(Path.GetFullPath(mp4)) != directory) throw new InvalidOperationException("Synthetic video must be in result directory.");
                    var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(directory);
                    var file = await folder.CreateFileAsync(Path.GetFileName(mp4), Windows.Storage.CreationCollisionOption.FailIfExists);
                    var composition = new Windows.Media.Editing.MediaComposition();
                    composition.Clips.Add(Windows.Media.Editing.MediaClip.CreateFromColor(Microsoft.UI.Colors.CornflowerBlue, TimeSpan.FromSeconds(6)));
                    var encodingProfile = Windows.Media.MediaProperties.MediaEncodingProfile.CreateMp4(Windows.Media.MediaProperties.VideoEncodingQuality.Vga);
                    var failure = await composition.RenderToFileAsync(file, Windows.Media.Editing.MediaTrimmingPreference.Precise, encodingProfile);
                    if (failure != Windows.Media.Transcoding.TranscodeFailureReason.None) throw new InvalidOperationException("Synthetic video creation failed: " + failure);
                }
                if (startup is not null) await startup;
                await Wait(() => visible && foreground && !switching && surface is not null);
                await SelectAsync("settings", true);
                await Wait(() => interactive && !switching && activeWidget == "settings");
                var expectedWidget = activeWidget;
                surface!.Visibility = Visibility.Collapsed;
                media = new MediaPlayerView { Width = 700, Height = 500, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom, IsTabStop = true };
                WidgetSurfaces.Children.Add(media);
                media.SetActive(true, true);
                foreach (var motion in new[] { MotionPreference.Full, MotionPreference.Reduced })
                {
                    fullscreenLayer.ApplyMotion(Appearance with { Motion = motion }, animationsEnabled: true);
                    trace.Add("motion=" + motion);
                    foreach (var (path, isImage) in new[] { (png, true), (mp4, false) })
                    {
                        var definition = new MediaPlayerDefinition(MediaPlayerSource.LocalFile(path), new(Muted: true));
                        media.Apply(_ => Task.FromResult(new HostMediaPlayerSource(definition, new Uri(path).AbsoluteUri, isImage, 640, 360)), definition, () => true);
                        trace.Add("loading=" + (isImage ? "image" : "video"));
                        await Wait(() => media.CanControl && media.CollapseProbeButton.IsEnabled && media.CollapseProbeButton.IsLoaded && media.CollapseProbeButton.ActualWidth > 0);
                        await Task.Delay(100);
                        for (var iteration = 0; iteration < 4; iteration++)
                        {
                            var utilityOpens = trace.Count(value => value.StartsWith("Utility open ", StringComparison.Ordinal));
                            await Click(media.CollapseProbeButton);
                            await Wait(() => fullscreenLayer.IsOpen && !fullscreenLayer.IsTransitioning);
                            Check(utilityPanel is null && trace.Count(value => value.StartsWith("Utility open ", StringComparison.Ordinal)) == utilityOpens,
                                $"{(isImage ? "image" : "video")} {motion} expansion {iteration} never opens a utility during reparenting");
                            await Click(media.CollapseProbeButton);
                            await Wait(() => !fullscreenLayer.IsOpen && !fullscreenLayer.IsRestoringFocus);
                            await Task.Delay(150);
                            Check(utilityPanel is null && trace.Count(value => value.StartsWith("Utility open ", StringComparison.Ordinal)) == utilityOpens && activeWidget == expectedWidget && interactive,
                                $"{(isImage ? "image" : "video")} {motion} collapse {iteration} keeps widget ownership without opening utilities");
                            Check(ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), media.CollapseProbeButton),
                                $"{(isImage ? "image" : "video")} {motion} collapse {iteration} restores media control focus");
                        }
                    }
                }
            }
            catch (Exception failure) { error = failure.ToString(); }
            finally
            {
                fullscreenLayer.Close(restoreFocus: false);
                if (media is not null) { WidgetSurfaces.Children.Remove(media); media.Dispose(); }
                if (surface is not null) surface.Visibility = Visibility.Visible;
                fullscreenLayer.ApplyMotion(Appearance, animationsEnabled: true);
                WindowActivationDiagnostic = priorDiagnostic;
                fullscreenLayer.TraceForValidation = null;
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { passed = error is null, checks, trace, error }, new JsonSerializerOptions { WriteIndented = true }));
            }
            void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
            static async Task Wait(Func<bool> condition)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                while (!condition()) await Task.Delay(20, timeout.Token);
            }
            static string FocusId(DependencyObject? element) => element is null ? "none" : AutomationProperties.GetAutomationId(element) is { Length: > 0 } id ? id : element.GetType().Name;
            static Task Click(FrameworkElement target) => NativeContextProbeInput.LeftClickAsync(target, new(target.ActualWidth / 2, target.ActualHeight / 2));
        };
    }
}
