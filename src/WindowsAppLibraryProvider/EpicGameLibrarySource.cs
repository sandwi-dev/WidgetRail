using System.Security.Cryptography;
using System.Text;
using WidgetRail.PlatformBroker;

namespace WidgetRail.WindowsAppLibraryProvider;

internal sealed class EpicGameLibrarySource : GameLibrarySourceBase
{
    internal const string StableSourceIdentity = "source-epic-installed";
    private readonly IEpicApplicationSource _source;
    private readonly IWindowsEpicLauncher _launcher;

    internal EpicGameLibrarySource(
        IEpicApplicationSource source,
        IWindowsEpicLauncher launcher) : base(StableSourceIdentity, "Epic")
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
    }

    public override bool RequiresStaArtwork => false;

    protected override GameLibrarySourceCandidate RefreshCore(
        CancellationToken cancellationToken)
    {
        var candidate = _source.Enumerate(cancellationToken);
        if (candidate.Health is GameLibrarySourceHealth.Disabled)
            return new(candidate.Health, [], EmptyAuthorities());
        if (candidate.Health is GameLibrarySourceHealth.Unavailable)
            return new(candidate.Health, Snapshot.Items, EmptyAuthorities());

        var authorities = new Dictionary<string, IGameLibrarySourceAuthority>(
            StringComparer.Ordinal);
        var items = candidate.Registrations.Where(IsValid)
            .Select(registration =>
            {
                var item = ToItem(registration);
                authorities.Add(item.SourceItemIdentity, new EpicAuthority(registration));
                return item;
            }).ToArray();
        return new(candidate.Health, Array.AsReadOnly(items), authorities);
    }

    protected override GameLibrarySourceItem? ResolveExactCore(
        GameLibrarySourceItem item,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthority<EpicAuthority>(item, out var expected)) return null;
        var current = _source.ReadExact(expected.Registration.ManifestPath,
            expected.Registration.AppName, cancellationToken);
        return current is not null && IsValid(current) && ExactMatch(
            expected.Registration, current) ? ToItem(current) : null;
    }

    protected override GameLibraryLaunchResult LaunchCore(
        GameLibrarySourceItem exactItem,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthority<EpicAuthority>(exactItem, out var authority))
            throw new InvalidOperationException("Epic launch authority is invalid.");
        _launcher.Launch(authority.Registration.CatalogNamespace,
            authority.Registration.CatalogItemId,
            authority.Registration.AppName, cancellationToken);
        return new(AppLibraryLaunchObservationState.LauncherStarted,
            GameLibraryLaunchEvidence.LauncherStarted);
    }

    protected override string? LoadArtworkCore(
        GameLibrarySourceItem exactItem,
        CancellationToken cancellationToken) => null;

    private GameLibrarySourceItem ToItem(EpicGameRegistration registration) => new(
        SourceIdentity, Attribution, registration.IdentityKey,
        registration.DisplayName, WindowsAppLibraryKind.Game,
        Installed: true, Available: true, GameLibrarySourceActions.Launch,
        registration.RevalidationKey, SourceItemIdentity(registration));

    private string SourceItemIdentity(EpicGameRegistration registration) =>
        "record-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            SourceIdentity + "\0" + registration.IdentityKey + "\0" +
            registration.RevalidationKey)));

    private static bool ExactMatch(EpicGameRegistration expected,
        EpicGameRegistration current) =>
        string.Equals(expected.IdentityKey, current.IdentityKey,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(expected.AppName, current.AppName, StringComparison.Ordinal) &&
        string.Equals(expected.CatalogNamespace, current.CatalogNamespace,
            StringComparison.Ordinal) &&
        string.Equals(expected.CatalogItemId, current.CatalogItemId,
            StringComparison.Ordinal) &&
        string.Equals(expected.InstallLocation, current.InstallLocation,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(expected.LaunchExecutable, current.LaunchExecutable,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(expected.RevalidationKey, current.RevalidationKey,
            StringComparison.Ordinal);

    private static bool IsValid(EpicGameRegistration registration) =>
        registration.IdentityKey is { Length: > 0 and <= 128 } &&
        WindowsAppLibraryProvider.SanitizeDisplayName(registration.DisplayName) ==
            registration.DisplayName &&
        registration.RevalidationKey is { Length: > 0 and <= 128 };

    private static IReadOnlyDictionary<string, IGameLibrarySourceAuthority>
        EmptyAuthorities() => new Dictionary<string, IGameLibrarySourceAuthority>(
            StringComparer.Ordinal);

    private sealed record EpicAuthority(EpicGameRegistration Registration) :
        IGameLibrarySourceAuthority;
}
