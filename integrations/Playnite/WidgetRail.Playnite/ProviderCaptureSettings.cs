using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Playnite.SDK;

namespace WidgetRail.Playnite;

/// <summary>Bounded adapter over the provider's public settings transaction; no config-file writes.</summary>
internal static class ProviderCaptureSettings
{
    private sealed class Field
    {
        internal readonly string Wire, Property, Kind;
        internal Field(string wire, string property, string kind) { Wire = wire; Property = property; Kind = kind; }
    }
    private static readonly Field[] Fields =
    {
        new Field("screenshotsEnabled", "EnableUnlockScreenshots", "bool"),
        new Field("screenshotClean", "UnlockScreenshotClean", "bool"),
        new Field("screenshotWithToast", "UnlockScreenshotWithToast", "bool"),
        new Field("screenshotFramed", "UnlockScreenshotFramed", "bool"),
        new Field("screenshotDelaySeconds", "CaptureDelaySeconds", "delay"),
        new Field("screenshotResolution", "ScreenshotResolution", "enum"),
        new Field("screenshotDirectory", "UnlockScreenshotDirectory", "path"),
        new Field("recordingsEnabled", "EnableUnlockRecordings", "bool"),
        new Field("recordingClean", "UnlockRecordingClean", "bool"),
        new Field("recordingWithToast", "UnlockRecordingWithToast", "bool"),
        new Field("recordingFramed", "UnlockRecordingFramed", "bool"),
        new Field("recordingSeconds", "RecordingClipSeconds", "seconds"),
        new Field("recordingFps", "RecordingFps", "fps"),
        new Field("recordingResolution", "RecordingResolution", "enum"),
        new Field("recordingQuality", "RecordingQuality", "enum"),
        new Field("recordingAudio", "RecordingIncludeAudio", "bool"),
        new Field("recordingMicrophone", "RecordingIncludeMicrophone", "bool"),
        new Field("recordingAudioSource", "RecordingAudioSource", "enum"),
        new Field("recordingDirectory", "UnlockRecordingDirectory", "path"),
    };
    private static object? Get(object owner, string name) => owner.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)?.GetValue(owner, null);
    private static object Persisted(object settings) => Get(settings, "Persisted") ?? throw new ApiException(409, "capture_settings_unsupported");
    private static PropertyInfo Property(object persisted, Field field)
    {
        var property = persisted.GetType().GetProperty(field.Property, BindingFlags.Instance | BindingFlags.Public);
        if (property?.GetMethod?.IsPublic != true || property.SetMethod?.IsPublic != true || property.GetIndexParameters().Length != 0 ||
            (field.Kind == "bool" && property.PropertyType != typeof(bool)) ||
            (field.Kind == "enum" && !property.PropertyType.IsEnum) ||
            (field.Kind == "path" && property.PropertyType != typeof(string)) ||
            (field.Kind == "delay" && property.PropertyType != typeof(double)) ||
            ((field.Kind == "seconds" || field.Kind == "fps") && property.PropertyType != typeof(int)))
            throw new ApiException(409, "capture_settings_unsupported");
        return property;
    }
    private static Dictionary<string, object> Project(object persisted) => Fields.ToDictionary(field => field.Wire, field =>
    {
        var value = Property(persisted, field).GetValue(persisted, null);
        return field.Kind == "enum" ? value!.ToString()! : field.Kind == "path" ? value as string ?? "" : value!;
    }, StringComparer.Ordinal);
    private static string Revision(Dictionary<string, object> settings)
    {
        var text = string.Join("\n", Fields.Select(field => field.Wire + ":" + Convert.ToString(settings[field.Wire], CultureInfo.InvariantCulture)));
        using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
    }
    private static MethodInfo Loader(object provider, Type settingsType)
    {
        var method = provider.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(item =>
            item.Name == "LoadPluginSettings" && item.IsGenericMethodDefinition && item.GetGenericArguments().Length == 1 && item.GetParameters().Length == 0);
        if (method == null) throw new ApiException(409, "capture_settings_unsupported");
        return method.MakeGenericMethod(settingsType);
    }
    private static bool Editing(object provider) => Get(provider, "IsSettingsEditSessionActive") is bool editing
        ? editing : throw new ApiException(409, "capture_settings_unsupported");
    internal static object Read(object? provider)
    {
        if (provider == null) return new { availability = "missing" };
        try
        {
            var settings = Get(provider, "Settings") ?? throw new ApiException(409, "capture_settings_unsupported");
            var persisted = Persisted(settings); var values = Project(persisted);
            _ = Loader(provider, settings.GetType());
            var screenshotRoot = (string)values["screenshotDirectory"]; var recordingRoot = (string)values["recordingDirectory"];
            return new { availability = "ready", revision = Revision(values), editing = Editing(provider), settings = values,
                options = Fields.Where(field => field.Kind == "enum").ToDictionary(field => field.Wire,
                    field => Enum.GetNames(Property(persisted, field).PropertyType)),
                roots = new { screenshots = screenshotRoot, recordings = string.IsNullOrWhiteSpace(recordingRoot) ? screenshotRoot : recordingRoot } };
        }
        catch (Exception error) when (error is ApiException || error is TargetInvocationException || error is ArgumentException || error is InvalidOperationException)
        { return new { availability = "unsupported" }; }
    }
    internal static object Update(object? provider, Dictionary<string, object> body)
    {
        if (body.Count != 2 || !body.TryGetValue("expectedRevision", out var expected) || !(expected is string expectedRevision) || expectedRevision.Length != 64 ||
            !body.TryGetValue("settings", out var patchValue) || !(patchValue is Dictionary<string, object> patch) || patch.Count == 0 || patch.Count > Fields.Length ||
            patch.Keys.Any(key => !Fields.Any(field => field.Wire == key))) throw new ApiException(400, "invalid_capture_settings");
        if (provider == null) throw new ApiException(409, "capture_settings_missing");
        var settings = Get(provider, "Settings") ?? throw new ApiException(409, "capture_settings_unsupported");
        var persisted = Persisted(settings); var before = Project(persisted);
        if (Editing(provider)) throw new ApiException(409, "capture_settings_editing");
        if (Revision(before) != expectedRevision) throw new ApiException(409, "capture_settings_changed");
        var loader = Loader(provider, settings.GetType());
        var getSettings = provider.GetType().GetMethod("GetSettings", new[] { typeof(bool) });
        if (!(getSettings?.Invoke(provider, new object[] { false }) is ISettings editor)) throw new ApiException(409, "capture_settings_unsupported");
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var field in Fields) if (patch.TryGetValue(field.Wire, out var value)) values.Add(field.Wire, ConvertValue(persisted, field, value));
        var began = false; var committing = false;
        try
        {
            began = true; editor.BeginEdit();
            Apply(Persisted(Get(provider, "Settings")!), values);
            if (!editor.VerifySettings(out var errors) || errors?.Count > 0) throw new ApiException(400, "capture_settings_invalid");
            var wanted = Project(Persisted(Get(provider, "Settings")!));
            committing = true;
            editor.EndEdit();
            var saved = loader.Invoke(provider, null) ?? throw new InvalidOperationException("Provider settings did not persist.");
            if (Revision(Project(Persisted(saved))) != Revision(wanted)) throw new InvalidOperationException("Provider settings readback differs.");
            return Read(provider);
        }
        catch (Exception error)
        {
            var rollbackFailed = false;
            if (began)
            {
                try
                {
                    editor.CancelEdit();
                    if (committing)
                    {
                        editor.BeginEdit();
                        Apply(Persisted(Get(provider, "Settings")!), before);
                        editor.EndEdit();
                        var restored = loader.Invoke(provider, null);
                        if (restored == null || Revision(Project(Persisted(restored))) != Revision(before)) rollbackFailed = true;
                    }
                }
                catch { rollbackFailed = true; }
                // Even failed persistence must leave live capture controls at the prior values.
                try { Apply(Persisted(Get(provider, "Settings")!), before); } catch { rollbackFailed = true; }
            }
            if (rollbackFailed) throw new ApiException(502, "capture_settings_rollback_failed");
            if (error is ApiException validation) throw validation;
            throw new ApiException(502, "capture_settings_save_failed");
        }
    }
    private static void Apply(object persisted, Dictionary<string, object> values)
    {
        foreach (var field in Fields) if (values.TryGetValue(field.Wire, out var value))
        {
            var property = Property(persisted, field);
            if (field.Kind == "enum" && value is string text) value = Enum.Parse(property.PropertyType, text, false);
            property.SetValue(persisted, value, null);
        }
    }
    private static object ConvertValue(object persisted, Field field, object value)
    {
        var property = Property(persisted, field);
        if (field.Kind == "bool" && value is bool) return value;
        if (field.Kind == "enum" && value is string name && Enum.GetNames(property.PropertyType).Contains(name)) return Enum.Parse(property.PropertyType, name, false);
        if ((field.Kind == "seconds" || field.Kind == "fps") && value is int number && number >= (field.Kind == "seconds" ? 5 : 10) && number <= 60) return number;
        if (field.Kind == "delay" && (value is int || value is double || value is decimal))
        {
            var delay = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (!double.IsNaN(delay) && !double.IsInfinity(delay) && delay >= 0 && delay <= 3600) return delay;
        }
        if (field.Kind == "path" && value is string path && path.Length <= 1024)
        {
            if (path.Length == 0) return path; // Provider's documented default/fallback.
            try
            {
                var full = Path.GetFullPath(path);
                if (!Path.IsPathRooted(path) || full.StartsWith("\\", StringComparison.Ordinal) || full.Length < 3 || full[1] != ':' ||
                    !string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), path.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) || !Directory.Exists(full))
                    throw new ApiException(400, "invalid_capture_directory");
                for (var directory = new DirectoryInfo(full); directory != null; directory = directory.Parent)
                    if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new ApiException(400, "invalid_capture_directory");
                return full;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is NotSupportedException)
            { throw new ApiException(400, "invalid_capture_directory"); }
        }
        throw new ApiException(400, "invalid_capture_settings");
    }
}
