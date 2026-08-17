using System.Collections.ObjectModel;
using WidgetRail.LauncherExperienceCatalog;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetStyling;

namespace WidgetRail.WidgetBridge;

public sealed record BridgeLauncherSealedAsset(
    string OpaqueAssetId,
    string Revision,
    string Format,
    string Base64);

public sealed record BridgeLauncherExperience
{
    public required long Revision { get; init; }
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string ContentDigest { get; init; }
    public required string PresentationRevision { get; init; }
    public required string Preset { get; init; }
    public required bool BuiltIn { get; init; }
    public required bool FollowWidgetPreset { get; init; }
    public required bool UseGlobalAppearance { get; init; }
    public required string BackgroundMode { get; init; }
    public required string FocusEffect { get; init; }
    public required string MotionIntensity { get; init; }
    public required LauncherLayoutRecipe Recipe { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, BridgeComputedStyleValue>> PackStyles { get; init; }
    public BridgeLauncherSealedAsset? PackBackground { get; init; }
    public string? Diagnostic { get; init; }
}

/// <summary>
/// The one private settings/catalog boundary for native Launcher Experience
/// presentation. It publishes only complete validated immutable values and
/// retains its last accepted value when a selected package later becomes
/// missing, invalid, tampered, or incompatible.
/// </summary>
public sealed class LauncherExperienceSelectionService : IAsyncDisposable
{
    private static readonly TimeSpan ReloadDebounce = TimeSpan.FromMilliseconds(200);
    private static readonly (LauncherSlot Slot, string Role)[] StyledSlots =
    [
        (LauncherSlot.HeroBackground, "launcher-hero-background"),
        (LauncherSlot.GameRail, "launcher-game-rail"),
        (LauncherSlot.DetailsPanel, "launcher-details-panel"),
        (LauncherSlot.CollectionTabs, "launcher-collection-tabs"),
        (LauncherSlot.SourceStatus, "launcher-source-status"),
        (LauncherSlot.OperationStatus, "launcher-operation-status"),
        (LauncherSlot.SystemStatus, "launcher-system-status"),
        (LauncherSlot.ControllerHints, "launcher-controller-hints"),
    ];

    private readonly PlatformSettingsStore _settings;
    private readonly LauncherExperienceCatalog.LauncherExperienceCatalog _catalog;
    private readonly LauncherExperienceSelectionPolicy _selectionPolicy;
    private readonly SemaphoreSlim _selectionGate = new(1, 1);
    private readonly object _gate = new();
    private readonly object _debounceGate = new();
    private readonly CancellationTokenSource _shutdown = new();
    private CancellationTokenSource? _debounce;
    private FileSystemWatcher? _settingsWatcher;
    private FileSystemWatcher? _catalogWatcher;
    private BridgeLauncherExperience? _current;
    private string? _lastFailureKey;
    private long _revision;
    private bool _disposed;

    public LauncherExperienceSelectionService(PlatformSettingsStore settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _catalog = new LauncherExperienceCatalog.LauncherExperienceCatalog(
            settings.Paths.LauncherExperiencesDirectory);
        _selectionPolicy = new LauncherExperienceSelectionPolicy(settings);
    }

    public event EventHandler<long>? Changed;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await ReloadNowAsync(cancellationToken).ConfigureAwait(false);
        CreateWatchers();
    }

    public BridgeLauncherExperience CreatePayload()
    {
        lock (_gate)
            return _current ?? throw new BridgeProtocolException(
                "Launcher Experience selection is unavailable.");
    }

