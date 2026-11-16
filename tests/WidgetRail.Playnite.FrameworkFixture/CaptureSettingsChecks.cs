using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Playnite.SDK;
using WidgetRail.Playnite;

internal static class CaptureSettingsChecks
{
    private static Dictionary<string, object> Read(object? provider) => new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
        Encoding.UTF8.GetString(WireJson.Response(ProviderCaptureSettings.Read(provider)).Body));
    private static Dictionary<string, object> Patch(FakeProvider provider, params object[] values)
    {
        var edits = new Dictionary<string, object>();
        for (var index = 0; index < values.Length; index += 2) edits[(string)values[index]] = values[index + 1];
        return new Dictionary<string, object> { ["expectedRevision"] = Read(provider)["revision"], ["settings"] = edits };
    }
    internal static void Run(Action<bool, string> check, string root)
    {
        check((string)Read(null)["availability"] == "missing", "Capture settings missing provider is explicit");
        check((string)Read(new object())["availability"] == "unsupported", "Capture settings unsupported API fails closed");
        // The inherited Playnite SDK loader is public; persistence never relies on a private provider member.
        check(typeof(Playnite.SDK.Plugins.Plugin).GetMethods().Any(method => method.Name == "LoadPluginSettings" && method.IsGenericMethodDefinition), "Public Playnite settings readback API exists");
        var provider = new FakeProvider();
        var initial = Read(provider);
        check((string)initial["availability"] == "ready", "Capture settings supports compatible public provider");
        var patch = Patch(provider, "screenshotsEnabled", true, "screenshotResolution", "Half", "recordingSeconds", 20, "screenshotDirectory", root);
        ProviderCaptureSettings.Update(provider, patch);
        check(provider.Settings.Persisted.EnableUnlockScreenshots && provider.Saved.Persisted.EnableUnlockScreenshots &&
            provider.Saved.Persisted.ScreenshotResolution == Mode.Half && provider.Saved.Persisted.RecordingClipSeconds == 20 &&
            provider.Saved.Persisted.UnlockScreenshotDirectory == root && provider.Settings.Persisted.Unrelated == "retain",
            "Capture settings persists typed fields without changing unrelated settings");
        Reject(() => ProviderCaptureSettings.Update(provider, patch), "capture_settings_changed", check);
        provider.BeginEdit();
        Reject(() => ProviderCaptureSettings.Update(provider, Patch(provider, "screenshotsEnabled", false)), "capture_settings_editing", check);
        provider.CancelEdit();
        foreach (var invalid in new[] { Patch(provider, "recordingFps", 61), Patch(provider, "recordingSeconds", 4),
            Patch(provider, "recordingResolution", "99"), Patch(provider, "screenshotsEnabled", "true"), Patch(provider, "screenshotDirectory", "relative"),
            Patch(provider, "screenshotDirectory", Path.Combine(root, "missing")) })
            Reject(() => ProviderCaptureSettings.Update(provider, invalid), null, check);
        provider.FailVerification = true;
        Reject(() => ProviderCaptureSettings.Update(provider, Patch(provider, "screenshotsEnabled", false)), "capture_settings_invalid", check);
        check(provider.Settings.Persisted.EnableUnlockScreenshots && !provider.IsSettingsEditSessionActive, "Verification failure rolls back active settings transaction");
        provider.FailVerification = false;
        provider.FailSaves = 1;
        Reject(() => ProviderCaptureSettings.Update(provider, Patch(provider, "screenshotsEnabled", false)), "capture_settings_save_failed", check);
        check(provider.Settings.Persisted.EnableUnlockScreenshots && provider.Saved.Persisted.EnableUnlockScreenshots, "Failed commit restores both live and persisted capture settings");
        provider.DropSaves = 1;
        Reject(() => ProviderCaptureSettings.Update(provider, Patch(provider, "screenshotsEnabled", false)), "capture_settings_save_failed", check);
        check(provider.Settings.Persisted.EnableUnlockScreenshots && provider.Saved.Persisted.EnableUnlockScreenshots, "Silent persistence failure is detected by public readback and rolled back");
        provider.FailSaves = 2;
        Reject(() => ProviderCaptureSettings.Update(provider, Patch(provider, "screenshotsEnabled", false)), "capture_settings_rollback_failed", check);
        check(provider.Settings.Persisted.EnableUnlockScreenshots, "Rollback failure still restores live capture settings and reports failure");
    }
    private static void Reject(Action action, string? expected, Action<bool, string> check)
    {
        try { action(); } catch (ApiException error) { check(expected == null || error.Code == expected, "Capture settings rejects " + (expected ?? "invalid field")); return; }
        throw new InvalidOperationException("Capture settings mutation was not rejected.");
    }
    public enum Mode { Native, Half }
    public sealed class Values
    {
        public string Unrelated { get; set; } = "retain";
        public bool EnableUnlockScreenshots { get; set; }
        public bool UnlockScreenshotClean { get; set; } = true;
        public bool UnlockScreenshotWithToast { get; set; }
        public bool UnlockScreenshotFramed { get; set; }
        public double CaptureDelaySeconds { get; set; }
        public Mode ScreenshotResolution { get; set; }
        public string UnlockScreenshotDirectory { get; set; } = "";
        public bool EnableUnlockRecordings { get; set; }
        public bool UnlockRecordingClean { get; set; } = true;
        public bool UnlockRecordingWithToast { get; set; }
        public bool UnlockRecordingFramed { get; set; }
        public int RecordingClipSeconds { get; set; } = 10;
        public int RecordingFps { get; set; } = 30;
        public Mode RecordingResolution { get; set; }
        public Mode RecordingQuality { get; set; }
        public bool RecordingIncludeAudio { get; set; }
        public bool RecordingIncludeMicrophone { get; set; }
        public Mode RecordingAudioSource { get; set; }
        public string UnlockRecordingDirectory { get; set; } = "";
        public Values Clone() => (Values)MemberwiseClone();
    }
    public sealed class Settings { public Values Persisted { get; set; } = new Values(); public Settings Clone() => new Settings { Persisted = Persisted.Clone() }; }
    public sealed class FakeProvider : ISettings
    {
        public Settings Settings { get; set; } = new Settings();
        public Settings Saved { get; private set; } = new Settings();
        private Settings? prior;
        public bool IsSettingsEditSessionActive => prior != null;
        public bool FailVerification;
        public int FailSaves, DropSaves;
        public ISettings GetSettings(bool first) => this;
        public T LoadPluginSettings<T>() where T : class => (Saved.Clone() as T)!;
        public void BeginEdit() { prior = Settings.Clone(); }
        public void CancelEdit() { if (prior != null) Settings = prior; prior = null; }
        public bool VerifySettings(out List<string> errors) { errors = new List<string>(); if (FailVerification) errors.Add("fixture"); return !FailVerification; }
        public void EndEdit()
        {
            prior = null;
            if (FailSaves > 0) { FailSaves--; throw new IOException("Fixture persistence failure"); }
            if (DropSaves > 0) { DropSaves--; return; }
            Saved = Settings.Clone();
        }
    }
}
