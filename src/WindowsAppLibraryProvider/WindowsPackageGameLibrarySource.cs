using System.Security.Cryptography;
using System.Text;
using GameBarAlternative.PlatformBroker;

namespace GameBarAlternative.WindowsAppLibraryProvider;

/// <summary>
/// Owns the installed Microsoft/Xbox game source generation and exact launch
/// revalidation. Catalog failures retain display state but never mint current
/// launch authority from stale registration data.
/// </summary>
internal sealed class WindowsPackageGameLibrarySource : GameLibrarySourceBase
{
    internal const string StableSourceIdentity = "source-microsoft-games-installed";
    private readonly IWindowsPackageGameApplicationSource _source;
    private readonly IWindowsPackagedAppLauncher _launcher;
    private readonly IWindowsAppIconSource _iconSource;

    internal WindowsPackageGameLibrarySource(
        IWindowsPackageGameApplicationSource source,
        IWindowsPackagedAppLauncher launcher,
        IWindowsAppIconSource iconSource) : base(
            StableSourceIdentity, "Xbox / Microsoft Store")
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _iconSource = iconSource ?? throw new ArgumentNullException(nameof(iconSource));
    }

    public override bool RequiresStaArtwork => true;

    protected override GameLibrarySourceCandidate RefreshCore(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<WindowsPackageGameRegistration> registrations;
        try
        {
            registrations = _source.Enumerate(cancellationToken);
        }
        catch (WindowsPackageEnumerationException)
        {
            return new(GameLibrarySourceHealth.Unavailable, Snapshot.Items,
                new Dictionary<string, IGameLibrarySourceAuthority>(StringComparer.Ordinal));
        }
        var authorities = new Dictionary<string, IGameLibrarySourceAuthority>(
            StringComparer.Ordinal);
        var items = registrations
            .Where(IsValid)
            .GroupBy(item => item.IdentityKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(item => item.PackageFullName,
                    StringComparer.Ordinal)
                .ThenBy(item => item.Aumid, StringComparer.Ordinal)
                .First())
            .Select(registration =>
            {
                var item = ToItem(registration);
                authorities.Add(item.SourceItemIdentity,
                    new PackageAuthority(registration));
                return item;
            })
            .ToArray();
        return new(GameLibrarySourceHealth.Healthy,
            Array.AsReadOnly(items), authorities);
    }

    protected override GameLibrarySourceItem? ResolveExactCore(
        GameLibrarySourceItem item,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthority<PackageAuthority>(item, out var expected)) return null;
        var current = _source.ReadExact(
            expected.Registration.PackageFullName,
            expected.Registration.Aumid,
            cancellationToken);
        return current is not null && IsValid(current) && ExactMatch(
            expected.Registration, current)
            ? ToItem(current)
            : null;
    }

    protected override GameLibraryLaunchResult LaunchCore(
        GameLibrarySourceItem exactItem,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthority<PackageAuthority>(exactItem, out var authority))
            throw new InvalidOperationException(
                "Windows package game authority is invalid.");
        _launcher.Launch(authority.Registration.Aumid, cancellationToken);
        return new(AppLibraryLaunchObservationState.LauncherStarted,
            GameLibraryLaunchEvidence.LauncherStarted);
    }

    protected override string? LoadArtworkCore(
        GameLibrarySourceItem exactItem,
        CancellationToken cancellationToken) =>
        TryGetAuthority<PackageAuthority>(exactItem, out var authority)
            ? _iconSource.TryRasterizeAppsFolderPngBase64(
                authority.Registration.Aumid, cancellationToken)
            : null;

    private GameLibrarySourceItem ToItem(WindowsPackageGameRegistration item) => new(
        SourceIdentity,
        Attribution,
        item.IdentityKey,
        item.DisplayName,
        WindowsAppLibraryKind.Game,
        Installed: true,
        Available: true,
        GameLibrarySourceActions.Launch | GameLibrarySourceActions.Artwork,
        item.RevalidationKey,
        SourceItemIdentity(item));

    private string SourceItemIdentity(WindowsPackageGameRegistration item) =>
        "record-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            SourceIdentity + "\0" + item.IdentityKey + "\0" +
            item.RevalidationKey)));

    private static bool ExactMatch(
        WindowsPackageGameRegistration expected,
        WindowsPackageGameRegistration current) =>
        string.Equals(expected.IdentityKey, current.IdentityKey,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(expected.PackageFamilyName, current.PackageFamilyName,
            StringComparison.Ordinal) &&
        string.Equals(expected.PackageFullName, current.PackageFullName,
            StringComparison.Ordinal) &&
        string.Equals(expected.Aumid, current.Aumid,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(expected.RevalidationKey, current.RevalidationKey,
            StringComparison.Ordinal);

    private static bool IsValid(WindowsPackageGameRegistration registration) =>
        registration is not null &&
        registration.IdentityKey is { Length: > 0 and <= 128 } &&
        registration.DisplayName is { Length: > 0 and <= 120 } &&
        WindowsAppLibraryProvider.SanitizeDisplayName(registration.DisplayName) ==
            registration.DisplayName &&
        registration.PackageFamilyName is { Length: > 0 and <= 256 } &&
        registration.PackageFullName is { Length: > 0 and <= 256 } &&
        WindowsAppsFolderApplicationSource.NormalizeAumid(registration.Aumid) is
            { } normalized && string.Equals(normalized, registration.Aumid,
                StringComparison.Ordinal) &&
        registration.RevalidationKey is { Length: > 0 and <= 128 };

    private sealed record PackageAuthority(
        WindowsPackageGameRegistration Registration) : IGameLibrarySourceAuthority;
}
