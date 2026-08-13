using Avalonia.Controls;
using Avalonia.Input;

namespace GameBarAlternative.AvaloniaPrototype.Views;

internal interface IPrototypeFocusPage
{
    Control InitialFocus { get; }
}

internal interface IPrototypeSemanticPage
{
    bool TryMoveSemantic(Control focused, NavigationDirection direction);

    bool TryActivateSemantic(Control focused);

    bool TryRestoreSemanticFocus(string automationId);
}
