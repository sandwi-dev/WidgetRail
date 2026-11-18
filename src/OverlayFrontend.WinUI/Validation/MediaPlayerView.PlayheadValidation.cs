using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using WidgetRail.OverlayFrontend.WinUI.Input;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    internal async Task ValidateEditorPlayheadAsync(Func<Task> beginPlayback, Func<bool> rangeUnchanged,
        string evidencePath, List<string> checks, bool selectionEnd = true)
    {
        var ticks = 0;
        void Count(DispatcherQueueTimer sender, object args) => ++ticks;
        timer.Tick += Count;
        try
        {
            var deadline = Environment.TickCount64 + 5000;
            while (player?.PlaybackSession.CanSeek != true || position.ActualWidth <= 0)
            { if (Environment.TickCount64 > deadline) throw new TimeoutException("Trim preview timeline not ready."); await Task.Delay(20); }
            var before = player.PlaybackSession.Position.TotalSeconds;
            await beginPlayback();
            deadline = Environment.TickCount64 + 5000;
            while (!IsPlaying) { if (Environment.TickCount64 > deadline) throw new TimeoutException("Trim preview did not play."); await Task.Delay(20); }
            var started = player!.PlaybackSession.Position.TotalSeconds;
            var sliderStarted = position.Value;
            var thumbStarted = ThumbX();
            await Task.Delay(1200);
            var current = player.PlaybackSession.Position.TotalSeconds;
            var thumbCurrent = ThumbX();
            await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(new
            {
                before, started, sliderStarted, current, slider = position.Value, thumbStarted, thumbCurrent, ticks, timer.IsRunning, timer.IsRepeating,
                active, input, IsLoaded, disposed, retainedForPageExit, cancelled = lifetime.IsCancellationRequested,
                pointerCapture = PointerCaptureLifetime.HasCaptureWithin(position), playing = IsPlaying,
                rangeUnchanged = rangeUnchanged(), text = time.Text,
            }));
            Check(IsPlaying && current > started + .7, "Trim preview native playback advances while playing");
            Check(position.Value > sliderStarted + .5 && Math.Abs(current - position.Value) < .55,
                "Visible playback head advances with playback before Pause");
            Check(thumbCurrent > thumbStarted + 2, "Rendered playback thumb advances before Pause");
            Check(rangeUnchanged(), "Playback progress does not move trim Start or End");
            if (!selectionEnd) Toggle();
            deadline = Environment.TickCount64 + 6000;
            while (IsPlaying) { if (Environment.TickCount64 > deadline) throw new TimeoutException("Trim selection did not stop at its end."); await Task.Delay(25); }
            Check(rangeUnchanged() && Math.Abs(player.PlaybackSession.Position.TotalSeconds - position.Value) < .55,
                selectionEnd ? "Selection end pauses playback with a current playhead and unchanged trim boundaries" :
                    "Player Pause keeps a current playhead and unchanged trim boundaries");
        }
        finally { timer.Tick -= Count; }
        double ThumbX()
        {
            var pending = new Stack<DependencyObject>(); pending.Push(position);
            while (pending.TryPop(out var element))
            {
                if (element is Thumb { IsLoaded: true, ActualWidth: > 0 } thumb)
                    return thumb.TransformToVisual(position).TransformPoint(new()).X;
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); ++i) pending.Push(VisualTreeHelper.GetChild(element, i));
            }
            throw new InvalidOperationException("Native playback thumb is not realized.");
        }
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
    }
}
