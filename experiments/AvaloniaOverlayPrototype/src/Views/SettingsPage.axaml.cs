using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GameBarAlternative.AvaloniaPrototype.ViewModels;

namespace GameBarAlternative.AvaloniaPrototype.Views;

public sealed partial class SettingsPage : UserControl, IPrototypeFocusPage
{
    public SettingsPage() : this(new SettingsPageViewModel())
    {
    }

    internal SettingsPage(SettingsPageViewModel viewModel)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
    }

    public Control InitialFocus => this.FindControl<Button>("PrimaryAction")!;
}
