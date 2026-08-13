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

public enum PresentationOutcome
{
    Admitted,
    Superseded,
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

    public bool ReducedMotion { get; set; }

    public async Task<PresentationOutcome> PresentAsync(
        Control destination,
        Action<TransitionPhase>? sample = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        transition?.Cancel();
        var ownedTransition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        transition = ownedTransition;
        var token = ownedTransition.Token;

        Content = destination;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        sample?.Invoke(TransitionPhase.Start);

        if (ReducedMotion)
        {
            admittedPage = destination;
            sample?.Invoke(TransitionPhase.Midpoint);
            sample?.Invoke(TransitionPhase.Completion);
            Complete(ownedTransition);
            return PresentationOutcome.Admitted;
        }

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(80), token);
            sample?.Invoke(TransitionPhase.Midpoint);
            await Task.Delay(TimeSpan.FromMilliseconds(80), token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Complete(ownedTransition);
            return PresentationOutcome.Superseded;
        }

        if (token.IsCancellationRequested || !ReferenceEquals(Content, destination))
        {
            Complete(ownedTransition);
            return PresentationOutcome.Superseded;
        }
        admittedPage = destination;
        sample?.Invoke(TransitionPhase.Completion);
        Complete(ownedTransition);
        return PresentationOutcome.Admitted;
    }

    public Control? ReplaceWithoutTransition(Control destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        transition?.Cancel();
        var prior = admittedPage;
        var configuredTransition = PageTransition;
        PageTransition = null;
        Content = destination;
        admittedPage = destination;
        PageTransition = configuredTransition;
        return prior;
    }

    public bool IsExactlyAdmitted(Control destination) =>
        ReferenceEquals(admittedPage, destination) && ReferenceEquals(Content, destination);

    public void CancelTransition() => transition?.Cancel();

    public void Dispose()
    {
        transition?.Cancel();
        transition?.Dispose();
        transition = null;
        admittedPage = null;
        Content = null;
    }

    private void Complete(CancellationTokenSource ownedTransition)
    {
        if (ReferenceEquals(transition, ownedTransition)) transition = null;
        ownedTransition.Dispose();
    }
}
