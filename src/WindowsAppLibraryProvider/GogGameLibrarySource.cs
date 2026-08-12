using System.Security.Cryptography;
using System.Text;
using GameBarAlternative.PlatformBroker;

namespace GameBarAlternative.WindowsAppLibraryProvider;

internal sealed class GogGameLibrarySource : GameLibrarySourceBase
{
    internal const string StableSourceIdentity = "source-gog-installed";
    private readonly IGogApplicationSource _source;

    internal GogGameLibrarySource(IGogApplicationSource source) :
        base(StableSourceIdentity, "GOG")
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
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

        var items = candidate.Registrations.Where(IsValid)
            .Select(ToItem).ToArray();
        return new(candidate.Health, Array.AsReadOnly(items), EmptyAuthorities());
    }

    protected override GameLibrarySourceItem? ResolveExactCore(
        GameLibrarySourceItem item,
        CancellationToken cancellationToken) => null;

    protected override GameLibraryLaunchResult LaunchCore(
        GameLibrarySourceItem exactItem,
        CancellationToken cancellationToken) =>
        throw new InvalidOperationException("GOG installed evidence is not launchable.");

    protected override string? LoadArtworkCore(
        GameLibrarySourceItem exactItem,
        CancellationToken cancellationToken) => null;

    private GameLibrarySourceItem ToItem(GogGameRegistration registration) => new(
        SourceIdentity, Attribution, registration.IdentityKey,
        registration.DisplayName, WindowsAppLibraryKind.Game,
        Installed: true, Available: true, GameLibrarySourceActions.None,
        registration.RevalidationKey, SourceItemIdentity(registration));

    private string SourceItemIdentity(GogGameRegistration registration) =>
        "record-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            SourceIdentity + "\0" + registration.IdentityKey + "\0" +
            registration.RevalidationKey)));

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
}
