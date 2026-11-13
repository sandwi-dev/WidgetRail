using System.Text.Json.Serialization;

namespace WidgetRail.PlatformSettings;

[Flags]
public enum OverlayShortcutModifiers { None = 0, Alt = 1, Control = 2, Shift = 4, Windows = 8 }

/// <summary>A single Windows virtual key and modifiers; registration remains host-owned.</summary>
public sealed record OverlayShortcutSettings
{
    public bool Enabled { get; init; }
    public int Key { get; init; } = 0x70;
    public OverlayShortcutModifiers Modifiers { get; init; }
    public static OverlayShortcutSettings Default { get; } = new();

    public bool IsWellFormed() => ((int)Modifiers & ~15) == 0 && IsPrimaryKey(Key);
    public static bool IsPrimaryKey(int key) => key is
        0x08 or 0x09 or 0x0C or 0x0D or 0x13 or 0x14 or 0x1B or
        >= 0x20 and <= 0x28 or 0x2C or 0x2D or 0x2E or
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or 0x5D or
        >= 0x60 and <= 0x87 or 0x90 or 0x91 or >= 0xA6 and <= 0xB7 or
        >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDF or 0xE2;

    [JsonIgnore]
    public string DisplayName => string.Join(" + ", new[]
    {
        Modifiers.HasFlag(OverlayShortcutModifiers.Control) ? "Ctrl" : null,
        Modifiers.HasFlag(OverlayShortcutModifiers.Alt) ? "Alt" : null,
        Modifiers.HasFlag(OverlayShortcutModifiers.Shift) ? "Shift" : null,
        Modifiers.HasFlag(OverlayShortcutModifiers.Windows) ? "Win" : null,
        KeyName(Key),
    }.Where(value => value is not null));

    private static string KeyName(int key) => key switch
    {
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)key).ToString(),
        >= 0x70 and <= 0x87 => "F" + (key - 0x6F),
        >= 0x60 and <= 0x69 => "Num " + (key - 0x60),
        0x08 => "Backspace", 0x09 => "Tab", 0x0C => "Clear", 0x0D => "Enter",
        0x13 => "Pause", 0x14 => "Caps Lock", 0x1B => "Escape", 0x20 => "Space",
        0x21 => "Page Up", 0x22 => "Page Down", 0x23 => "End", 0x24 => "Home",
        0x25 => "Left", 0x26 => "Up", 0x27 => "Right", 0x28 => "Down",
        0x2C => "Print Screen", 0x2D => "Insert", 0x2E => "Delete", 0x5D => "Menu",
        0x6A => "Num *", 0x6B => "Num +", 0x6C => "Separator", 0x6D => "Num -",
        0x6E => "Num .", 0x6F => "Num /", 0x90 => "Num Lock", 0x91 => "Scroll Lock",
        0xAD => "Volume Mute", 0xAE => "Volume Down", 0xAF => "Volume Up",
        0xB0 => "Next Track", 0xB1 => "Previous Track", 0xB2 => "Media Stop", 0xB3 => "Play/Pause",
        _ => $"Key 0x{key:X2}",
    };
}
