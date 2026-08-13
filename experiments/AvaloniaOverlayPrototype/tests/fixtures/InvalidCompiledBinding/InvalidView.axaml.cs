using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GameBarAlternative.AvaloniaPrototype.InvalidBindingFixture;

public sealed partial class InvalidView : UserControl
{
    public InvalidView() => AvaloniaXamlLoader.Load(this);
}
