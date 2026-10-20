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
                    });
            else achievementPreviews.Publish(achievementPreviewContext);
        }
        return achievementPreviews!.Descriptor;
    }
}
