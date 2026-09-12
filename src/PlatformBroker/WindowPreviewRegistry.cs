namespace WidgetRail.PlatformBroker;

/// <summary>Trusted provider-to-native-host data; never part of the widget protocol.</summary>
public sealed record NativeWindowPreviewTarget(
    string Handle, uint ProcessId, string ProcessCreated, string ClassName);

/// <summary>Host-private authority associated with one authenticated broker lifetime.</summary>
internal static class WindowPreviewRegistry
{
    private sealed record Entry(object Owner, BrokerWidgetIdentity Identity, NativeWindowPreviewTarget Target);
    private static readonly object Gate = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, object> Retired = new();
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);

    internal static void Replace(object owner, BrokerWidgetIdentity identity,
        IReadOnlyDictionary<string, NativeWindowPreviewTarget> targets)
    {
        lock (Gate)
        {
            if (Retired.TryGetValue(owner, out _)) return;
            RemoveOwner(owner);
            foreach (var pair in targets)
                if (Valid(pair.Value)) Entries.Add(pair.Key, new(owner, identity, pair.Value));
        }
    }

    private static bool Valid(NativeWindowPreviewTarget target)
    {
        static bool Hex(string? value) => value is { Length: > 0 and <= 16 } &&
            value.All(c => char.IsAsciiHexDigit(c)) &&
            ulong.TryParse(value, System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture, out var number) && number != 0;
        return target is not null && target.ProcessId != 0 && Hex(target.Handle) && Hex(target.ProcessCreated) &&
            target.ClassName is { Length: > 0 and <= 255 } && !target.ClassName.Any(char.IsControl);
    }

    internal static void Retire(object owner)
    {
        lock (Gate)
        {
            _ = Retired.GetValue(owner, _ => new object());
            RemoveOwner(owner);
        }
    }

    private static void RemoveOwner(object owner)
    {
        foreach (var key in Entries.Where(pair => ReferenceEquals(pair.Value.Owner, owner))
                     .Select(pair => pair.Key).ToArray())
            Entries.Remove(key);
    }

    internal static IReadOnlyList<string> ActiveIds(BrokerWidgetIdentity identity)
    {
        lock (Gate) return Entries.Where(pair => pair.Value.Identity == identity).Select(pair => pair.Key).ToArray();
    }

    internal static NativeWindowPreviewTarget? Resolve(BrokerWidgetIdentity identity, string windowId)
    {
        lock (Gate)
            return Entries.TryGetValue(windowId, out var entry) && entry.Identity == identity
                ? entry.Target : null;
    }
}
