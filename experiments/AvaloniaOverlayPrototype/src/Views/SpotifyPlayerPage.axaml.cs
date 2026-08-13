using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GameBarAlternative.AvaloniaPrototype.Views;

public sealed partial class SpotifyPlayerPage : UserControl, IPrototypeFocusPage
{
    public SpotifyPlayerPage() => AvaloniaXamlLoader.Load(this);

    public Control InitialFocus => this.FindControl<Button>("PrimaryAction")!;
}
