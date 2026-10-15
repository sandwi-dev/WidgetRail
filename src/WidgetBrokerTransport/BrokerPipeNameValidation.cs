namespace WidgetRail.PlatformBroker;

internal static class BrokerPipeNameValidation
{
    internal static void Validate(string pipeName)
    {
        if (string.IsNullOrWhiteSpace(pipeName) || pipeName.Length > 200)
            throw new ArgumentException("Broker pipe name is invalid.", nameof(pipeName));
        if (pipeName.Contains('\\') || pipeName.Any(char.IsControl))
            throw new ArgumentException("Broker pipe name is invalid.", nameof(pipeName));
    }

}
