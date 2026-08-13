using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GameBarAlternative.AvaloniaPrototype.Views;

public sealed partial class GameLauncherPage : UserControl, IPrototypeFocusPage
{
    private static readonly string[] Games =
    [
        "Disaster Crew — Grand Reopening Cooperative Test",
        "The Centrifuge: Long-Name Containment Fixture",
        "Cloud Harbor",
        "Signal Cartographers",
        "A Game Whose Artwork Has Not Been Downloaded Yet",
        "Night Shift Dispatch",
        "Orbital Pantry",
        "River City Constructors",
        "Circuit Garden",
        "Uncatalogued Windows Application",
        "Cooperative Museum After Hours",
        "Twelve Player Lobby Stress Fixture",
        "Lanternline",
        "Missing Box Art Adventure",
        "Local Multiplayer Workshop",
        "Prototype Library Final Entry",
    ];

    public GameLauncherPage()
    {
        AvaloniaXamlLoader.Load(this);
        var list = this.FindControl<StackPanel>("ApplicationList")!;
        for (var index = 0; index < Games.Length; index++)
        {
            var button = new Button
            {
                Content = $"{index + 1:00}   {Games[index]}",
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                MinHeight = 46,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            };
            AutomationProperties.SetAutomationId(button, $"launcher.game.{index + 1:00}");
            AutomationProperties.SetName(button, $"Open {Games[index]}");
            list.Children.Add(button);
        }
    }

    public ScrollViewer ApplicationScrollControl => this.FindControl<ScrollViewer>("ApplicationScroll")!;

    public Control InitialFocus => this.FindControl<Button>("PrimaryAction")!;

    public IReadOnlyList<Button> ApplicationButtons =>
        this.FindControl<StackPanel>("ApplicationList")!.Children.OfType<Button>().ToArray();
}
