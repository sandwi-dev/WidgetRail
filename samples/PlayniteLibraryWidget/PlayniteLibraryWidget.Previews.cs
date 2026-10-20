using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.PlayniteLibrary;

public sealed partial class PlayniteLibraryWidget
{
    private sealed record AchievementPreviewContext(long Generation, PlayniteDetailsExtras State);
    private AchievementPreviewContext? achievementPreviewContext;
    private WidgetFocusPreviewSource<AchievementPreviewContext>? achievementPreviews;

    private IndexedCollectionDescriptor? PrepareAchievementPreviews(PlayniteLibraryRenderState local)
    {
        if (local.DetailsExtras.Tab != PlayniteDetailsTab.Achievements || local.DetailsExtras.Achievements is not { Available: true })
            return null;
        if (achievementPreviewContext?.Generation != local.DetailsGeneration ||
            !ReferenceEquals(achievementPreviewContext.State, local.DetailsExtras))
        {
            achievementPreviewContext = new(local.DetailsGeneration, local.DetailsExtras);
            if (achievementPreviews is null)
                achievementPreviews = CreateFocusPreviewSource("playnite-library.achievement-previews", achievementPreviewContext,
                    new WidgetFocusPreviewOptions<AchievementPreviewContext>
                    {
                        Resolve = (context, key, token) =>
                        {
                            token.ThrowIfCancellationRequested();
                            const string prefix = "achievement.";
                            if (!key.StartsWith(prefix, StringComparison.Ordinal) || !int.TryParse(key[prefix.Length..], out var index) ||
                                index < 0 || index >= context.State.Achievements!.Items.Count)
                                throw new ArgumentException("Unknown achievement preview key.");
                            return ValueTask.FromResult(PlayniteLibraryDetailsPresentation.AchievementPreview(context.State, index));
                        },
                        ResolveArtworkSource = (context, key, handle, token) =>
                        {
                            token.ThrowIfCancellationRequested();
                            const string prefix = "achievement.";
                            if (!key.StartsWith(prefix, StringComparison.Ordinal) || !int.TryParse(key[prefix.Length..], out var index) ||
                                index < 0 || index >= context.State.Achievements!.Items.Count ||
                                handle != PlayniteLibraryDetailsPresentation.AchievementIconHandle(index))
                                return ValueTask.FromResult<WidgetArtworkSource?>(null);
                            var item = context.State.Achievements.Items[index];
                            var secret = item.Hidden && !item.Unlocked && !context.State.RevealedAchievements.Contains(index);
                            return ValueTask.FromResult<WidgetArtworkSource?>(!secret && item.Icon is { } path && ArtworkFileContract.IsLocalPath(path)
                                ? new WidgetFileArtwork(path, context.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture)) : null);
                        },
                    });
            else achievementPreviews.Publish(achievementPreviewContext);
        }
        return achievementPreviews!.Descriptor;
    }
}
