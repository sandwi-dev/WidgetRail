using System.Net;
using System.Net.Http;
using System.Text;
using WidgetRail.RemoteMediaTransport;
using Windows.Media.Playback;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class NativeDashMediaSource
{
    internal static async Task VerifySegmentCancellationAsync()
    {
        var handler = new DelayedDashResponse();
        await using var downloads = new PublicMediaClient(handler, new());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var source = await OpenAsync(new("https://example.org/master.mpd"), downloads, deadline.Token);
        using var player = new MediaPlayer { IsMuted = true };
        player.CommandManager.IsEnabled = false;
        player.SystemMediaTransportControls.IsEnabled = false;
        try
        {
            player.Source = source.Source;
            player.Play();
            await handler.WaitingForBytes.Task.WaitAsync(deadline.Token);
        }
        finally
        {
            await source.DisposeAsync().AsTask().WaitAsync(deadline.Token);
            player.Source = null;
        }
        if (!handler.Canceled.Task.IsCompletedSuccessfully || source.pending != 0)
            throw new InvalidOperationException("The interrupted DASH segment did not drain.");
    }

    private sealed class DelayedDashResponse : HttpMessageHandler
    {
        internal TaskCompletionSource WaitingForBytes { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            const string manifest = """
                <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="PT6S">
                <Period><AdaptationSet contentType="video"><Representation id="0" mimeType="video/mp4"
                    codecs="avc1.640029" bandwidth="1000000" width="640" height="360">
                    <SegmentTemplate timescale="1" duration="3" initialization="init.m4s" media="seg-$Number$.m4s" startNumber="1"/>
                </Representation></AdaptationSet></Period></MPD>
                """;
            HttpContent content = request.RequestUri!.AbsolutePath == "/master.mpd"
                ? new StringContent(manifest, Encoding.UTF8, "application/dash+xml") : new StreamContent(new DelayedSegment(this));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class DelayedSegment(DelayedDashResponse owner) : MemoryStream(new byte[32])
    {
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (Position == 0) return await base.ReadAsync(buffer[..1], token);
            owner.WaitingForBytes.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, token); return 0; }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            { owner.Canceled.TrySetResult(); throw; }
        }
    }
}
