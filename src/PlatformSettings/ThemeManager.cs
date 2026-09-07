using WidgetRail.WidgetStyling;

namespace WidgetRail.PlatformSettings;

public sealed class ThemeSnapshot
{
    internal ThemeSnapshot(
        long revision,
        ThemeDescriptor activeTheme,
        AppearanceSettings appearance,
        WrssTheme theme,
        IReadOnlyList<WrssDocument> platformDocuments,
        IReadOnlyList<WrssDocument> userDocuments,
        IReadOnlyList<WrssDiagnostic> diagnostics)
    {
        Revision = revision;
        ActiveTheme = activeTheme;
        Appearance = appearance;
        Theme = theme;
        PlatformDocuments = platformDocuments;
        UserDocuments = userDocuments;
        Diagnostics = diagnostics;
    }

    public long Revision { get; }
    public ThemeDescriptor ActiveTheme { get; }
    public AppearanceSettings Appearance { get; }
    public WrssTheme Theme { get; }
    public IReadOnlyList<WrssDiagnostic> Diagnostics { get; }
    internal IReadOnlyList<WrssDocument> PlatformDocuments { get; }
    internal IReadOnlyList<WrssDocument> UserDocuments { get; }

    public WrssCompileResult CompileForWidget(WrssPackageResult widgetPackage)
    {
        ArgumentNullException.ThrowIfNull(widgetPackage);
        return ThemeLayerCompiler.Compile(
            new WrssPackageResult(PlatformDocuments, []),
            widgetPackage,
            new WrssPackageResult(UserDocuments, []));
    }
}

public sealed record ThemeReloadResult(
    bool Published,
    ThemeSnapshot Current,
    IReadOnlyList<WrssDiagnostic> Diagnostics);

public sealed class ThemeManager : IDisposable
{
    private readonly PlatformSettingsStore _settings;
    private readonly ThemeCatalog _catalog;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly object _stateLock = new();
    private ThemeSnapshot _current;
    private IReadOnlyList<WrssDiagnostic> _lastReloadDiagnostics;
    private bool _disposed;

    public ThemeManager(PlatformSettingsStore settings, ThemeCatalog catalog)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        var builtIn = catalog.BuiltInDefault;
        var compiled = ThemeLayerCompiler.Compile(builtIn.Package, EmptyPackage(), EmptyPackage());
        if (!compiled.IsValid)
            throw new PlatformSettingsException(
                "invalid_builtin_theme", "The built-in theme could not be published.");
        _current = new ThemeSnapshot(
            1,
            builtIn.Descriptor,
            AppearanceSettings.Default,
            compiled.Theme!,
            builtIn.Package.Documents,
            [],
            compiled.Diagnostics);
        _lastReloadDiagnostics = compiled.Diagnostics;
    }

    public ThemeSnapshot Current
    {
        get { lock (_stateLock) return _current; }
    }

    public IReadOnlyList<WrssDiagnostic> LastReloadDiagnostics
    {
        get { lock (_stateLock) return _lastReloadDiagnostics; }
    }

    public async Task<ThemeReloadResult> ReloadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PlatformSettingsDocument settings;
            try
            {
                settings = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (PlatformSettingsException exception)
            {
                return Retain([Diagnostic(exception.Code, exception.Message, "platform-settings.json")]);
            }

            var selected = _catalog.Load(
                settings.Appearance.ThemeId,
                settings.Appearance.ThemeVersion);
            var platform = _catalog.BuiltInDefault.Package;
            var user = string.Equals(
                selected.Descriptor.Id,
                ThemeIdentity.BuiltInDefault,
                StringComparison.Ordinal)
                ? EmptyPackage()
                : selected.Package;
            var compiled = ThemeLayerCompiler.Compile(platform, EmptyPackage(), user);
            if (!compiled.IsValid)
                return Retain(compiled.Diagnostics);

            ThemeSnapshot next;
            lock (_stateLock)
            {
                var revision = checked(_current.Revision + 1);
                next = new ThemeSnapshot(
                    revision,
                    selected.Descriptor,
                    settings.Appearance,
                    compiled.Theme!,
                    platform.Documents,
                    user.Documents,
                    compiled.Diagnostics);
                _current = next;
                _lastReloadDiagnostics = compiled.Diagnostics;
            }
            return new ThemeReloadResult(true, next, compiled.Diagnostics);
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _reloadGate.Dispose();
    }

    private ThemeReloadResult Retain(IReadOnlyList<WrssDiagnostic> diagnostics)
    {
        ThemeSnapshot current;
        lock (_stateLock)
        {
            _lastReloadDiagnostics = diagnostics;
            current = _current;
        }
        return new ThemeReloadResult(false, current, diagnostics);
    }

    private static WrssPackageResult EmptyPackage() => new([], []);

    private static WrssDiagnostic Diagnostic(string code, string message, string source) =>
        new(source, 1, 1, WrssDiagnosticSeverity.Error, code, SafeMessage(message));

    private static string SafeMessage(string value)
    {
        var oneLine = value.Replace('\r', ' ').Replace('\n', ' ');
        return oneLine.Length <= 512 ? oneLine : oneLine[..512];
    }
}

public static class ThemeLayerCompiler
{
    public static WrssCompileResult Compile(
        WrssPackageResult platform,
        WrssPackageResult widget,
        WrssPackageResult user)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(widget);
        ArgumentNullException.ThrowIfNull(user);
        var compiled = WrssThemeCompiler.Compile(
        [
            new WrssThemeLayer(0, platform.Documents),
            new WrssThemeLayer(100, user.Documents),
            new WrssThemeLayer(200, widget.Documents),
        ]);
        var diagnostics = platform.Diagnostics
            .Concat(widget.Diagnostics)
            .Concat(user.Diagnostics)
            .Concat(compiled.Diagnostics)
            .ToArray();
        var hasErrors = diagnostics.Any(item => item.Severity == WrssDiagnosticSeverity.Error);
        return new WrssCompileResult(hasErrors ? null : compiled.Theme, diagnostics);
    }
}
