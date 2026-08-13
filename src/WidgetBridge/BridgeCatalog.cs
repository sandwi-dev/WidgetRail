using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using GameBarAlternative.PlatformBroker;
using GameBarAlternative.WidgetCatalog;
using GameBarAlternative.WidgetProtocol;
using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetStyling;
using CatalogService = GameBarAlternative.WidgetCatalog.WidgetCatalog;

namespace GameBarAlternative.WidgetBridge;

public sealed record BridgeQuickActionDescriptor
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string ActionId { get; init; }
    public required string SourceElementId { get; init; }
    public ControllerButton? ControllerButton { get; init; }
}

public sealed record BridgeWidgetDescriptor
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string InstanceId { get; init; }
    public required string RuntimeGeneration { get; init; }
    public required string PresentationGeneration { get; init; }
    public required WidgetGlyph Icon { get; init; }
    public bool PinningSupported { get; init; }
    public WidgetAdvancedPresentationDeclaration? AdvancedPresentation { get; init; }
    /// <summary>
    /// Trusted host policy for the bundled Network Controls credential prompt.
    /// This is derived by the bridge and cannot be declared by a widget package.
    /// </summary>
    public bool ProtectedWifiPromptSupported { get; init; }
    public IReadOnlyList<BridgeQuickActionDescriptor> QuickActions { get; init; } = [];
}

internal sealed record ConfiguredWidget
{
    public required string Id { get; init; }
    public required string PackageId { get; init; }
    public required string PublisherId { get; init; }
    public required string Name { get; init; }
    public required string InstanceId { get; init; }
    public WidgetGlyph Icon { get; init; } = WidgetGlyph.Connection;
    /// <summary>Immutable declaration; native-window authority remains host-only.</summary>
    public bool PinningSupported { get; init; }
    /// <summary>
    /// Validated package declaration. Trusted catalog JSON cannot manufacture
    /// this value; it is copied only from a bundled or installed manifest.
    /// </summary>
    [JsonIgnore]
    public WidgetAdvancedPresentationDeclaration? AdvancedPresentation { get; init; }
    public required string WorkerExecutable { get; init; }
    public string? StyleFile { get; init; }
    public IReadOnlyList<string> WorkerArguments { get; init; } = [];
    public IReadOnlyList<string> DeclaredCapabilities { get; init; } = [];
    /// <summary>
    /// Trusted host policy. This member is never read from catalog JSON or a
    /// widget manifest; installed community packages are marked during merge.
    /// </summary>
    [JsonIgnore]
    public bool RequiresAppContainer { get; init; }
    [JsonIgnore]
    public string? IsolationKey { get; init; }
    [JsonIgnore]
    public string? AuthorityGeneration { get; init; }
    [JsonIgnore]
    public IReadOnlyList<string> ReadOnlyPaths { get; init; } = [];
    [JsonIgnore]
    public Func<CancellationToken, IWidgetProcessContentLease>?
        ContentLeaseFactory { get; init; }
    /// <summary>
    /// Trusted catalog provenance. Only the packaged generic loader receives
    /// its closed startup-exit diagnostic vocabulary.
    /// </summary>
    [JsonIgnore]
    public bool UsesGenericWorkerHost { get; init; }
    /// <summary>
    /// Optional author guidance used only for bounded diagnostics. It never
    /// configures Job containment or host admission.
    /// </summary>
    [JsonPropertyName("memoryLimitMb")]
    public int? MemoryRequestMb { get; init; }
    /// <summary>
    /// Validated residency policy. For installed packages this is copied from
    /// the immutable manifest; a worker cannot alter it through IPC.
    /// </summary>
    public WidgetResidencyPolicy ResidencyPolicy { get; init; } = new();
    public IReadOnlyList<BridgeQuickActionDescriptor> QuickActions { get; init; } = [];
    [JsonIgnore]
    public GbssTheme? CompiledTheme { get; init; }
    [JsonIgnore]
    public GbssPackageResult StylePackage { get; init; } = new([], []);
    [JsonIgnore]
    public string WorkerFingerprint { get; init; } = string.Empty;
    [JsonIgnore]
    public string CatalogFingerprint { get; init; } = string.Empty;

