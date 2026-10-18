using System.Text.Json;

namespace WidgetRail.Samples.PobCompanion;

/// <summary>Features supplied by this engine snapshot, never inferred from the game number.</summary>
internal sealed record PobEngineCapabilities(bool CanCreateLoadout, bool SkillWeaponSets, bool CanEditNodeNotes, bool WeaponTreeModes, bool TimelessFinder)
{
    internal static readonly PobEngineCapabilities None = new(false, false, false, false, false);

    internal static PobEngineCapabilities Read(JsonElement state)
    {
        if (state.ValueKind == JsonValueKind.Undefined) return None;
        if (state.ValueKind != JsonValueKind.Object) throw new IOException("PoB returned an invalid capability snapshot.");
        if (!state.TryGetProperty("capabilities", out var capabilities)) return None; // Operation-only responses have no build capabilities.
        if (capabilities.ValueKind != JsonValueKind.Object) throw new IOException("PoB returned invalid engine capabilities.");
        bool Flag(string name)
        {
            if (!capabilities.TryGetProperty(name, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new IOException("PoB returned incomplete engine capabilities. Update the widget and its engine together.");
            return value.GetBoolean();
        }
        return new(Flag("canCreateLoadout"), Flag("skillWeaponSets"), Flag("canEditNodeNotes"), Flag("weaponTreeModes"), Flag("timelessFinder"));
    }
}
