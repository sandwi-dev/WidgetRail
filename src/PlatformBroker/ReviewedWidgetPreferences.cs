namespace WidgetRail.PlatformBroker;

public static class ReviewedWidgetPreferences
{
    /// <summary>Checks continuity for a saved preference. Commands still require the selected package's exact authority.</summary>
    public static async Task<bool> SharesScopeAsync(string consentRoot, string packageId,
        string previousPublisher, string currentPublisher, CancellationToken token)
    {
        static bool IsContentAuthority(string value) => value.Length == 73 &&
            value.StartsWith("unsigned.", StringComparison.Ordinal) && value.AsSpan(9).IndexOfAnyExcept("0123456789abcdef") < 0;
        if (!IsContentAuthority(previousPublisher) || !IsContentAuthority(currentPublisher)) return false;
        var scopes = new AppLibraryContinuityStore(consentRoot);
        var previous = new BrokerWidgetIdentity(packageId, previousPublisher, "settings-transfer");
        var current = previous with { PublisherId = currentPublisher };
        var previousScope = await scopes.ResolveAsync(previous, token).ConfigureAwait(false);
        var currentScope = await scopes.ResolveAsync(current, token).ConfigureAwait(false);
        return previousScope == currentScope;
    }
}