    public BridgeWidgetDescriptor PublicDescriptor() => new()
    {
        Id = Id,
        Name = Name,
        InstanceId = InstanceId,
        RuntimeGeneration = WorkerFingerprint[..32].ToLowerInvariant(),
        PresentationGeneration = CatalogFingerprint[..32].ToLowerInvariant(),
        Icon = Icon,
        PinningSupported = PinningSupported,
        AdvancedPresentation = AdvancedPresentation,
        ProtectedWifiPromptSupported =
            string.Equals(PackageId, "org.gbar.firstparty.network-controls", StringComparison.Ordinal) &&
            string.Equals(PublisherId, "org.gbar.firstparty", StringComparison.Ordinal) &&
            DeclaredCapabilities.Contains(
                PlatformCapabilities.NetworkWifiConnectV1, StringComparer.Ordinal),
        QuickActions = QuickActions,
    };
}

internal sealed record BridgeCatalogDocument
{
    public int CatalogVersion { get; init; } = 1;
    public IReadOnlyList<ConfiguredWidget> Widgets { get; init; } = [];
    /// <summary>
    /// Platform-owned packages that intentionally use the same generic worker,
    /// capability-free AppContainer, and broker path as installed packages.
    /// Only shell presentation metadata lives here; executable identity,
    /// capabilities, resource limits, and residency come from manifest.json.
    /// </summary>
    public IReadOnlyList<BundledWidgetDefinition> BundledWidgets { get; init; } = [];
    public string GenericWorkerExecutable { get; init; } =
        "runtime/WidgetWorkerHost/WidgetWorkerHost.exe";
}

internal sealed record BundledWidgetDefinition
{
    public required string Id { get; init; }
    /// <summary>Host-pinned package identity; must exactly match manifest.json.</summary>
    public required string PackageId { get; init; }
    public required string InstanceId { get; init; }
    public required string PackageRoot { get; init; }
    public WidgetGlyph Icon { get; init; } = WidgetGlyph.Connection;
    public IReadOnlyList<BridgeQuickActionDescriptor> QuickActions { get; init; } = [];
}

public sealed class BridgeCatalog
{
    private readonly IReadOnlyDictionary<string, ConfiguredWidget> _configured;
    private readonly IReadOnlyList<ConfiguredWidget> _ordered;
    private readonly string _fingerprint;

    internal BridgeCatalog(IEnumerable<ConfiguredWidget> configured)
    {
        _ordered = configured.ToArray();
        _configured = _ordered.ToDictionary(widget => widget.Id, StringComparer.Ordinal);
        _fingerprint = Fingerprint(_ordered.Select(widget => widget.CatalogFingerprint));
    }

    public IReadOnlyList<BridgeWidgetDescriptor> Widgets =>
        _ordered.Select(widget => widget.PublicDescriptor()).ToArray();

    internal ConfiguredWidget GetConfigured(string widgetId)
    {
        if (!_configured.TryGetValue(widgetId, out var widget))
            throw new BridgeProtocolException($"Unknown widget '{widgetId}'.");
        return widget;
    }

    internal bool IsEquivalentTo(BridgeCatalog other) =>
        string.Equals(_fingerprint, other._fingerprint, StringComparison.Ordinal);

    public static BridgeCatalog Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        BridgeCatalogDocument document;
        try
        {
            document = JsonSerializer.Deserialize<BridgeCatalogDocument>(File.ReadAllBytes(fullPath), options)
                ?? throw new BridgeCatalogException("Catalog document was null.");
        }
        catch (JsonException exception)
        {
            throw new BridgeCatalogException("Catalog JSON is invalid or contains unknown properties.", exception);
        }
        if (document.CatalogVersion != 1)
            throw new BridgeCatalogException("Only catalog version 1 is supported.");
        if (document.Widgets is null || document.BundledWidgets is null ||
            document.Widgets.Count + document.BundledWidgets.Count > 256)
            throw new BridgeCatalogException("Catalog must contain at most 256 widgets.");
        if (!IsSafePackageRelativePath(document.GenericWorkerExecutable, allowDirectory: false))
            throw new BridgeCatalogException("The generic worker executable path is invalid.");

