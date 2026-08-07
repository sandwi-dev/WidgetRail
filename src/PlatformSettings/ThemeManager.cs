using GameBarAlternative.WidgetStyling;

namespace GameBarAlternative.PlatformSettings;

public sealed class ThemeSnapshot
{
    internal ThemeSnapshot(
        long revision,
        ThemeDescriptor activeTheme,
        AppearanceSettings appearance,
        GbssTheme theme,
        IReadOnlyList<GbssDocument> platformDocuments,
        IReadOnlyList<GbssDocument> userDocuments,
        IReadOnlyList<GbssDiagnostic> diagnostics)
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
    public GbssTheme Theme { get; }
    public IReadOnlyList<GbssDiagnostic> Diagnostics { get; }
    internal IReadOnlyList<GbssDocument> PlatformDocuments { get; }
    internal IReadOnlyList<GbssDocument> UserDocuments { get; }

    public GbssCompileResult CompileForWidget(GbssPackageResult widgetPackage)
    {
        ArgumentNullException.ThrowIfNull(widgetPackage);
        return ThemeLayerCompiler.Compile(
            new GbssPackageResult(PlatformDocuments, []),
            widgetPackage,
            new GbssPackageResult(UserDocuments, []));
    }
}

public sealed record ThemeReloadResult(
    bool Published,
    ThemeSnapshot Current,
    IReadOnlyList<GbssDiagnostic> Diagnostics);

public sealed class ThemeManager : IDisposable
{
    private readonly PlatformSettingsStore _settings;
    private readonly ThemeCatalog _catalog;
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly object _stateLock = new();
    private ThemeSnapshot _current;
    private IReadOnlyList<GbssDiagnostic> _lastReloadDiagnostics;
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

    public IReadOnlyList<GbssDiagnostic> LastReloadDiagnostics
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
            var user = selected.Descriptor.IsBuiltIn ? EmptyPackage() : selected.Package;
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

    private ThemeReloadResult Retain(IReadOnlyList<GbssDiagnostic> diagnostics)
    {
        ThemeSnapshot current;
        lock (_stateLock)
        {
            _lastReloadDiagnostics = diagnostics;
            current = _current;
        }
        return new ThemeReloadResult(false, current, diagnostics);
    }

    private static GbssPackageResult EmptyPackage() => new([], []);

    private static GbssDiagnostic Diagnostic(string code, string message, string source) =>
        new(source, 1, 1, GbssDiagnosticSeverity.Error, code, SafeMessage(message));

    private static string SafeMessage(string value)
    {
        var oneLine = value.Replace('\r', ' ').Replace('\n', ' ');
        return oneLine.Length <= 512 ? oneLine : oneLine[..512];
    }
}

public static class ThemeLayerCompiler
{
    public static GbssCompileResult Compile(
        GbssPackageResult platform,
        GbssPackageResult widget,
        GbssPackageResult user)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(widget);
        ArgumentNullException.ThrowIfNull(user);
        var compiled = GbssThemeCompiler.Compile(
        [
            new GbssThemeLayer(0, platform.Documents),
            new GbssThemeLayer(100, widget.Documents),
            new GbssThemeLayer(200, user.Documents),
        ]);
        var diagnostics = platform.Diagnostics
            .Concat(widget.Diagnostics)
            .Concat(user.Diagnostics)
            .Concat(compiled.Diagnostics)
            .ToArray();
        var hasErrors = diagnostics.Any(item => item.Severity == GbssDiagnosticSeverity.Error);
        return new GbssCompileResult(hasErrors ? null : compiled.Theme, diagnostics);
    }
}
