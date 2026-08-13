using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GameBarAlternative.AvaloniaPrototype.ViewModels;

namespace GameBarAlternative.AvaloniaPrototype.Views;

public sealed partial class SpotifyPlayerPage : UserControl, IPrototypeFocusPage
{
    public SpotifyPlayerPage() : this(new SpotifyPlayerPageViewModel())
    {
    }

    internal SpotifyPlayerPage(SpotifyPlayerPageViewModel viewModel)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
    }

    public Control InitialFocus => this.FindControl<Button>("PrimaryAction")!;
}
