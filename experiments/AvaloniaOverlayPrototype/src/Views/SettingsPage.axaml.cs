using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GameBarAlternative.AvaloniaPrototype.Views;

public sealed partial class SettingsPage : UserControl, IPrototypeFocusPage
{
    public SettingsPage() => AvaloniaXamlLoader.Load(this);

    public Control InitialFocus => this.FindControl<Button>("PrimaryAction")!;
}