    internal async Task<BridgeLauncherExperience> ApplySelectionAsync(
        BridgeLauncherExperienceSelectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _selectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            switch (request.Operation)
            {
                case BridgeLauncherExperienceSelectionOperation.SelectExact
                    when !string.IsNullOrWhiteSpace(request.Id) &&
                        !string.IsNullOrWhiteSpace(request.Version):
                    try
                    {
                        await _selectionPolicy.SelectAsync(
                            request.Id, request.Version, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (PlatformSettingsException exception)
                    {
                        throw new BridgeProtocolException(
                            "The exact installed Launcher Experience version is unavailable or invalid.",
                            exception);
                    }
                    break;
                case BridgeLauncherExperienceSelectionOperation.RecoverBuiltIn
                    when request.Id is null && request.Version is null:
                    await _selectionPolicy.RecoverBuiltInAsync(cancellationToken)
                        .ConfigureAwait(false);
                    break;
                default:
                    throw new BridgeProtocolException(
                        "Launcher Experience selection request is invalid.");
            }
            await ReloadNowAsync(cancellationToken).ConfigureAwait(false);
            return CreatePayload();
        }
        finally
        {
            _selectionGate.Release();
        }
    }

    public async Task ReloadNowAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        BridgeLauncherExperience? candidate = null;
        string? failure = null;
        try
        {
            var settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
            candidate = BuildCandidate(settings.LauncherExperience);
        }
        catch (Exception exception) when (exception is PlatformSettingsException or
            LauncherExperiencePackageException or IOException or UnauthorizedAccessException)
        {
            failure = SafeDiagnostic(exception.Message);
        }

        long publishedRevision = 0;
        lock (_gate)
        {
            if (candidate is not null &&
                _current is { FollowWidgetPreset: false } admitted &&
                !candidate.FollowWidgetPreset &&
                string.Equals(candidate.Id, admitted.Id, StringComparison.Ordinal) &&
                string.Equals(candidate.Version, admitted.Version, StringComparison.Ordinal) &&
                !string.Equals(candidate.ContentDigest, admitted.ContentDigest,
                    StringComparison.Ordinal))
            {
                failure = "The selected immutable Launcher Experience content digest changed.";
                candidate = null;
            }
            if (candidate is not null)
            {
                var identity = CandidateIdentity(candidate);
                if (_current is null || identity != CandidateIdentity(_current))
                {
                    candidate = candidate with { Revision = ++_revision };
                    _current = candidate;
                    publishedRevision = candidate.Revision;
                }
                _lastFailureKey = null;
            }
            else
            {
                failure ??= "The selected Launcher Experience could not be validated.";
                var failureKey = SafeDiagnostic(failure);
                if (_current is null)
                {
                    var recovery = BuildRecovery(failureKey) with { Revision = ++_revision };
                    _current = recovery;
                    _lastFailureKey = failureKey;
                    publishedRevision = recovery.Revision;
                }
                else if (!string.Equals(_lastFailureKey, failureKey, StringComparison.Ordinal))
                {
                    _current = _current with
                    {
                        Revision = ++_revision,
                        Diagnostic = failureKey,
                    };
                    _lastFailureKey = failureKey;
                    publishedRevision = _current.Revision;
                }
            }
        }
        if (publishedRevision != 0) Changed?.Invoke(this, publishedRevision);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _shutdown.Cancel();
        lock (_debounceGate)
        {
            _debounce?.Cancel();
            _debounce?.Dispose();
            _debounce = null;
        }
        _settingsWatcher?.Dispose();
        _catalogWatcher?.Dispose();
        _selectionGate.Dispose();
        _shutdown.Dispose();
        return ValueTask.CompletedTask;
    }

    private BridgeLauncherExperience BuildCandidate(
        LauncherExperienceSelectionSettings selection)
    {
        var id = selection.SelectedId;
        var version = selection.SelectedVersion;
        if (id is null || version is null)
        {
            if (selection.LastGoodId is null || selection.LastGoodVersion is null)
                return Create(
                    LauncherExperienceBuiltIns.RecoveryFor(
                        LauncherLayoutPreset.HeroRail),
                    useGlobalAppearance: true,
                    diagnostic: null) with { FollowWidgetPreset = true };
            id = selection.LastGoodId;
            version = selection.LastGoodVersion;
        }
        var entry = _catalog.Load(id, version);
        if (entry.IsValid && entry.Package is not null)
            return Create(entry.Package, selection.UseGlobalAppearance, null);

        var selectedDiagnostic = entry.Diagnostics.FirstOrDefault();
        if (selection.LastGoodId is { } lastGoodId &&
            selection.LastGoodVersion is { } lastGoodVersion &&
            (!string.Equals(id, lastGoodId, StringComparison.Ordinal) ||
             !string.Equals(version, lastGoodVersion, StringComparison.Ordinal)))
        {
            var lastGood = _catalog.Load(lastGoodId, lastGoodVersion);
            if (lastGood.IsValid && lastGood.Package is not null)
                return Create(
                    lastGood.Package,
                    selection.UseGlobalAppearance,
                    "Selected Launcher Experience was invalid; retained last-good " +
                    lastGoodId + "@" + lastGoodVersion + ".");
        }
        throw new LauncherExperiencePackageException(
            selectedDiagnostic?.Code ?? "experience_invalid",
            selectedDiagnostic?.Message ?? "The selected Launcher Experience is invalid.");
    }

