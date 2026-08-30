using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using WidgetRail.PlatformSettings;
using WidgetRail.WidgetStyling;

namespace WidgetRail.WidgetBridge;

public sealed record BridgePlatformAppearance
{
    public required long Revision { get; init; }
    public required string ThemeId { get; init; }
    public required string ThemeVersion { get; init; }
    public required double InterfaceScale { get; init; }
    public required double TextScale { get; init; }
    public required double BackdropOpacity { get; init; }
    public required MotionPreference Motion { get; init; }
    public required ContrastPreference Contrast { get; init; }
    public required bool BoldText { get; init; }
    public required TransparencyPreference Transparency { get; init; }
    public required bool AnimateWidgetSwitching { get; init; }
    public required WidgetSurfaceAppearanceOverride WidgetSurfaceAppearance { get; init; }
    public required IReadOnlyDictionary<string, WidgetSurfaceAppearanceOverride>
        WidgetSurfaceAppearanceOverrides { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, BridgeComputedStyleValue>> ShellStyles { get; init; }
}

public sealed class PlatformAppearanceService : IAsyncDisposable
{
    private static readonly TimeSpan ReloadDebounce = TimeSpan.FromMilliseconds(200);
    private readonly PlatformSettingsPaths _paths;
    private readonly ThemeManager _themes;
    private readonly ConcurrentDictionary<string, WidgetThemeCacheEntry> _widgetThemes =
        new(StringComparer.Ordinal);
    private readonly object _debounceLock = new();
    private readonly CancellationTokenSource _shutdown = new();
    private CancellationTokenSource? _debounce;
    private FileSystemWatcher? _settingsWatcher;
    private FileSystemWatcher? _themesWatcher;
    private bool _started;
    private bool _disposed;

    public PlatformAppearanceService(PlatformSettingsPaths paths, ThemeManager themes)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _themes = themes ?? throw new ArgumentNullException(nameof(themes));
    }

    public event EventHandler<ThemeSnapshot>? Changed;

    public ThemeSnapshot Current => _themes.Current;

