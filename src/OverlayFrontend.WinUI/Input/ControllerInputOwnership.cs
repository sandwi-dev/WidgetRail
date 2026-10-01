namespace WidgetRail.OverlayFrontend.WinUI.Input;

/// <summary>Reading a controller does not grant permission to act on its input.</summary>
internal sealed class ControllerInputOwnership
{
    private bool admitted;

    internal (bool Deliver, bool Prime) Update(bool visible, bool foreground)
    {
        var next = visible && foreground;
        var prime = next && !admitted;
        admitted = next;
        return (next, prime);
    }

    internal void Reset() => admitted = false;
}
