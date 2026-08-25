using System.Globalization;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.Samples.SpotifyWidget;

internal interface ISpotifyRuntimeDiagnostics
{
    void Record(
        string boundary,
        string code,
        long operation = 0,
        long generation = 0,
        long elapsedMilliseconds = 0);
}

internal static class SpotifyRuntimeDiagnostics
{
    internal static ISpotifyRuntimeDiagnostics None { get; } =
        new NullSpotifyRuntimeDiagnostics();

    internal static string Code(Exception exception) => exception switch
    {
        ProtocolValidationException validation =>
            validation.Errors.FirstOrDefault()?.Code ?? "protocol-validation",
        SpotifyApplicationException application => application.Code,
        OperationCanceledException => "operation-canceled",
        InvalidOperationException => "invalid-operation",
        ArgumentException => "invalid-argument",
        IOException => "io-failure",
        _ => "unexpected-failure",
    };

    internal static bool TryEncode(
        DateTimeOffset timestamp,
        string boundary,
        string code,
        long operation,
        long generation,
        long elapsedMilliseconds,
        out string line)
    {
        if (!IsToken(boundary) || !IsToken(code) || operation < 0 ||
            generation < 0 || elapsedMilliseconds < 0)
        {
            line = string.Empty;
            return false;
        }

        line = string.Concat(
            timestamp.ToString("O", CultureInfo.InvariantCulture),
            " level=debug category=spotify-runtime boundary=", boundary,
            " code=", code,
            " operation=", operation.ToString(CultureInfo.InvariantCulture),
            " generation=", generation.ToString(CultureInfo.InvariantCulture),
            " elapsed-ms=", elapsedMilliseconds.ToString(CultureInfo.InvariantCulture));
        return true;
    }

    private static bool IsToken(string value) =>
        value.Length is > 0 and <= 64 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_');

    private sealed class NullSpotifyRuntimeDiagnostics : ISpotifyRuntimeDiagnostics
    {
        public void Record(
            string boundary,
            string code,
            long operation = 0,
            long generation = 0,
            long elapsedMilliseconds = 0)
        {
        }
    }
}