    public IReadOnlyList<WrssDiagnostic> LastReloadDiagnostics =>
        _themes.LastReloadDiagnostics;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started) return;
        await ReloadNowAsync(cancellationToken).ConfigureAwait(false);
        CreateWatchers();
        _started = true;
    }

    public async Task<ThemeReloadResult> ReloadNowAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var result = await _themes.ReloadAsync(cancellationToken).ConfigureAwait(false);
        if (result.Published)
        {
            _widgetThemes.Clear();
            Changed?.Invoke(this, result.Current);
        }
        return result;
    }

    public WrssTheme ResolveWidgetTheme(string widgetId, WrssPackageResult widgetPackage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(widgetId);
        ArgumentNullException.ThrowIfNull(widgetPackage);
        var current = Current;
        if (_widgetThemes.TryGetValue(widgetId, out var cached) && cached.Revision == current.Revision)
            return cached.Theme;

        var compiled = current.CompileForWidget(widgetPackage);
        if (!compiled.IsValid)
        {
            var diagnostic = compiled.Diagnostics.First(item =>
                item.Severity == WrssDiagnosticSeverity.Error);
            throw new BridgeProtocolException(
                $"Layered style for widget '{widgetId}' is invalid: {SafeDiagnostic(diagnostic.Code)}: " +
                SafeDiagnostic(diagnostic.Message));
        }
        var entry = new WidgetThemeCacheEntry(current.Revision, compiled.Theme!);
        _widgetThemes[widgetId] = entry;
        return entry.Theme;
    }

    public BridgePlatformAppearance CreatePayload()
    {
        var current = Current;
        var appearance = current.Appearance;
        return new BridgePlatformAppearance
        {
            Revision = current.Revision,
            ThemeId = current.ActiveTheme.Id,
            ThemeVersion = current.ActiveTheme.Version.ToString(),
            InterfaceScale = appearance.InterfaceScale,
            TextScale = appearance.TextScale,
            BackdropOpacity = appearance.BackdropOpacity,
            Motion = appearance.Motion,
            Contrast = appearance.Contrast,
            BoldText = appearance.BoldText,
            Transparency = appearance.Transparency,
            AnimateWidgetSwitching = appearance.AnimateWidgetSwitching,
            WidgetSurfaceAppearance = appearance.WidgetSurfaceAppearance,
            WidgetSurfaceAppearanceOverrides = appearance.WidgetSurfaceAppearanceOverrides,
            ShellStyles = ResolveShellStyles(current.Theme),
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _shutdown.Cancel();
        lock (_debounceLock)
        {
            _debounce?.Cancel();
            _debounce?.Dispose();
            _debounce = null;
        }
        _settingsWatcher?.Dispose();
        _themesWatcher?.Dispose();
        _themes.Dispose();
        _shutdown.Dispose();
        await Task.CompletedTask;
    }

    private void CreateWatchers()
    {
        Directory.CreateDirectory(_paths.RootDirectory);
        RejectReparsePoint(_paths.RootDirectory);
        Directory.CreateDirectory(_paths.ThemesDirectory);
        RejectReparsePoint(_paths.ThemesDirectory);

        _settingsWatcher = new FileSystemWatcher(
            _paths.RootDirectory, Path.GetFileName(_paths.SettingsFile))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite |
                           NotifyFilters.Size | NotifyFilters.CreationTime,
        };
        _themesWatcher = new FileSystemWatcher(_paths.ThemesDirectory)
        {
            Filter = "*",
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                           NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
        };
        Subscribe(_settingsWatcher);
        Subscribe(_themesWatcher);
        _settingsWatcher.EnableRaisingEvents = true;
        _themesWatcher.EnableRaisingEvents = true;
    }

    private void Subscribe(FileSystemWatcher watcher)
    {
        watcher.Changed += OnFileSystemChanged;
        watcher.Created += OnFileSystemChanged;
        watcher.Deleted += OnFileSystemChanged;
        watcher.Renamed += OnFileSystemChanged;
        watcher.Error += OnWatcherError;
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs args) => ScheduleReload();
    private void OnWatcherError(object sender, ErrorEventArgs args) => ScheduleReload();

    private void ScheduleReload()
    {
        if (_disposed) return;
        CancellationTokenSource next;
        lock (_debounceLock)
        {
            var previous = _debounce;
            next = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            _debounce = next;
            previous?.Cancel();
            previous?.Dispose();
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
        catch (OperationCanceledException) when (debounce.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformSettingsException)
        {
            _ = exception;
            // ThemeManager retains its last valid immutable snapshot. The next
            // file event or explicit Settings reload can recover.
        }
        finally
        {
            lock (_debounceLock)
            {
                if (ReferenceEquals(_debounce, debounce)) _debounce = null;
            }
            debounce.Dispose();
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, BridgeComputedStyleValue>>
        ResolveShellStyles(WrssTheme theme)
    {
        var definitions = new (string Key, string Role, IReadOnlySet<WrssPseudoState> States)[]
        {
            ("canvas", "canvas", EmptyStates()),
            ("backdrop", "backdrop", EmptyStates()),
            ("panel", "panel", EmptyStates()),
            ("tray", "tray", EmptyStates()),
            ("tray-item", "tray-item", EmptyStates()),
            ("tray-item:selected", "tray-item", States(WrssPseudoState.Selected)),
            ("tray-item:focused", "tray-item", States(WrssPseudoState.Focused)),
            ("tray-item:selected:focused", "tray-item", States(WrssPseudoState.Selected, WrssPseudoState.Focused)),
            ("title", "title", EmptyStates()),
            ("body", "body", EmptyStates()),
            ("hint", "hint", EmptyStates()),
            ("status", "status", EmptyStates()),
        };
        var result = new SortedDictionary<string, IReadOnlyDictionary<string, BridgeComputedStyleValue>>(StringComparer.Ordinal);
        var total = 0;
        foreach (var definition in definitions)
        {
            var resolved = theme.Resolve(new WrssElement(
                definition.Role,
                $"shell.{definition.Key.Replace(':', '.')}",
                new HashSet<string>(StringComparer.Ordinal),
                definition.States));
            if (resolved.Properties.Count > BridgeRenderStyleLimits.MaximumPropertiesPerState)
                throw new BridgeProtocolException($"Shell style '{definition.Key}' has too many properties.");
            total += resolved.Properties.Count;
            if (total > BridgeRenderStyleLimits.MaximumTotalProperties)
                throw new BridgeProtocolException("Shell appearance has too many computed properties.");
            var values = new SortedDictionary<string, BridgeComputedStyleValue>(StringComparer.Ordinal);
            foreach (var (property, value) in resolved.Properties)
            {
                if (property.Length == 0 || property.Length > BridgeRenderStyleLimits.MaximumPropertyNameLength ||
                    value.Text.Length > BridgeRenderStyleLimits.MaximumValueTextLength ||
                    value.Unit is { Length: > 16 } ||
                    value.Number is { } number && !double.IsFinite(number))
                    throw new BridgeProtocolException($"Shell style '{definition.Key}' produced an invalid value.");
                values.Add(property, new BridgeComputedStyleValue
                {
                    Kind = value.Kind,
                    Text = value.Text,
                    Number = value.Number,
                    Unit = value.Unit,
                });
            }
            result.Add(definition.Key, new ReadOnlyDictionary<string, BridgeComputedStyleValue>(values));
        }
        return new ReadOnlyDictionary<string, IReadOnlyDictionary<string, BridgeComputedStyleValue>>(result);
    }

    private static IReadOnlySet<WrssPseudoState> EmptyStates() =>
        new HashSet<WrssPseudoState>();

    private static IReadOnlySet<WrssPseudoState> States(params WrssPseudoState[] states) =>
        new HashSet<WrssPseudoState>(states);

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new PlatformSettingsException(
                "reparse_point", "Platform appearance directories cannot be reparse points.");
    }

    private static string SafeDiagnostic(string value)
    {
        var singleLine = value.Replace('\r', ' ').Replace('\n', ' ');
        return singleLine.Length <= 256 ? singleLine : singleLine[..256];
    }

    private sealed record WidgetThemeCacheEntry(long Revision, WrssTheme Theme);
}
