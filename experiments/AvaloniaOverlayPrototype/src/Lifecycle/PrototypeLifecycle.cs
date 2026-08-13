namespace GameBarAlternative.AvaloniaPrototype.Lifecycle;

public enum PrototypeVisibility
{
    Hidden,
    Visible,
}

public sealed class PrototypeLifecycle
{
    private readonly object gate = new();
    private CancellationTokenSource hiddenCancellation = new();

    public PrototypeLifecycle() => hiddenCancellation.Cancel();

    public PrototypeVisibility Visibility { get; private set; } = PrototypeVisibility.Hidden;

    public event EventHandler<PrototypeVisibility>? VisibilityChanged;

    public CancellationToken VisibleLifetime
    {
        get
        {
            lock (gate)
            {
                return hiddenCancellation.Token;
            }
        }
    }

    public void Show()
    {
        lock (gate)
        {
            if (Visibility == PrototypeVisibility.Visible)
            {
                return;
            }

            hiddenCancellation.Dispose();
            hiddenCancellation = new CancellationTokenSource();
            Visibility = PrototypeVisibility.Visible;
        }

        VisibilityChanged?.Invoke(this, PrototypeVisibility.Visible);
    }

    public void Hide()
    {
        lock (gate)
        {
            if (Visibility == PrototypeVisibility.Hidden)
            {
                return;
            }

            Visibility = PrototypeVisibility.Hidden;
            hiddenCancellation.Cancel();
        }

        VisibilityChanged?.Invoke(this, PrototypeVisibility.Hidden);
    }

}
