using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.WidgetProtocol;
using WidgetRail.RemoteMediaTransport;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    // Opt-in public test media only. No account, attachment URL, capture or microphone.
    private async Task PublicMediaStreamAsync(string output)
    {
        Exception? failure = null;
        uint width = 0, height = 0;
        double duration = 0, seekPosition = 0;
        long length = 0;
        await using var client = new PublicMediaClient();
        NativePublicMediaStream? stream = null;
        MediaSource? source = null;
        NativePublicMediaSource? securedSource = null;
        MediaPlayer? player = null;
        IRandomAccessStream? cursor = null;
        var video = new MediaPlayerElement { Width = 640, Height = 360, AreTransportControlsEnabled = false };
        host.Children.Clear(); host.Children.Add(status); host.Children.Add(video);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var token = deadline.Token;
            var artwork = await NativeArtworkDecoder.DecodeAsync(new(ReadOnlyMemory<byte>.Empty,
                new Uri("https://raw.githubusercontent.com/github/explore/main/topics/csharp/csharp.png"), IsPublicMedia: true),
                new(128, 128), ImageFit.Contain, token);
            Check(artwork is { Source.Width: > 0, Source.Height: > 0 } && artwork.Image.PixelWidth > 0,
                "Anonymous same-origin image transport reaches the native bounded bitmap decoder");
            var blocks = await PublicMediaBlockSource.OpenAsync(client,
                new Uri("https://media.w3.org/2010/05/sintel/trailer.mp4"),
                new PublicMediaPolicy(new[] { "https://media.w3.org" }), cancellationToken: token);
            stream = new NativePublicMediaStream(blocks, "video/mp4");
            length = stream.Length;
            cursor = stream.CreateView();
            var header = await cursor.ReadAsync(new Windows.Storage.Streams.Buffer(16), 16, InputStreamOptions.None).AsTask(token);
            Check(header.ToArray().AsSpan(4, 4).SequenceEqual("ftyp"u8), "Controlled transport supplies actual MP4 bytes");
            using (var clone = cursor.CloneStream())
            {
                Check(clone.Position == cursor.Position, "Native clone starts at the source cursor position");
                clone.Seek((ulong)length - 16);
                var tail = await clone.ReadAsync(new Windows.Storage.Streams.Buffer(16), 16, InputStreamOptions.None).AsTask(token);
                Check(tail.Length == 16 && cursor.Position == 16, "Tail seeking does not change another native cursor");
                var eof = await clone.ReadAsync(new Windows.Storage.Streams.Buffer(16), 16, InputStreamOptions.None).AsTask(token);
                Check(eof.Length == 0, "Native reader observes bounded end of file");
            }
            cursor.Seek(0);
            player = new MediaPlayer { AutoPlay = false, IsMuted = true };
            player.CommandManager.IsEnabled = false;
            player.SystemMediaTransportControls.IsEnabled = false;
            video.SetMediaPlayer(player);
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var failed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            player.MediaOpened += (_, _) => opened.TrySetResult();
            player.MediaFailed += (_, error) => failed.TrySetResult($"{error.Error}: {error.ExtendedErrorCode.HResult:X8}");
            securedSource = await NativePublicMediaSource.OpenAsync(new Uri("https://media.w3.org/2010/05/sintel/trailer.mp4"), token);
            source = securedSource.Source;
            player.Source = source;
            await UntilAsync(() => opened.Task.IsCompleted);
            width = player.PlaybackSession.NaturalVideoWidth;
            height = player.PlaybackSession.NaturalVideoHeight;
            duration = player.PlaybackSession.NaturalDuration.TotalSeconds;
            Check(width > 0 && height > 0 && duration > 10, "Native decoder opens checked stream with valid video dimensions and duration");
            player.Play();
            await UntilAsync(() => player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing && player.PlaybackSession.Position.TotalSeconds > .5);
            Check(true, "Native playback clock advances from the controlled stream");
            player.Pause();
            var target = duration * .7;
            player.PlaybackSession.Position = TimeSpan.FromSeconds(target);
            player.Play();
            await UntilAsync(() => player.PlaybackSession.Position.TotalSeconds >= target + .5);
            seekPosition = player.PlaybackSession.Position.TotalSeconds;
            Check(true, "Native video seeks beyond the initial buffered region and resumes playback");
            player.Pause();
            player.PlaybackSession.Position = TimeSpan.FromSeconds(1);
            player.Play();
            await UntilAsync(() => player.PlaybackSession.Position.TotalSeconds is >= 1.5 and < 5);
            Check(true, "Native video can seek backward and resume playback");
            if (Environment.GetCommandLineArgs().Contains("--public-media-visual-check"))
            {
                foreach (var position in new[] { 12, 35 })
                {
                    player.PlaybackSession.Position = TimeSpan.FromSeconds(position);
                    await UntilAsync(() => player.PlaybackSession.Position.TotalSeconds >= position + .5);
                    player.Pause();
                    status.Text = $"Public-media visual checkpoint: {position} seconds";
                    await File.WriteAllTextAsync(output + ".visual.json", JsonSerializer.Serialize(new { position }));
                    await Task.Delay(TimeSpan.FromSeconds(25), token);
                    player.Play();
                }
            }
            video.SetMediaPlayer(null);
            player.Source = null;
            player.Dispose(); player = null;
            await securedSource.DisposeAsync(); securedSource = null; source = null;
            await stream.DisposeAsync();
            Check(!cursor.CanRead, "Owner retirement closes native cursors after decoder teardown");
            try { stream.CreateView(); throw new InvalidOperationException("Retired stream accepted a cursor."); }
            catch (ObjectDisposedException) { Check(true, "Retired streams reject new native reader admission"); }
            await VerifyPublicMediaViewAsync(token);

            async Task UntilAsync(Func<bool> condition)
            {
                while (!condition())
                {
                    if (failed.Task.IsCompleted) throw new InvalidOperationException(await failed.Task);
                    await Task.Delay(20, token);
                }
            }
        }
        catch (Exception error) { failure = error; }
        finally
        {
            video.SetMediaPlayer(null); player?.Dispose();
            if (securedSource is not null) await securedSource.DisposeAsync();
            cursor?.Dispose();
            host.Children.Remove(video);
            if (stream is not null) await stream.DisposeAsync();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new
            {
                passed = failure is null, checks, width, height, duration, seekPosition, length,
                error = failure?.ToString(),
                scope = "Native source and shared viewer over checked HTTPS streams. Visible frame pixels and live Discord attachments are separate qualification gates."
            }));
        }
        if (failure is not null) throw failure;
    }

    private async Task VerifyPublicMediaViewAsync(CancellationToken token)
    {
        const string clip = "https://media.w3.org/2010/05/sintel/trailer.mp4";
        const string picture = "https://raw.githubusercontent.com/github/explore/main/topics/csharp/csharp.png";
        var view = new MediaPlayerView { Width = 640, Height = 360 };
        var resident = MediaPlayerView.ResidentPlayers;
        var current = true;
        host.Children.Add(view);
        try
        {
            var definition = new MediaPlayerDefinition(MediaPlayerSource.PublicFile(clip), new(AutoPlay: true, Muted: true));
            view.SetPreviewPlayback(true); view.SetActive(true, true);
            var held = new List<IDisposable>();
            try
            {
                for (var index = 0; index < 4; index++) held.Add(await NativePublicMediaTransport.AdmitDecodeAsync(token));
                view.Apply(_ => Task.FromResult(new WidgetRail.WidgetBridge.HostMediaPlayerSource(definition, clip)), definition, () => current, "public.fixture");
                await Wait(() => MediaPlayerView.ResidentPlayers == resident + 1);
                Check(!view.CanControl, "Queued public acquisition reserves its slot without advertising unloaded playback controls");
                view.Toggle();
                view.SetActive(false, false);
                await view.PlaybackRetirement.WaitAsync(token);
                Check(!view.CanControl && MediaPlayerView.ResidentPlayers == resident,
                    "Hiding while public acquisition is queued cancels the wait and releases its player reservation");
            }
            finally { foreach (var lease in held) lease.Dispose(); }
            view.SetActive(true, true);
            view.Apply(_ => Task.FromResult(new WidgetRail.WidgetBridge.HostMediaPlayerSource(definition, clip)), definition, () => current, "public.fixture");
            await Wait(() => view.IsPlaying && MediaPlayerView.ResidentPlayers == resident + 1);
            Check(true, "Shared media viewer plays the checked native source using one interactive slot");
            current = false;
            await Wait(() => MediaPlayerView.ResidentPlayers == resident);
            await view.PlaybackRetirement.WaitAsync(token);
            Check(!view.IsPlaying, "Withdrawing source authority retires checked playback and its reader lifetime");
            current = true;
            var imageDefinition = new MediaPlayerDefinition(MediaPlayerSource.PublicImage(picture), new());
            view.SetActive(true, true);
            view.Apply(_ => Task.FromResult(new WidgetRail.WidgetBridge.HostMediaPlayerSource(imageDefinition, picture, IsImage: true)),
                imageDefinition, () => current, "public.fixture");
            await Wait(() => view.IsImage && view.CanControl);
            Check(MediaPlayerView.ResidentPlayers == resident, "Public image replacement uses the shared viewer without a video slot");
            view.SetActive(false, false);
            await view.PlaybackRetirement.WaitAsync(token);
            Check(!view.CanControl && MediaPlayerView.ResidentPlayers == resident, "Hiding the preview releases the decoded public image and playback input");
        }
        finally
        {
            view.Dispose(); await view.PlaybackRetirement.WaitAsync(token); host.Children.Remove(view);
        }
    }
}
