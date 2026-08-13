using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Threading;

namespace GameBarAlternative.AvaloniaPrototype.Navigation;

public enum TransitionPhase
{
    Start,
    Midpoint,
    Completion,
}

public sealed class PageTransitionPresenter : TransitioningContentControl, IDisposable
{
    private CancellationTokenSource? transition;
    private Control? admittedPage;

    public PageTransitionPresenter()
    {
        PageTransition = new CrossFade(TimeSpan.FromMilliseconds(160));
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
    }

    public Control? AdmittedPage => admittedPage;

    public async Task PresentAsync(
        Control destination,
        Action<TransitionPhase>? sample = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        transition?.Cancel();
        transition?.Dispose();
        transition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = transition.Token;

        Content = destination;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        sample?.Invoke(TransitionPhase.Start);

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(80), token);
            sample?.Invoke(TransitionPhase.Midpoint);
            await Task.Delay(TimeSpan.FromMilliseconds(80), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }

        admittedPage = destination;
        sample?.Invoke(TransitionPhase.Completion);
    }

    public void CancelTransition() => transition?.Cancel();

    public void Dispose()
    {
        transition?.Cancel();
        transition?.Dispose();
        transition = null;
    }
}
