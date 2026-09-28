using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Host-owned font assets; widgets send semantic prompts, never font paths or private characters.</summary>
internal static class WidgetGlyphs
{
    private static readonly FontFamily Xbox = new("ms-appx:///Assets/Fonts/kenney_input_xbox_series.ttf#Kenney Input Xbox Series");
    private static readonly FontFamily PlayStation = new("ms-appx:///Assets/Fonts/kenney_input_playstation_series.ttf#Kenney Input PlayStation Series");
    private static readonly FontFamily Guide = new("ms-appx:///Assets/Fonts/promptfont.ttf#PromptFont");
    private static readonly FontFamily SystemIcons = new("Segoe Fluent Icons");

    // Same existing, licensed assets and character mapping as ControllerPrompt.h.
    internal static void Apply(FontIcon icon, ViewNode node, bool playStation)
    {
        icon.IsHitTestVisible = false;
        icon.MirroredWhenRightToLeft = false;
        if (node.Kind == ViewNodeKind.ControllerGlyph)
        {
            var prompt = node.ControllerPrompt ?? throw new InvalidDataException("Controller glyph has no prompt.");
            icon.FontFamily = playStation ? prompt == ControllerPrompt.Guide ? Guide : PlayStation : Xbox;
            icon.Glyph = char.ConvertFromUtf32(Character(prompt, playStation));
            AutomationProperties.SetName(icon, node.AccessibilityLabel ?? AccessibleName(prompt, playStation));
        }
        else
        {
            var glyph = node.Glyph ?? throw new InvalidDataException("Icon has no semantic fallback glyph.");
            icon.FontFamily = SystemIcons;
            icon.Glyph = char.ConvertFromUtf32(glyph switch
            {
                WidgetGlyph.Music => (int)Symbol.MusicInfo, WidgetGlyph.Play => (int)Symbol.Play,
                WidgetGlyph.Pause => (int)Symbol.Pause, WidgetGlyph.Previous => (int)Symbol.Previous,
                WidgetGlyph.Next => (int)Symbol.Next, WidgetGlyph.Refresh => (int)Symbol.Refresh,
                WidgetGlyph.Shuffle => (int)Symbol.Shuffle, WidgetGlyph.Like => (int)Symbol.Like,
                WidgetGlyph.Dislike => (int)Symbol.Dislike, WidgetGlyph.Repeat => (int)Symbol.RepeatAll,
                WidgetGlyph.RepeatOne => (int)Symbol.RepeatOne, WidgetGlyph.Settings => (int)Symbol.Setting,
                WidgetGlyph.Warning => (int)Symbol.Important, WidgetGlyph.Check => (int)Symbol.Accept,
                WidgetGlyph.Connection => (int)Symbol.Link, WidgetGlyph.Volume => (int)Symbol.Volume,
                WidgetGlyph.Muted => (int)Symbol.Mute, WidgetGlyph.Microphone => (int)Symbol.Microphone,
                WidgetGlyph.Wifi => 0xE701, WidgetGlyph.Ethernet => 0xE839,
                WidgetGlyph.Rewind => (int)Symbol.Back, WidgetGlyph.FastForward => (int)Symbol.Forward,
                WidgetGlyph.Fullscreen => (int)Symbol.FullScreen,
                _ => throw new ArgumentOutOfRangeException(nameof(node)),
            });
            AutomationProperties.SetName(icon, node.AccessibilityLabel ?? glyph.ToString());
        }
    }

