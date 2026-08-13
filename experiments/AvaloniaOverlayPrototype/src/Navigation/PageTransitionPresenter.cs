using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Threading;

namespace GameBarAlternative.AvaloniaPrototype.Navigation;

public sealed class PageTransitionPresenter : Grid, IDisposable
{
    private CancellationTokenSource? transition;

    public Control? AdmittedPage { get; private set; }

    public async Task PresentAsync(Control destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        transition?.Cancel();
        transition?.Dispose();
        transition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = transition.Token;

        destination.Opacity = 0;
        destination.Transitions =
        [
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(140),
            },
        ];

        Children.Add(destination);
        await Dispatcher.UIThread.InvokeAsync(() => destination.Opacity = 1);

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(160), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Children.Remove(destination);
            return;
        }

        if (AdmittedPage is { } previous)
        {
            Children.Remove(previous);
        }

        AdmittedPage = destination;
    }

    public void CancelTransition() => transition?.Cancel();

    public void Dispose()
    {
        transition?.Cancel();
        transition?.Dispose();
        transition = null;
    }
}
