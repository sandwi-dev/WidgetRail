using Microsoft.UI.Xaml.Data;

namespace WidgetRail.OverlayFrontend.WinUI.Media;

internal sealed partial class MediaPlayerView
{
    private readonly Editing.CaptureEditAcceleration seekAcceleration = new();
    private long feedbackUntil;

    private void SeekWithRepeat(int direction, bool repeated)
    {
        var fast = Math.Clamp(Duration / 100, SeekStep, 30);
        var distance = seekAcceleration.Step(direction, repeated, Environment.TickCount64, SeekStep, fast);
        Seek(direction * distance);
    }

    private void ShowMediaFeedback(string message)
    {
        feedbackUntil = Environment.TickCount64 + 1100;
        time.Text = message;
    }

    private sealed partial class PlaybackTimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language) =>
            value is double seconds && double.IsFinite(seconds) ? FormatTime(Math.Max(0, seconds)) : string.Empty;
        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            throw new NotSupportedException();
    }
}