    internal static int Character(ControllerPrompt prompt, bool playStation) => (prompt, playStation) switch
    {
        (ControllerPrompt.A, true) => 0xE04C, (ControllerPrompt.B, true) => 0xE042,
        (ControllerPrompt.X, true) => 0xE052, (ControllerPrompt.Y, true) => 0xE054,
        (ControllerPrompt.LeftBumper, true) => 0xE07B, (ControllerPrompt.RightBumper, true) => 0xE083,
        (ControllerPrompt.LeftTrigger, true) => 0xE07F, (ControllerPrompt.RightTrigger, true) => 0xE087,
        (ControllerPrompt.LeftStickPress, true) => 0xE04E, (ControllerPrompt.RightStickPress, true) => 0xE050,
        (ControllerPrompt.LeftStickMove, true) => 0xE064, (ControllerPrompt.RightStickMove, true) => 0xE06C,
        (ControllerPrompt.DPad, true) => 0xE055, (ControllerPrompt.DPadHorizontal, true) => 0xE05A,
        (ControllerPrompt.DPadVertical, true) => 0xE063, (ControllerPrompt.DPadUp, true) => 0xE061,
        (ControllerPrompt.DPadDown, true) => 0xE058, (ControllerPrompt.DPadLeft, true) => 0xE05C,
        (ControllerPrompt.DPadRight, true) => 0xE05F, (ControllerPrompt.View, true) => 0xE020,
        (ControllerPrompt.Menu, true) => 0xE026, (ControllerPrompt.Guide, true) => 0xE000,
        (ControllerPrompt.A, false) => 0xE005, (ControllerPrompt.B, false) => 0xE007,
        (ControllerPrompt.X, false) => 0xE01F, (ControllerPrompt.Y, false) => 0xE021,
        (ControllerPrompt.LeftBumper, false) => 0xE044, (ControllerPrompt.RightBumper, false) => 0xE04A,
        (ControllerPrompt.LeftTrigger, false) => 0xE048, (ControllerPrompt.RightTrigger, false) => 0xE04E,
        (ControllerPrompt.LeftStickPress, false) => 0xE046, (ControllerPrompt.RightStickPress, false) => 0xE04C,
        (ControllerPrompt.LeftStickMove, false) => 0xE04F, (ControllerPrompt.RightStickMove, false) => 0xE057,
        (ControllerPrompt.DPad, false) => 0xE022, (ControllerPrompt.DPadHorizontal, false) => 0xE027,
        (ControllerPrompt.DPadVertical, false) => 0xE038, (ControllerPrompt.DPadUp, false) => 0xE036,
        (ControllerPrompt.DPadDown, false) => 0xE025, (ControllerPrompt.DPadLeft, false) => 0xE029,
        (ControllerPrompt.DPadRight, false) => 0xE02C, (ControllerPrompt.View, false) => 0xE01D,
        (ControllerPrompt.Menu, false) => 0xE015, (ControllerPrompt.Guide, false) => 0xE042,
        _ => throw new ArgumentOutOfRangeException(nameof(prompt)),
    };

    internal static string AccessibleName(ControllerPrompt prompt, bool playStation) => (prompt, playStation) switch
    {
        (ControllerPrompt.A, true) => "Cross button", (ControllerPrompt.B, true) => "Circle button",
        (ControllerPrompt.X, true) => "Square button", (ControllerPrompt.Y, true) => "Triangle button",
        (ControllerPrompt.LeftBumper, true) => "L1 button", (ControllerPrompt.RightBumper, true) => "R1 button",
        (ControllerPrompt.LeftTrigger, true) => "L2 trigger", (ControllerPrompt.RightTrigger, true) => "R2 trigger",
        (ControllerPrompt.View, true) => "Create button", (ControllerPrompt.Menu, true) => "Options button",
        (ControllerPrompt.Guide, true) => "PS button",
        (ControllerPrompt.A, _) => "A button", (ControllerPrompt.B, _) => "B button",
        (ControllerPrompt.X, _) => "X button", (ControllerPrompt.Y, _) => "Y button",
        (ControllerPrompt.LeftBumper, _) => "Left bumper", (ControllerPrompt.RightBumper, _) => "Right bumper",
        (ControllerPrompt.LeftTrigger, _) => "Left trigger", (ControllerPrompt.RightTrigger, _) => "Right trigger",
        (ControllerPrompt.LeftStickPress, _) => "Press left stick", (ControllerPrompt.RightStickPress, _) => "Press right stick",
        (ControllerPrompt.LeftStickMove, _) => "Move left stick", (ControllerPrompt.RightStickMove, _) => "Move right stick",
        (ControllerPrompt.DPad, _) => "Directional pad", (ControllerPrompt.DPadHorizontal, _) => "Directional pad left or right",
        (ControllerPrompt.DPadVertical, _) => "Directional pad up or down", (ControllerPrompt.DPadUp, _) => "Directional pad up",
        (ControllerPrompt.DPadDown, _) => "Directional pad down", (ControllerPrompt.DPadLeft, _) => "Directional pad left",
        (ControllerPrompt.DPadRight, _) => "Directional pad right", (ControllerPrompt.View, _) => "View button",
        (ControllerPrompt.Menu, _) => "Menu button", (ControllerPrompt.Guide, _) => "Guide button",
        _ => throw new ArgumentOutOfRangeException(nameof(prompt)),
    };
}
