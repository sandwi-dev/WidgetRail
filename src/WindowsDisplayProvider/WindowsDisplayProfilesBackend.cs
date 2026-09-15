using System.ComponentModel;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32;
using WidgetRail.PlatformBroker;

namespace WidgetRail.WindowsDisplayProvider;

internal sealed record SavedDisplayProfile(string Id, string Name, DisplayConfiguration Configuration);
internal sealed record DisplayProfileDocument(int Version, SavedDisplayProfile[] Profiles);

[SupportedOSPlatform("windows")]
public sealed class WindowsDisplayProfilesBackend : IDisplayProfilesPlatformBackend, IAsyncDisposable
{
    private readonly string _directory;
    private readonly IDisplayNative _native;
    private readonly IDisplayRestoreLauncher _launcher;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IDisplayRestoreSession? _restore;
    private BrokerWidgetIdentity? _restoreOwner;
    private string? _restoreName;
    private string? _outcome;
    private bool _disposed;
    private bool _observing;
    public event EventHandler<BrokerPlatformEvent>? EventPublished;

    public WindowsDisplayProfilesBackend(string settingsRoot, string bridgeExecutable)
        : this(Path.Combine(settingsRoot, "display-profiles"), new WindowsDisplayNative(),
            new DisplayRestoreLauncher(bridgeExecutable, Path.GetFullPath(Path.Combine(settingsRoot, "display-profiles")))) { }
    internal WindowsDisplayProfilesBackend(string directory, IDisplayNative native, IDisplayRestoreLauncher launcher)
    { _directory = Path.GetFullPath(directory); _native = native; _launcher = launcher; }

