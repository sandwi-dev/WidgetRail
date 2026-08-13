using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GameBarAlternative.AvaloniaPrototype.Navigation;
using GameBarAlternative.AvaloniaPrototype.Views;

namespace GameBarAlternative.AvaloniaPrototype.Input;

internal sealed class SemanticInputRouter(MainWindow window)
{
    private static readonly TimeSpan CrossSourceDuplicateWindow = TimeSpan.FromMilliseconds(75);
    private SemanticInput? lastInput;
    private SemanticInputSource lastSource;
    private DateTimeOffset lastDispatchAt;

    public bool Route(SemanticInput input, SemanticInputSource source)
    {
        var now = DateTimeOffset.UtcNow;
        if (input is SemanticInput.Activate or SemanticInput.Back &&
            input == lastInput && source != lastSource && now - lastDispatchAt <= CrossSourceDuplicateWindow)
        {
            return true;
        }

        lastInput = input;
        lastSource = source;
        lastDispatchAt = now;
        var focused = window.FocusManager?.GetFocusedElement() as Control;

        if (input is SemanticInput.Left or SemanticInput.Right &&
            window.ShellView.TryCycleTray(focused, input == SemanticInput.Left ? -1 : 1))
        {
            return true;
        }

        if (input is SemanticInput.Down or SemanticInput.Activate &&
            window.ShellView.TryEnterContent(focused))
        {
            return true;
        }

        if (focused is Slider slider && input is SemanticInput.Left or SemanticInput.Right)
        {
            var step = slider.SmallChange > 0 ? slider.SmallChange : 1;
            slider.Value = Math.Clamp(slider.Value + (input == SemanticInput.Left ? -step : step), slider.Minimum, slider.Maximum);
            return true;
        }

        if (input is SemanticInput.Up or SemanticInput.Down or SemanticInput.Left or SemanticInput.Right)
        {
            return window.ShellView.TryMoveSpatial(focused, input switch
            {
                SemanticInput.Up => NavigationDirection.Up,
                SemanticInput.Down => NavigationDirection.Down,
                SemanticInput.Left => NavigationDirection.Left,
                _ => NavigationDirection.Right,
            });
        }

        if (input == SemanticInput.Activate && window.ShellView.TryActivateFocused(focused))
        {
            return true;
        }

        if (input == SemanticInput.Activate && focused is Button button)
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return true;
        }

        if (input == SemanticInput.Back)
        {
            if (window.ShellView.RestoreSelectedTrayFocus(focused))
            {
                return true;
            }

            _ = window.ShellView.BackAsync();
            return true;
        }

        return false;
    }

    public void ResetDuplicateState()
    {
        lastInput = null;
        lastDispatchAt = default;
    }
}