    private BridgeLauncherExperience BuildRecovery(string diagnostic) => Create(
        LauncherExperienceBuiltIns.RecoveryFor(LauncherLayoutPreset.HeroRail),
        useGlobalAppearance: true,
        diagnostic);

    private static BridgeLauncherExperience Create(
        LauncherExperiencePackage package,
        bool useGlobalAppearance,
        string? diagnostic)
    {
        var descriptor = package.Descriptor;
        var manifest = package.Manifest;
        var recipe = package.Recipe ?? LauncherExperienceBuiltIns.RecoveryFor(
            descriptor.LayoutPreset).Recipe!;
        var styles = package.Descriptor.IsBuiltIn
            ? EmptyStyles()
            : CompileStyles(package);
        var revision = descriptor.Id + ":" + descriptor.Version + ":" +
            descriptor.ContentDigest;
        BridgeLauncherSealedAsset? background = null;
        var backgroundMode = manifest.Parameters.BackgroundMode ??
            "selected-game-artwork";
        if (!descriptor.IsBuiltIn && backgroundMode == "pack-asset")
        {
            var path = Path.Combine(package.PackageRoot,
                manifest.PreviewFile.Replace('/', Path.DirectorySeparatorChar));
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length > LauncherExperienceValidator.MaximumAssetBytes)
                throw new LauncherExperiencePackageException(
                    "file_too_large", "Launcher Experience background exceeds the asset budget.");
            var extension = Path.GetExtension(manifest.PreviewFile);
            var format = extension.Equals(".png", StringComparison.Ordinal)
                ? "png"
                : extension is ".jpg" or ".jpeg" ? "jpeg" : "webp";
            background = new(
                "pack:" + descriptor.ContentDigest,
                revision,
                format,
                Convert.ToBase64String(bytes));
        }
        return new BridgeLauncherExperience
        {
            Revision = 0,
            Id = descriptor.Id,
            Version = descriptor.Version.ToString(),
            ContentDigest = descriptor.ContentDigest,
            PresentationRevision = revision,
            Preset = Kebab(descriptor.LayoutPreset),
            BuiltIn = descriptor.IsBuiltIn,
            FollowWidgetPreset = false,
            UseGlobalAppearance = useGlobalAppearance,
            BackgroundMode = backgroundMode,
            FocusEffect = manifest.Parameters.FocusEffect ?? "lift",
            MotionIntensity = manifest.Parameters.MotionIntensity ?? "standard",
            Recipe = recipe,
            PackStyles = styles,
            PackBackground = background,
            Diagnostic = diagnostic,
        };
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, BridgeComputedStyleValue>>
        CompileStyles(LauncherExperiencePackage package)
    {
        var loaded = WrssPackageLoader.Load(
            package.Manifest.StyleFile,
            new WrssFileSourceProvider(package.PackageRoot));
        var compiled = WrssThemeCompiler.Compile(loaded);
        if (!compiled.IsValid || compiled.Theme is null)
            throw new LauncherExperiencePackageException(
                "wrss_invalid", "Launcher Experience styles could not be compiled.");
        var result = new SortedDictionary<string, IReadOnlyDictionary<string, BridgeComputedStyleValue>>(
            StringComparer.Ordinal);
        var total = 0;
        foreach (var (slot, role) in StyledSlots)
        {
            var resolved = compiled.Theme.Resolve(new WrssElement(role));
            total += resolved.Properties.Count;
            if (resolved.Properties.Count > BridgeRenderStyleLimits.MaximumPropertiesPerState ||
                total > BridgeRenderStyleLimits.MaximumTotalProperties)
                throw new LauncherExperiencePackageException(
                    "style_budget", "Launcher Experience styles exceed the native projection budget.");
            var values = new SortedDictionary<string, BridgeComputedStyleValue>(StringComparer.Ordinal);
            foreach (var (property, value) in resolved.Properties)
                values.Add(property, new BridgeComputedStyleValue
                {
                    Kind = value.Kind,
                    Text = value.Text,
                    Number = value.Number,
                    Unit = value.Unit,
                });
            result.Add(Kebab(slot), new ReadOnlyDictionary<string, BridgeComputedStyleValue>(values));
        }
        return new ReadOnlyDictionary<string, IReadOnlyDictionary<string, BridgeComputedStyleValue>>(result);
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, BridgeComputedStyleValue>>
        EmptyStyles() => new ReadOnlyDictionary<string, IReadOnlyDictionary<string, BridgeComputedStyleValue>>(
            new SortedDictionary<string, IReadOnlyDictionary<string, BridgeComputedStyleValue>>(
                StringComparer.Ordinal));

    private void CreateWatchers()
    {
        Directory.CreateDirectory(_settings.Paths.RootDirectory);
        Directory.CreateDirectory(_settings.Paths.LauncherExperiencesDirectory);
        _settingsWatcher = new FileSystemWatcher(
            _settings.Paths.RootDirectory, Path.GetFileName(_settings.Paths.SettingsFile))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite |
                NotifyFilters.Size | NotifyFilters.CreationTime,
        };
        _catalogWatcher = new FileSystemWatcher(_settings.Paths.LauncherExperiencesDirectory)
        {
            Filter = "*",
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
        };
        Subscribe(_settingsWatcher);
        Subscribe(_catalogWatcher);
        _settingsWatcher.EnableRaisingEvents = true;
        _catalogWatcher.EnableRaisingEvents = true;
    }

