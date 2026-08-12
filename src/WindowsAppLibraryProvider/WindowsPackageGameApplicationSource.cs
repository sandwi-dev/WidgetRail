using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace GameBarAlternative.WindowsAppLibraryProvider;

/// <summary>
/// Enumerates registered Windows packages through PackageManager and admits
/// only applications named by a bounded MicrosoftGame.config executable ID.
/// The package path and registration identifiers remain provider-private.
/// </summary>
internal sealed class WindowsPackageGameApplicationSource :
    IWindowsPackageGameApplicationSource
{
    internal const int MaximumPackages = 4_096;
    internal const int MaximumGames = 1_024;
    internal const int MaximumApplicationsPerPackage = 16;
    internal const int MaximumConfigBytes = 64 * 1024;
    private const string GameConfigFileName = "MicrosoftGame.config";
    private readonly IWindowsPackageRegistrationCatalog _catalog;

    internal WindowsPackageGameApplicationSource() : this(
        new WindowsPackageRegistrationCatalog())
    {
    }

    internal WindowsPackageGameApplicationSource(
        IWindowsPackageRegistrationCatalog catalog) =>
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    public IReadOnlyList<WindowsPackageGameRegistration> Enumerate(
        CancellationToken cancellationToken)
    {
        try
        {
            var packages = _catalog.Enumerate(cancellationToken);
            if (packages.Count > MaximumPackages)
                throw new WindowsPackageEnumerationException(
                    "Windows package registration exceeded its bounded catalog.");
            var result = new List<WindowsPackageGameRegistration>();
            foreach (var package in packages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.AddRange(Classify(package, cancellationToken));
                if (result.Count > MaximumGames)
                    throw new WindowsPackageEnumerationException(
                        "Windows game registration exceeded its bounded catalog.");
            }
            return result
                .GroupBy(item => item.IdentityKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderBy(item => item.PackageFullName,
                        StringComparer.Ordinal)
                    .ThenBy(item => item.Aumid, StringComparer.Ordinal)
                    .First())
                .OrderBy(item => item.IdentityKey, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WindowsPackageEnumerationException)
        {
            throw;
        }
        catch (Exception exception) when (IsRegistrationFailure(exception))
        {
            throw new WindowsPackageEnumerationException(
                "Windows package registration could not produce an authoritative catalog.",
                exception);
        }
    }

    public WindowsPackageGameRegistration? ReadExact(
        string packageFullName,
        string aumid,
        CancellationToken cancellationToken)
    {
        if (!IsBoundedIdentifier(packageFullName, 256) ||
            WindowsAppsFolderApplicationSource.NormalizeAumid(aumid) is null)
            return null;
        try
        {
            var package = _catalog.ReadExact(packageFullName, cancellationToken);
            if (package is null || !string.Equals(
                    package.PackageFullName, packageFullName, StringComparison.Ordinal))
                return null;
            return Classify(package, cancellationToken).SingleOrDefault(item =>
                string.Equals(item.Aumid, aumid, StringComparison.OrdinalIgnoreCase));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsRegistrationFailure(exception))
        {
            return null;
        }
    }

    private static IReadOnlyList<WindowsPackageGameRegistration> Classify(
        WindowsPackageRegistrationCandidate package,
        CancellationToken cancellationToken)
    {
        if (!IsCandidateValid(package) ||
            package.Applications.Count > MaximumApplicationsPerPackage)
            return [];
        var config = ReadGameConfig(package.InstalledLocation, cancellationToken);
        if (config is null) return [];
        var executableIds = ParseExecutableIds(config);
        if (executableIds.Count == 0) return [];

        var configRevision = Convert.ToHexString(SHA256.HashData(config));
        var result = new List<WindowsPackageGameRegistration>();
        foreach (var application in package.Applications)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalized = WindowsAppsFolderApplicationSource.NormalizeAumid(
                application.Aumid);
            if (normalized is null ||
                !string.Equals(normalized, application.Aumid, StringComparison.Ordinal) ||
                !TryApplicationId(normalized, out var applicationId) ||
                !executableIds.Contains(applicationId) ||
                WindowsAppLibraryProvider.SanitizeDisplayName(application.DisplayName) is
                    not { } displayName)
                continue;
            var identity = WindowsAppsFolderApplicationSource.IdentityFor(normalized);
            var revalidation = "package-" + Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(package.PackageFullName + "\0" + normalized +
                    "\0" + configRevision)));
            result.Add(new(
                identity,
                displayName,
                package.PackageFamilyName,
                package.PackageFullName,
                normalized,
                revalidation));
        }
        return result;
    }

    private static byte[]? ReadGameConfig(
        string installedLocation,
        CancellationToken cancellationToken)
    {
        if (!Path.IsPathFullyQualified(installedLocation) ||
            new DirectoryInfo(installedLocation).Attributes.HasFlag(
                FileAttributes.ReparsePoint)) return null;
        var root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(installedLocation));
        var path = Path.GetFullPath(Path.Combine(root, GameConfigFileName));
        if (!string.Equals(Path.GetRelativePath(root, path), GameConfigFileName,
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path) || File.GetAttributes(path).HasFlag(
                FileAttributes.ReparsePoint))
            return null;
        var before = new FileInfo(path);
        if (before.Length is <= 0 or > MaximumConfigBytes) return null;
        var beforeLength = before.Length;
        var beforeWrite = before.LastWriteTimeUtc;
        cancellationToken.ThrowIfCancellationRequested();
        byte[] bytes;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                   FileShare.ReadWrite | FileShare.Delete, 4096,
                   FileOptions.SequentialScan))
        {
            bytes = new byte[beforeLength];
            var offset = 0;
            while (offset < bytes.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read == 0) return null;
                offset += read;
            }
            if (stream.ReadByte() != -1) return null;
        }
        var after = new FileInfo(path);
        return after.Exists && after.Length == beforeLength &&
               after.LastWriteTimeUtc == beforeWrite
            ? bytes
            : null;
    }

    private static HashSet<string> ParseExecutableIds(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaximumConfigBytes,
                MaxCharactersFromEntities = 0,
            });
            var document = XDocument.Load(reader, LoadOptions.None);
            if (!string.Equals(document.Root?.Name.LocalName, "Game",
                    StringComparison.Ordinal))
                return [];
            return document.Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "Executable",
                    StringComparison.Ordinal))
                .Select(element => element.Attributes().FirstOrDefault(attribute =>
                    string.Equals(attribute.Name.LocalName, "Id",
                        StringComparison.Ordinal))?.Value)
                .Where(value => IsBoundedApplicationId(value))
                .Select(value => value!)
                .Take(MaximumApplicationsPerPackage + 1)
                .ToHashSet(StringComparer.OrdinalIgnoreCase) is { Count: <=
                    MaximumApplicationsPerPackage } ids ? ids : [];
        }
        catch (Exception exception) when (exception is XmlException or
            InvalidOperationException or ArgumentException)
        {
            return [];
        }
    }

    private static bool TryApplicationId(string aumid, out string applicationId)
    {
        var separator = aumid.LastIndexOf('!');
        applicationId = separator >= 0 ? aumid[(separator + 1)..] : string.Empty;
        return IsBoundedApplicationId(applicationId);
    }

    private static bool IsCandidateValid(WindowsPackageRegistrationCandidate package) =>
        package is not null &&
        IsBoundedIdentifier(package.PackageFamilyName, 256) &&
        IsBoundedIdentifier(package.PackageFullName, 256) &&
        Path.IsPathFullyQualified(package.InstalledLocation) &&
        package.Applications is not null;

    private static bool IsBoundedIdentifier(string? value, int maximum) =>
        value is { Length: > 0 } && value.Length <= maximum &&
        !value.Any(char.IsControl);

    private static bool IsBoundedApplicationId(string? value) =>
        value is { Length: > 0 and <= 64 } && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    private static bool IsRegistrationFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.Security.SecurityException or
            System.Runtime.InteropServices.COMException or ArgumentException or
            NotSupportedException;
}