        var widgets = new Dictionary<string, ConfiguredWidget>(StringComparer.Ordinal);
        var packageIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in document.Widgets)
        {
            ValidateIdentifier(source.Id, "widget ID");
            ValidatePackageIdentity(source.PackageId, "package ID");
            ValidatePackageIdentity(source.PublisherId, "publisher ID");
            ValidateIdentifier(source.InstanceId, "instance ID");
            ValidateLabel(source.Name, "widget name");
            if (source.WorkerArguments is null || source.WorkerArguments.Count > 64 ||
                source.WorkerArguments.Any(argument => argument is null || argument.Length > 4096))
                throw new BridgeCatalogException($"Widget '{source.Id}' has invalid worker arguments.");
            if (source.DeclaredCapabilities is null || source.DeclaredCapabilities.Count > 32 ||
                source.DeclaredCapabilities.Any(capability =>
                    string.IsNullOrWhiteSpace(capability) || capability.Length > 128 ||
                    !PlatformCapabilities.IsManifestDeclarable(capability)) ||
                source.DeclaredCapabilities.Distinct(StringComparer.Ordinal).Count() !=
                    source.DeclaredCapabilities.Count)
                throw new BridgeCatalogException(
                    $"Widget '{source.Id}' has invalid declared capabilities.");
            if (source.MemoryRequestMb is <= 0)
                throw new BridgeCatalogException(
                    $"Widget '{source.Id}' memoryLimitMb must be a positive advisory value.");
            if (source.ResidencyPolicy is null)
                throw new BridgeCatalogException(
                    $"Widget '{source.Id}' residencyPolicy cannot be null.");
            var residencyErrors = new List<ManifestValidationError>();
            WidgetResidencyPolicies.Validate(
                source.ResidencyPolicy,
                "$.residencyPolicy",
                (path, code, message) => residencyErrors.Add(new(path, code, message)));
            if (residencyErrors.Count != 0)
                throw new BridgeCatalogException(
                    $"Widget '{source.Id}' has an invalid residency policy ({residencyErrors[0].Code}).");
            if (source.QuickActions is null || source.QuickActions.Count > 16)
                throw new BridgeCatalogException($"Widget '{source.Id}' has too many quick actions.");

            var executable = Path.GetFullPath(source.WorkerExecutable, directory);
            if (!File.Exists(executable))
                throw new BridgeCatalogException($"Worker executable for '{source.Id}' does not exist.");
            var style = CompileTheme(source, directory);
            var quickActionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var action in source.QuickActions)
            {
                ValidateIdentifier(action.Id, "quick action ID");
                ValidateIdentifier(action.ActionId, "action ID");
                ValidateIdentifier(action.SourceElementId, "source element ID");
                ValidateLabel(action.Label, "quick action label");
                if (!quickActionIds.Add(action.Id))
                    throw new BridgeCatalogException($"Widget '{source.Id}' repeats quick action '{action.Id}'.");
            }
            var configured = WithFingerprints(source with
                {
                    WorkerExecutable = executable,
                    CompiledTheme = style.Theme,
                    StylePackage = style.Package,
                });
            if (!widgets.TryAdd(source.Id, configured))
                throw new BridgeCatalogException($"Widget ID '{source.Id}' is duplicated.");
            if (!packageIds.Add(source.PackageId))
                throw new BridgeCatalogException($"Package ID '{source.PackageId}' is duplicated.");
        }
        var workerHost = Path.GetFullPath(
            document.GenericWorkerExecutable.Replace('/', Path.DirectorySeparatorChar),
            directory);
        foreach (var source in document.BundledWidgets)
        {
            var configured = LoadBundledWidget(source, directory, workerHost);
            if (!widgets.TryAdd(configured.Id, configured))
                throw new BridgeCatalogException($"Widget ID '{configured.Id}' is duplicated.");
            if (!packageIds.Add(configured.PackageId))
                throw new BridgeCatalogException($"Package ID '{configured.PackageId}' is duplicated.");
        }
        return new BridgeCatalog(widgets.Values);
    }

    public static async Task<BridgeCatalogLoadResult> LoadWithInstalledAsync(
        string trustedCatalogPath,
        string installedCatalogRoot,
        string workerHostExecutable,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installedCatalogRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerHostExecutable);
        var installedRoot = Path.GetFullPath(installedCatalogRoot);
        var trusted = Load(trustedCatalogPath).WithSettingsCatalogRoot(installedRoot);
        var workerHost = Path.GetFullPath(workerHostExecutable);
        var warnings = new List<string>();
        WidgetCatalogSnapshot installed;
        try
        {
            installed = await new CatalogService(
                installedRoot).DiscoverAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is WidgetPackageException or IOException or UnauthorizedAccessException)
        {
            var code = exception is WidgetPackageException package ? package.Code : "catalog_unavailable";
            warnings.Add($"Installed widget catalog was ignored ({SafeDiagnostic(code)}).");
            return new BridgeCatalogLoadResult(trusted, warnings, InstalledCatalogValid: false);
        }

        var combined = trusted._ordered.ToList();
        var known = combined.Select(widget => widget.Id).ToHashSet(StringComparer.Ordinal);
        var knownPackages = combined.Select(widget => widget.PackageId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var widget in installed.Widgets.Where(item => item.Enabled))
        {
            if (combined.Count == 256)
            {
                warnings.Add("Enabled installed widgets exceeded the 256-widget host limit; remaining entries were ignored.");
                break;
            }
            var manifest = widget.ActiveVersion.Manifest;
            var authorityPublisherId = InstalledWidgetAuthority.PublisherId(
                widget.ActiveVersion);
            if (!IsBridgeIdentifier(manifest.Id) || !IsBridgeLabel(manifest.Name))
            {
                warnings.Add("An enabled installed widget had an ID or name outside bridge bounds and was ignored.");
                continue;
            }
            if (!known.Add(manifest.Id) || !knownPackages.Add(manifest.Id))
            {
                warnings.Add($"Installed widget '{SafeDiagnostic(manifest.Id)}' conflicts with a trusted widget and was ignored.");
                continue;
            }
            if (!WidgetHostCompatibility.Evaluate(manifest).IsSupported)
            {
                warnings.Add($"Installed widget '{SafeDiagnostic(manifest.Id)}' is incompatible with this host and was ignored.");
                continue;
            }
            var declaredCapabilities = manifest.Permissions
                .Concat(manifest.OptionalPermissions)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (declaredCapabilities.Any(capability =>
                    !PlatformCapabilities.IsManifestDeclarable(capability)))
            {
                warnings.Add($"Installed widget '{SafeDiagnostic(manifest.Id)}' declares an unsupported capability and was ignored.");
                continue;
            }
            if (!File.Exists(workerHost))
            {
                warnings.Add("Installed widgets are enabled, but the generic worker host is not packaged.");
                break;
            }
            var packageRoot = widget.ActiveVersion.InstallPath;
            var assembly = Path.GetFullPath(
                manifest.Entrypoint.Assembly.Replace('/', Path.DirectorySeparatorChar),
                packageRoot);
            var styleFile = widget.ActiveVersion.VerifiedGbssDigests.ContainsKey(
                "styles/default.gbss")
                ? "styles/default.gbss"
                : null;
            CompiledWidgetStyle style;
            try
            {
                style = CompileTheme(new ConfiguredWidget
                {
                    Id = manifest.Id,
                    PackageId = manifest.Id,
                    PublisherId = manifest.Publisher,
                    Name = manifest.Name,
                    InstanceId = InstalledWidgetInstanceIdentity.Derive(
                        manifest.Id, manifest.Version),
                    WorkerExecutable = workerHost,
                    WorkerArguments = [],
                    StyleFile = styleFile,
                }, packageRoot, widget.ActiveVersion.VerifiedGbssDigests);
            }
            catch (Exception exception) when (exception is BridgeCatalogException or IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Installed widget '{SafeDiagnostic(manifest.Id)}' has invalid styles and was ignored.");
                continue;
            }
            combined.Add(WithFingerprints(new ConfiguredWidget
            {
                Id = manifest.Id,
                PackageId = manifest.Id,
                PublisherId = authorityPublisherId,
                Name = manifest.Name,
                InstanceId = InstalledWidgetInstanceIdentity.Derive(
                    manifest.Id, manifest.Version),
                Icon = manifest.Presentation.Icon,
                PinningSupported = manifest.PinningSupported,
                AdvancedPresentation = manifest.AdvancedPresentation,
                WorkerExecutable = workerHost,
                WorkerArguments =
                [
                    "--package-root", packageRoot,
                    "--widget-assembly", assembly,
                    "--widget-type", manifest.Entrypoint.Type,
                ],
                DeclaredCapabilities = declaredCapabilities,
                RequiresAppContainer = true,
                IsolationKey = CommunityIsolationKey(authorityPublisherId, manifest.Id),
                AuthorityGeneration = manifest.Version,
                ReadOnlyPaths = [],
                ContentLeaseFactory = cancellationToken => AcquireInstalledContentLease(
                    installedRoot,
                    widget.ActiveVersion,
                    cancellationToken),
                UsesGenericWorkerHost = true,
                StyleFile = styleFile,
                MemoryRequestMb = manifest.ResourceRequest.MemoryMb,
                ResidencyPolicy = manifest.ResidencyPolicy ??
                    (manifest.BackgroundPolicy == "suspend"
                        ? new WidgetResidencyPolicy
                        {
                            Mode = WidgetResidencyPolicies.SuspendWhenHidden,
                        }
                        : new WidgetResidencyPolicy()),
                QuickActions = [],
                CompiledTheme = style.Theme,
                StylePackage = style.Package,
            }));
        }
        return new BridgeCatalogLoadResult(new BridgeCatalog(combined), warnings, InstalledCatalogValid: true);
    }

    private BridgeCatalog WithSettingsCatalogRoot(string installedCatalogRoot)
    {
        var updated = _ordered.Select(widget =>
        {
            if (widget.RequiresAppContainer ||
                !string.Equals(widget.Id, "settings", StringComparison.Ordinal) ||
                !string.Equals(widget.PackageId, "org.gbar.firstparty.settings", StringComparison.Ordinal) ||
                !string.Equals(widget.PublisherId, "org.gbar.firstparty", StringComparison.Ordinal))
                return widget;
            return WithFingerprints(widget with
            {
                WorkerArguments =
                [
                    .. widget.WorkerArguments,
                    "--installed-widget-catalog-root",
                    installedCatalogRoot,
                ],
            });
        });
        return new BridgeCatalog(updated);
    }

    private static ConfiguredWidget WithFingerprints(ConfiguredWidget source)
    {
        var workerFingerprint = Fingerprint(
        [
            source.Id,
            source.PackageId,
            source.PublisherId,
            source.InstanceId,
            source.WorkerExecutable,
            .. source.WorkerArguments,
            .. source.DeclaredCapabilities,
            source.RequiresAppContainer ? "appcontainer-required" : "host-trusted-job-only",
            source.IsolationKey ?? string.Empty,
            source.AuthorityGeneration ?? string.Empty,
            source.ContentLeaseFactory is null
                ? "broad-read-authority"
                : "verified-content-lease-v1",
            .. source.ReadOnlyPaths,
            source.MemoryRequestMb?.ToString(
                System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            source.ResidencyPolicy.SchemaVersion.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            source.ResidencyPolicy.Mode,
            source.ResidencyPolicy.IdleSeconds?.ToString(
                System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        ]);
        var catalogFingerprint = Fingerprint(
        [
            workerFingerprint,
            source.Name,
            source.Icon.ToString(),
            source.AdvancedPresentation?.SchemaVersion.ToString(
                System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            source.AdvancedPresentation?.Kind.ToString() ?? string.Empty,
            .. source.QuickActions.SelectMany(action => new[]
            {
                action.Id,
                action.Label,
                action.ActionId,
                action.SourceElementId,
                action.ControllerButton?.ToString() ?? string.Empty,
            }),
            .. CanonicalStyle(source.StylePackage),
        ]);
        return source with
        {
            WorkerFingerprint = workerFingerprint,
            CatalogFingerprint = catalogFingerprint,
        };
    }

    private static IEnumerable<string> CanonicalStyle(GbssPackageResult package)
    {
        foreach (var document in package.Documents)
        {
            yield return document.Source;
            foreach (var statement in document.Statements)
            {
                switch (statement)
                {
                case GbssImport import:
                    yield return "import";
                    yield return import.Path;
                    break;
                case GbssRule rule:
                    yield return "rule";
                    foreach (var selector in rule.Selectors) yield return selector.Text;
                    foreach (var declaration in rule.Declarations)
                    {
                        yield return declaration.Property;
                        yield return declaration.Value;
                    }
                    break;
                }
            }
        }
    }

    private static string Fingerprint(IEnumerable<string> values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)];
        foreach (var value in values)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
            hash.AppendData(length);
            hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string CommunityIsolationKey(string publisherId, string packageId) =>
        $"community-v2\n{publisherId}\n{packageId}";

    private static string BundledIsolationKey(string publisherId, string packageId) =>
        $"bundled-v1\n{publisherId}\n{packageId}";

    private static ConfiguredWidget LoadBundledWidget(
        BundledWidgetDefinition source,
        string catalogDirectory,
        string workerHost)
    {
        ValidateIdentifier(source.Id, "bundled widget ID");
        ValidatePackageIdentity(source.PackageId, "bundled package ID");
        ValidateIdentifier(source.InstanceId, "bundled widget instance ID");
        if (!IsSafePackageRelativePath(source.PackageRoot, allowDirectory: true))
            throw new BridgeCatalogException(
                $"Bundled widget '{source.Id}' has an invalid packageRoot.");
        if (source.QuickActions is null || source.QuickActions.Count > 16)
            throw new BridgeCatalogException(
                $"Bundled widget '{source.Id}' has too many quick actions.");
        ValidateQuickActions(source.Id, source.QuickActions);
        if (!File.Exists(workerHost))
            throw new BridgeCatalogException(
                $"The generic worker host for bundled widget '{source.Id}' does not exist.");
        EnsureNoReparsePoints(catalogDirectory, workerHost, "generic worker host");

        var packageRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
            source.PackageRoot.Replace('/', Path.DirectorySeparatorChar),
            catalogDirectory));
        if (!Directory.Exists(packageRoot))
            throw new BridgeCatalogException(
                $"Package root for bundled widget '{source.Id}' does not exist.");
        EnsureNoReparsePoints(catalogDirectory, packageRoot, "bundled package root");
        var manifestPath = Path.Combine(packageRoot, "manifest.json");
        if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length > 1024 * 1024)
            throw new BridgeCatalogException(
                $"Bundled widget '{source.Id}' is missing a bounded manifest.json.");
        EnsureNoReparsePoints(packageRoot, manifestPath, "bundled manifest");

        WidgetManifest manifest;
        try
        {
            manifest = ManifestJson.Deserialize(File.ReadAllBytes(manifestPath));
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new BridgeCatalogException(
                $"Bundled widget '{source.Id}' manifest is invalid.", exception);
        }
        var errors = WidgetManifestValidator.Validate(manifest);
        if (errors.Count != 0)
            throw new BridgeCatalogException(
                $"Bundled widget '{source.Id}' manifest is invalid ({errors[0].Code}).");
        if (!manifest.Id.Equals(manifest.Publisher, StringComparison.Ordinal) &&
            !manifest.Id.StartsWith(manifest.Publisher + ".", StringComparison.Ordinal))
            throw new BridgeCatalogException(
                $"Bundled widget '{source.Id}' manifest identity is outside its publisher namespace.");
        if (!string.Equals(manifest.Id, source.PackageId, StringComparison.Ordinal))
            throw new BridgeCatalogException(
                $"Bundled widget '{source.Id}' manifest does not match its pinned package ID.");
        if (!Version.TryParse(manifest.Version, out var version) ||
            !string.Equals(version.ToString(), manifest.Version, StringComparison.Ordinal))
            throw new BridgeCatalogException(
                $"Bundled widget '{source.Id}' manifest version is not canonical.");
        if (!WidgetHostCompatibility.Evaluate(manifest).IsSupported)
            throw new BridgeCatalogException(
                $"Bundled widget '{source.Id}' is incompatible with this host.");

        var declaredCapabilities = manifest.Permissions
            .Concat(manifest.OptionalPermissions)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (declaredCapabilities.Any(capability =>
                !PlatformCapabilities.IsManifestDeclarable(capability)))
            throw new BridgeCatalogException(
                $"Bundled widget '{source.Id}' declares an unsupported capability.");
        var assembly = Path.GetFullPath(
            manifest.Entrypoint.Assembly.Replace('/', Path.DirectorySeparatorChar),
            packageRoot);
        if (!IsWithin(packageRoot, assembly) || !File.Exists(assembly))
            throw new BridgeCatalogException(
                $"Bundled widget '{source.Id}' entrypoint is missing.");
        EnsureNoReparsePoints(packageRoot, assembly, "bundled entrypoint");

        var styleFile = File.Exists(Path.Combine(packageRoot, "styles", "default.gbss"))
            ? "styles/default.gbss"
            : null;
        var style = CompileTheme(new ConfiguredWidget
        {
            Id = source.Id,
            PackageId = manifest.Id,
            PublisherId = manifest.Publisher,
            Name = manifest.Name,
            InstanceId = source.InstanceId,
            WorkerExecutable = workerHost,
            StyleFile = styleFile,
        }, packageRoot);
        var residency = manifest.ResidencyPolicy ??
            (manifest.BackgroundPolicy == "suspend"
                ? new WidgetResidencyPolicy { Mode = WidgetResidencyPolicies.SuspendWhenHidden }
                : new WidgetResidencyPolicy());
        return WithFingerprints(new ConfiguredWidget
        {
            Id = source.Id,
            PackageId = manifest.Id,
            PublisherId = manifest.Publisher,
            Name = manifest.Name,
            InstanceId = source.InstanceId,
            Icon = manifest.Presentation.Icon,
            PinningSupported = manifest.PinningSupported,
            AdvancedPresentation = manifest.AdvancedPresentation,
            WorkerExecutable = workerHost,
            WorkerArguments =
            [
                "--package-root", packageRoot,
                "--widget-assembly", assembly,
                "--widget-type", manifest.Entrypoint.Type,
            ],
            DeclaredCapabilities = declaredCapabilities,
            RequiresAppContainer = true,
            IsolationKey = BundledIsolationKey(manifest.Publisher, manifest.Id),
            ReadOnlyPaths = [packageRoot],
            UsesGenericWorkerHost = true,
            StyleFile = styleFile,
            MemoryRequestMb = manifest.ResourceRequest.MemoryMb,
            ResidencyPolicy = residency,
            QuickActions = source.QuickActions,
            CompiledTheme = style.Theme,
            StylePackage = style.Package,
        });
    }

    private static void ValidateQuickActions(
        string widgetId,
        IReadOnlyList<BridgeQuickActionDescriptor> quickActions)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in quickActions)
        {
            ValidateIdentifier(action.Id, "quick action ID");
            ValidateIdentifier(action.ActionId, "action ID");
            ValidateIdentifier(action.SourceElementId, "source element ID");
            ValidateLabel(action.Label, "quick action label");
            if (!ids.Add(action.Id))
                throw new BridgeCatalogException(
                    $"Widget '{widgetId}' repeats quick action '{action.Id}'.");
        }
    }

    private static bool IsSafePackageRelativePath(string? path, bool allowDirectory)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) ||
            path.Contains('\\') || path.Any(char.IsControl))
            return false;
        var segments = path.Split('/');
        if (segments.Any(segment => segment is "" or "." or "..")) return false;
        return allowDirectory || Path.HasExtension(path);
    }

    private static IWidgetProcessContentLease AcquireInstalledContentLease(
        string installedCatalogRoot,
        InstalledWidgetVersion version,
        CancellationToken cancellationToken)
    {
        try
        {
            return new BridgeContentLease(InstalledPackageLaunchLease.Acquire(
                installedCatalogRoot,
                version,
                cancellationToken.ThrowIfCancellationRequested));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is WidgetPackageException or IOException or UnauthorizedAccessException)
        {
            throw new WidgetProcessAdmissionException(
                "Installed widget content changed before its worker could start.");
        }
    }

    private static bool IsWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) && relative is not "." and not ".." &&
            !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
            !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static void EnsureNoReparsePoints(string root, string path, string label)
    {
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var target = Path.GetFullPath(path);
        if (!IsWithinOrEqual(current, target))
            throw new BridgeCatalogException($"The {label} escaped its trusted root.");
        RejectReparse(current, label);
        var relative = Path.GetRelativePath(current, target);
        if (relative == ".") return;
        foreach (var segment in relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (File.Exists(current) || Directory.Exists(current)) RejectReparse(current, label);
        }
    }

    private static bool IsWithinOrEqual(string root, string path) =>
        string.Equals(root, path, StringComparison.OrdinalIgnoreCase) || IsWithin(root, path);

    private static void RejectReparse(string path, string label)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new BridgeCatalogException($"The {label} cannot contain reparse points.");
    }

    private static CompiledWidgetStyle CompileTheme(
        ConfiguredWidget source,
        string packageRoot,
        IReadOnlyDictionary<string, string>? expectedContentDigests = null)
    {
        if (source.StyleFile is null) return new(new GbssPackageResult([], []), null);
        if (!GbssPackageLoader.IsSafePackagePath(source.StyleFile))
            throw new BridgeCatalogException(
                $"Widget '{source.Id}' styleFile must be a normalized package-relative .gbss path.");
        var package = GbssPackageLoader.Load(
            source.StyleFile,
            new GbssFileSourceProvider(packageRoot, expectedContentDigests));
        var compiled = GbssThemeCompiler.Compile(package);
        if (compiled.IsValid) return new(package, compiled.Theme!);

        var diagnostics = compiled.Diagnostics
            .Where(item => item.Severity == GbssDiagnosticSeverity.Error)
            .Take(8)
            .Select(item =>
                $"{SafeDiagnostic(item.Source)}({item.Line},{item.Column}) {item.Code}: {SafeDiagnostic(item.Message)}")
            .ToArray();
        var summary = diagnostics.Length == 0
            ? "unknown GBSS compilation error"
            : string.Join("; ", diagnostics);
        throw new BridgeCatalogException($"Widget '{source.Id}' style is invalid: {summary}");
    }

    private sealed record CompiledWidgetStyle(GbssPackageResult Package, GbssTheme? Theme);

    private sealed class BridgeContentLease(InstalledPackageLaunchLease lease)
        : IWidgetProcessContentLease
    {
        public IReadOnlyList<AppContainerAuthorityExpectedTarget> Targets { get; } =
            lease.Targets.Select(MapTarget)
                .ToArray();

        public void Dispose() => lease.Dispose();

        private static AppContainerAuthorityExpectedTarget MapTarget(
            InstalledPackageContentTarget target)
        {
            var identity = target.ObjectIdentity ??
                throw new PlatformNotSupportedException(
                    "Installed content object identity requires Windows.");
            return new AppContainerAuthorityExpectedTarget(
                new AppContainerAuthorityTarget(
                    target.Path,
                    target.Kind switch
                    {
                        InstalledPackageContentTargetKind.AuthorityRootDirectory =>
                            AppContainerAuthorityTargetKind.AuthorityRootDirectory,
                        InstalledPackageContentTargetKind.ReadOnlyDirectory =>
                            AppContainerAuthorityTargetKind.VerifiedDirectory,
                        InstalledPackageContentTargetKind.ReadOnlyFile =>
                            AppContainerAuthorityTargetKind.VerifiedFile,
                        _ => throw new InvalidOperationException(
                            "Installed content target kind is unsupported."),
                    }),
                new AppContainerAuthorityObjectIdentity(
                    identity.VolumeSerialNumber,
                    identity.FileId));
        }
    }

    private static string SafeDiagnostic(string value)
    {
        var singleLine = value.Replace('\r', ' ').Replace('\n', ' ');
        return singleLine.Length <= 256 ? singleLine : singleLine[..256];
    }

    private static void ValidateIdentifier(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 ||
            !value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.'))
            throw new BridgeCatalogException($"The {label} is invalid.");
    }

    private static void ValidateLabel(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256)
            throw new BridgeCatalogException($"The {label} is invalid.");
    }

    private static void ValidatePackageIdentity(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 ||
            value.Split('.').Length < 2 ||
            !value.Split('.').All(segment => segment.Length > 0 &&
                char.IsAsciiLetterOrDigit(segment[0]) &&
                segment.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')))
            throw new BridgeCatalogException($"The {label} is invalid.");
    }

    private static bool IsBridgeIdentifier(string value) =>
        value.Length is > 0 and <= 128 &&
        value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.');

    private static bool IsBridgeLabel(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 256;

}

public sealed record BridgeCatalogLoadResult(
    BridgeCatalog Catalog,
    IReadOnlyList<string> Warnings,
    bool InstalledCatalogValid = true);

public sealed class BridgeCatalogException(string message, Exception? innerException = null)
    : Exception(message, innerException);
