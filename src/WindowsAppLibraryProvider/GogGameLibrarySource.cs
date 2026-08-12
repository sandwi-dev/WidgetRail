using System.Security.Cryptography;
using System.Text;
using GameBarAlternative.PlatformBroker;

namespace GameBarAlternative.WindowsAppLibraryProvider;

internal sealed class GogGameLibrarySource : GameLibrarySourceBase
{
    internal const string StableSourceIdentity = "source-gog-installed";
    private readonly IGogApplicationSource _source;
    private readonly IWindowsGogLauncher _launcher;

    internal GogGameLibrarySource(
        IGogApplicationSource source,
        IWindowsGogLauncher launcher) : base(StableSourceIdentity, "GOG")
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
                authorities.Add(item.SourceItemIdentity, new GogAuthority(registration));
                return item;
            }).ToArray();
        return new(candidate.Health, Array.AsReadOnly(items), authorities);
    }

    protected override GameLibrarySourceItem? ResolveExactCore(
        GameLibrarySourceItem item,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthority<GogAuthority>(item, out var expected)) return null;
        var registration = expected.Registration;
        var current = _source.ReadExact(registration.RegistryView,
            registration.RegistryKeyName, registration.ProductId, cancellationToken);
        return current is not null && IsValid(current) && ExactMatch(registration, current)
            ? ToItem(current)
            : null;
    }

    protected override GameLibraryLaunchResult LaunchCore(
        GameLibrarySourceItem exactItem,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthority<GogAuthority>(exactItem, out var authority))
            throw new InvalidOperationException("GOG launch authority is invalid.");
        _launcher.Launch(authority.Registration.ProductId,
            authority.Registration.InstallLocation, cancellationToken);
        return new(AppLibraryLaunchObservationState.LauncherStarted,
            GameLibraryLaunchEvidence.LauncherStarted);
    }

    protected override string? LoadArtworkCore(
        GameLibrarySourceItem exactItem,
        CancellationToken cancellationToken) => null;

    private GameLibrarySourceItem ToItem(GogGameRegistration registration) => new(
        SourceIdentity, Attribution, registration.IdentityKey,
        registration.DisplayName, WindowsAppLibraryKind.Game,
        Installed: true, Available: true, GameLibrarySourceActions.Launch,
        registration.RevalidationKey, SourceItemIdentity(registration));

    private string SourceItemIdentity(GogGameRegistration registration) =>
        "record-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            SourceIdentity + "\0" + registration.IdentityKey + "\0" +
            registration.RevalidationKey)));

    private static bool ExactMatch(
        GogGameRegistration expected,
        GogGameRegistration current) =>
        string.Equals(expected.IdentityKey, current.IdentityKey, StringComparison.Ordinal) &&
        string.Equals(expected.ProductId, current.ProductId, StringComparison.Ordinal) &&
        string.Equals(expected.RegistryView, current.RegistryView, StringComparison.Ordinal) &&
        string.Equals(expected.RegistryKeyName, current.RegistryKeyName,
            StringComparison.Ordinal) &&
        string.Equals(expected.InstallLocation, current.InstallLocation,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(expected.InfoPath, current.InfoPath,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(expected.RevalidationKey, current.RevalidationKey,
            StringComparison.Ordinal);

    private static bool IsValid(GogGameRegistration registration) =>
        registration.IdentityKey is { Length: > 0 and <= 128 } &&
        WindowsAppLibraryProvider.SanitizeDisplayName(registration.DisplayName) ==
            registration.DisplayName &&
        registration.ProductId is { Length: > 0 and <= 20 } &&
        registration.ProductId.All(char.IsAsciiDigit) &&
        registration.RevalidationKey is { Length: > 0 and <= 128 };

    private static IReadOnlyDictionary<string, IGameLibrarySourceAuthority>
        EmptyAuthorities() => new Dictionary<string, IGameLibrarySourceAuthority>(
            StringComparer.Ordinal);

    private sealed record GogAuthority(GogGameRegistration Registration) :
        IGameLibrarySourceAuthority;
}