internal sealed class WindowsPackageRegistrationCatalog :
    IWindowsPackageRegistrationCatalog
{
    public IReadOnlyList<WindowsPackageRegistrationCandidate> Enumerate(
        CancellationToken cancellationToken)
    {
        var manager = new PackageManager();
        var result = new List<WindowsPackageRegistrationCandidate>();
        var visited = 0;
        foreach (var package in manager.FindPackagesForUser(string.Empty))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++visited > WindowsPackageGameApplicationSource.MaximumPackages)
                throw new WindowsPackageEnumerationException(
                    "Windows package registration exceeded its bounded catalog.");
            if (TryProject(package, cancellationToken) is { } candidate)
                result.Add(candidate);
        }
        return result;
    }

    public WindowsPackageRegistrationCandidate? ReadExact(
        string packageFullName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var package = new PackageManager().FindPackageForUser(
            string.Empty, packageFullName);
        return package is null ? null : TryProject(package, cancellationToken);
    }

    private static WindowsPackageRegistrationCandidate? TryProject(
        Package package,
        CancellationToken cancellationToken)
    {
        if (package.IsFramework || package.IsResourcePackage || package.IsBundle ||
            !package.Status.VerifyIsOK())
            return null;
        var fullName = package.Id.FullName;
        var familyName = package.Id.FamilyName;
        var location = package.InstalledLocation?.Path;
        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(familyName) ||
            string.IsNullOrWhiteSpace(location))
            return null;
        var applications = package.GetAppListEntriesAsync()
            .AsTask(cancellationToken).GetAwaiter().GetResult()
            .Take(WindowsPackageGameApplicationSource.MaximumApplicationsPerPackage + 1)
            .Select(entry => new WindowsPackageApplicationRegistration(
                entry.AppUserModelId,
                entry.DisplayInfo.DisplayName))
            .ToArray();
        return applications.Length <=
               WindowsPackageGameApplicationSource.MaximumApplicationsPerPackage
            ? new(familyName, fullName, location, applications)
            : null;
    }
}
