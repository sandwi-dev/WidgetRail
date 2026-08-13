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

        if (focused is Slider slider && input is SemanticInput.Left or SemanticInput.Right)
        {
            var step = slider.SmallChange > 0 ? slider.SmallChange : 1;
            slider.Value = Math.Clamp(slider.Value + (input == SemanticInput.Left ? -step : step), slider.Minimum, slider.Maximum);
            return true;
        }

        if (input is SemanticInput.Up or SemanticInput.Down or SemanticInput.Left or SemanticInput.Right)
        {
            return FocusNavigator.Move(window, input switch
            {
                SemanticInput.Up => Key.Up,
                SemanticInput.Down => Key.Down,
                SemanticInput.Left => Key.Left,
                _ => Key.Right,
            });
        }

        if (input == SemanticInput.Activate && focused is Button button)
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return true;
        }

        if (input == SemanticInput.Back)
        {
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
