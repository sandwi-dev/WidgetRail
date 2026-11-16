using WidgetRail.Samples.PlayniteLibrary;
namespace WidgetRail.Tests.PlayniteLibraryCommunityApplication;
internal static class LivePlayniteConnection
{
    internal static PlayniteLocalConnectionStore Source() => new(
        Environment.GetEnvironmentVariable("WRAIL_TEST_PLAYNITE_STATE") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WidgetRail", "community-apps", "widgetrail.samples.playnite-library"));
}
