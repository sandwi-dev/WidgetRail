using WidgetRail.WidgetProtocol;
namespace WidgetRail.Samples.GameHelp;

public interface IGameHelpService
{
    Task<bool> HasKeyAsync(CancellationToken token);
    Task SaveKeyAsync(string key, CancellationToken token);
    Task DeleteKeyAsync(CancellationToken token);
    Task<GameHelpAnswer> AskAsync(GameHelpRequest request, CancellationToken token);
}
