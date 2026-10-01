namespace WidgetRail.FirstPartyWidgets.Settings;

/// <summary>
/// Immutable host implementation features supplied through the trusted Settings
/// worker launch. These describe available controls, never user preferences.
/// </summary>
public sealed record SettingsHostFeatures(bool ExclusiveControllerControl = true, bool HeldDpadScroll = true,
    bool StartupRegistration = true);