    public async Task<DisplayProfilesState> GetDisplayProfilesAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            StartObserving();
            return await Task.Run(ReadState, token).ConfigureAwait(false);
        }
        catch (Exception error) when (CanMap(error)) { throw Friendly(error); }
        finally { _gate.Release(); }
    }

    public async Task<DisplayProfilesState> ChangeDisplayProfileAsync(DisplayProfileCommand command,
        DisplayProfileRequest request, BrokerWidgetIdentity identity, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_restore is not null && command is not (DisplayProfileCommand.Keep or DisplayProfileCommand.Revert))
                throw new BrokerException("display_busy", "Keep or revert the current display change first.");
            if (command is DisplayProfileCommand.Keep or DisplayProfileCommand.Revert)
            {
                var restore = _restore;
                if (restore is null || request.RestoreId != restore.Pending.Id || _restoreOwner != identity)
                    throw new BrokerException("display_restore_expired", "This display confirmation has ended.");
                await restore.DecideAsync(command == DisplayProfileCommand.Keep, token).ConfigureAwait(false);
                _outcome = await restore.Completion.ConfigureAwait(false);
                _restore = null; _restoreOwner = null;
                await restore.DisposeAsync().ConfigureAwait(false);
                return ReadState();
            }
            var profiles = ReadProfiles().ToList();
            var index = profiles.FindIndex(profile => profile.Id == request.ProfileId);
            if (command != DisplayProfileCommand.Save && index < 0)
                throw new BrokerException("display_profile_missing", "This profile no longer exists.");
            var name = request.Name?.Trim();
            if (command is DisplayProfileCommand.Save or DisplayProfileCommand.Rename)
            {
                if (string.IsNullOrWhiteSpace(name) || name.Length > 60 || name.Any(char.IsControl))
                    throw new BrokerException("invalid_payload", "Use a profile name of 1 to 60 characters.");
                if (profiles.Any(profile => profile.Id != request.ProfileId && profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    throw new BrokerException("display_profile_name_exists", "A profile with this name already exists.");
            }
            switch (command)
            {
                case DisplayProfileCommand.Save:
                    if (profiles.Count >= 24) throw new BrokerException("display_profile_limit", "You can save up to 24 profiles. Remove one first.");
                    profiles.Add(new(Guid.NewGuid().ToString("N"), name!, _native.Capture()));
                    break;
                case DisplayProfileCommand.Rename: profiles[index] = profiles[index] with { Name = name! }; break;
                case DisplayProfileCommand.Replace: profiles[index] = profiles[index] with { Configuration = _native.Capture() }; break;
                case DisplayProfileCommand.Delete: profiles.RemoveAt(index); break;
                case DisplayProfileCommand.Apply:
                    var profile = profiles[index];
                    var target = DisplayProfileMatching.Remap(profile.Configuration, _native.ConnectedPaths());
                    _native.Validate(target);
                    _outcome = null;
                    _restore = await _launcher.StartAsync(target, token).ConfigureAwait(false);
                    _restoreOwner = identity; _restoreName = profile.Name;
                    _ = ObserveRestoreAsync(_restore);
                    return ReadState();
                default: throw new BrokerException("invalid_payload", "Unknown display profile action.");
            }
            WriteProfiles(profiles.ToArray());
            return ReadState();
        }
        catch (Exception error) when (CanMap(error)) { throw Friendly(error); }
        finally { _gate.Release(); PublishChanged(); }
    }

    private async Task ObserveRestoreAsync(IDisplayRestoreSession restore)
    {
        var outcome = await restore.Completion.ConfigureAwait(false);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!ReferenceEquals(_restore, restore)) return;
            _restore = null; _restoreOwner = null; _outcome = outcome;
            await restore.DisposeAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); PublishChanged(); }
    }
    private DisplayProfilesState ReadState()
    {
        var current = _native.Capture();
        var connected = _native.ConnectedPaths();
        var profiles = ReadProfiles().Select(profile =>
        {
            string? unavailable = null;
            DisplayConfiguration? remapped = null;
            try { remapped = DisplayProfileMatching.Remap(profile.Configuration, connected); }
            catch (BrokerException error) { unavailable = error.Message; }
            return new DisplayProfileSummary(profile.Id, profile.Name, profile.Configuration.Mode,
                profile.Configuration.Summaries(), remapped?.Matches(current) == true, unavailable is null, unavailable);
        }).ToArray();
        return new(current.Mode, current.Summaries(), profiles,
            _restore is null ? null : new(_restore.Pending.Id, _restoreName!, _restore.Pending.Deadline), _outcome);
    }
    private SavedDisplayProfile[] ReadProfiles()
    {
        CheckDirectory();
        var path = Path.Combine(_directory, "profiles.json");
        if (!File.Exists(path)) return [];
        CheckFile(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > 1024 * 1024) throw new InvalidDataException();
        var document = JsonSerializer.Deserialize<DisplayProfileDocument>(input);
        if (document is not { Version: 1, Profiles.Length: <= 24 } ||
            document.Profiles.Any(profile => profile is null || !Guid.TryParseExact(profile.Id, "N", out _) ||
                string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 60 || profile.Name.Any(char.IsControl) || profile.Configuration is null) ||
            document.Profiles.Select(profile => profile.Id).Distinct().Count() != document.Profiles.Length)
            throw new InvalidDataException();
        foreach (var profile in document.Profiles) profile.Configuration.Validate();
        return document.Profiles;
    }
    private void WriteProfiles(SavedDisplayProfile[] profiles)
    {
        CheckDirectory(); Directory.CreateDirectory(_directory); CheckDirectory();
        var path = Path.Combine(_directory, "profiles.json");
        CheckFile(path);
        var temporary = Path.Combine(_directory, ".profiles-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(output, new DisplayProfileDocument(1, profiles));
                output.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private void CheckDirectory()
    {
        for (var directory = new DirectoryInfo(_directory); directory is not null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Profile directory is redirected.");
    }
    private static void CheckFile(string path)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Profile file is redirected.");
    }
    private void StartObserving()
    {
        if (_observing) return;
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        _observing = true;
    }
    private void DisplayChanged(object? sender, EventArgs args) => PublishChanged();
    private void PublishChanged()
    {
        if (!_disposed) EventPublished?.Invoke(this, new(PlatformCapabilities.DisplaysReadV1,
            PlatformCapabilities.DisplayProfilesChanged, new { acknowledged = true }));
    }
    private static bool CanMap(Exception error) => error is Win32Exception or IOException or
        UnauthorizedAccessException or JsonException or TimeoutException;
    private static BrokerException Friendly(Exception error) => error switch
    {
        Win32Exception native => new(native.NativeErrorCode == 5 ? "display_denied" : "display_mode_unavailable",
            "Windows couldn't use this display setup. Check the connected monitors and save the profile again.", native),
        JsonException or InvalidDataException => new("display_profile_store_invalid", "Saved display profiles could not be read. Your file has been kept unchanged.", error),
        TimeoutException => new("display_restore_failed", "The display change did not finish. The restore guard will revert it.", error),
        _ => new("display_profile_io", "Display profiles could not be saved or restored. Check available disk space and folder access.", error),
    };
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            if (_observing) SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            if (_restore is { } restore)
            {
                _restore = null;
                await restore.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally { _gate.Release(); }
    }
}
