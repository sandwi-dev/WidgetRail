using System.Text.Json;
using WidgetRail.WidgetSdk;

namespace WidgetRail.Samples.GameHelp;

internal sealed record GameHelpPreferences(bool Grounding = false, string Spoilers = "Hints first");

public sealed partial class GameHelpWidget
{
    private bool preferencesLoaded;
    private long preferenceEdit;
    private async Task LoadPreferencesAsync(CancellationToken token)
    {
        if (preferencesLoaded) return;
        var edit = Interlocked.Read(ref preferenceEdit);
        try
        {
            var stored = await HostServices.PrivateState.ReadAsync<GameHelpPreferences>(cancellationToken: token);
            var value = stored.Value ?? new();
            if (value.Spoilers is not ("Hints first" or "Balanced" or "Full solution")) value = new(value.Grounding);
            model.Update(state => Interlocked.Read(ref preferenceEdit) == edit
                ? state with { Grounding = value.Grounding, Spoilers = value.Spoilers } : state);
            preferencesLoaded = true;
        }
        catch (Exception error) when (error is WidgetCapabilityException or WidgetCapabilityUnavailableException or JsonException)
        { preferencesLoaded = true; }
    }
    private void SetPreferences(bool? grounding = null, string? spoilers = null)
    {
        Interlocked.Increment(ref preferenceEdit);
        model.Update(state => state with { Grounding = grounding ?? state.Grounding, Spoilers = spoilers ?? state.Spoilers, Error = null });
        if (!HostServices.Capabilities.IsAvailable) return;
        Operations.RunLatest("preferences", async context =>
        {
            try
            {
                var state = model.Value;
                await HostServices.PrivateState.WriteAsync(new GameHelpPreferences(state.Grounding, state.Spoilers), cancellationToken: context.CancellationToken);
            }
            catch (Exception error) when (error is WidgetCapabilityException or WidgetCapabilityUnavailableException)
            { if (context.IsCurrent) model.Update(state => state with { Error = "Could not save your preferences. Try the setting again." }); }
        }, WidgetOperationLifetime.Widget);
    }
}