    private void Subscribe(FileSystemWatcher watcher)
    {
        watcher.Changed += OnChanged;
        watcher.Created += OnChanged;
        watcher.Deleted += OnChanged;
        watcher.Renamed += OnChanged;
        watcher.Error += OnWatcherError;
    }

    private void OnChanged(object sender, FileSystemEventArgs args) => ScheduleReload();
    private void OnWatcherError(object sender, ErrorEventArgs args) => ScheduleReload();

    private void ScheduleReload()
    {
        if (_disposed) return;
        CancellationTokenSource next;
        lock (_debounceGate)
        {
            var prior = _debounce;
            next = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            _debounce = next;
            prior?.Cancel();
            prior?.Dispose();
        }
        _ = ReloadAfterDelayAsync(next);
    }

    private async Task ReloadAfterDelayAsync(CancellationTokenSource debounce)
    {
        try
        {
            await Task.Delay(ReloadDebounce, debounce.Token).ConfigureAwait(false);
            await ReloadNowAsync(debounce.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (debounce.IsCancellationRequested) { }
        finally
        {
            lock (_debounceGate)
                if (ReferenceEquals(_debounce, debounce)) _debounce = null;
            debounce.Dispose();
        }
    }

    private static string CandidateIdentity(BridgeLauncherExperience value) =>
        value.PresentationRevision + "\n" + value.UseGlobalAppearance + "\n" +
        value.FollowWidgetPreset + "\n" + value.Diagnostic;

    private static string SafeDiagnostic(string value)
    {
        var result = value.Replace('\r', ' ').Replace('\n', ' ');
        return result.Length <= 256 ? result : result[..256];
    }

    private static string Kebab<T>(T value) where T : struct, Enum =>
        value.ToString() switch
        {
            "HeroRail" => "hero-rail",
            "CoverWall" => "cover-wall",
            "CompactGrid" => "compact-grid",
            "HeroBackground" => "hero-background",
            "GameRail" => "game-rail",
            "DetailsPanel" => "details-panel",
            "CollectionTabs" => "collection-tabs",
            "SourceStatus" => "source-status",
            "OperationStatus" => "operation-status",
            "SystemStatus" => "system-status",
            "ControllerHints" => "controller-hints",
            _ => value.ToString().ToLowerInvariant(),
        };
}
