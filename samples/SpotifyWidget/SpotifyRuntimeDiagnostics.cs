using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.SpotifyWidget;

internal interface ISpotifyRuntimeDiagnostics
{
    void Record(string boundary, string code);
}

internal static class SpotifyRuntimeDiagnostics
{
    internal static ISpotifyRuntimeDiagnostics None { get; } =
        new NullSpotifyRuntimeDiagnostics();

    internal static string Code(Exception exception) => exception switch
    {
        ProtocolValidationException validation =>
            validation.Errors.FirstOrDefault()?.Code ?? "protocol-validation",
        OperationCanceledException => "operation-canceled",
        InvalidOperationException => "invalid-operation",
        ArgumentException => "invalid-argument",
        IOException => "io-failure",
        _ => "unexpected-failure",
    };

    private sealed class NullSpotifyRuntimeDiagnostics : ISpotifyRuntimeDiagnostics
    {
        public void Record(string boundary, string code)
        {
        }
    }
}
