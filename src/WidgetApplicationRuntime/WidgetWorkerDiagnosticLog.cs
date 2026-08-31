using System.Text.Json;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

namespace WidgetRail.WidgetRuntime;

/// <summary>
/// Worker-owned, best-effort diagnostics for failures that originate before a
/// reply can be trusted to reach the host. The host supplies one isolated path
/// per process; widget content never selects the destination or record shape.
/// </summary>
internal sealed class WidgetWorkerDiagnosticLog
{
    internal const long MaximumFileBytes = 128 * 1024;
    internal const int RetainedGenerationCount = 2;
    internal const string FileName = "worker.jsonl";

    private readonly object _gate = new();
    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private WidgetWorkerDiagnosticLog(string path) => _path = path;

    internal static WidgetWorkerDiagnosticLog? TryCreate(IReadOnlyList<string> args)
    {
        try
        {
            var matches = Enumerable.Range(0, args.Count)
                .Where(index => string.Equals(
                    args[index], "--worker-diagnostics-path", StringComparison.Ordinal))
                .ToArray();
            if (matches.Length == 0) return null;
            if (matches.Length != 1 || matches[0] + 1 >= args.Count)
                return null;
            var path = Path.GetFullPath(args[matches[0] + 1]);
            if (!Path.IsPathFullyQualified(path) ||
                !string.Equals(Path.GetFileName(path), FileName, StringComparison.Ordinal) ||
                path.Length > 4096 || !Directory.Exists(Path.GetDirectoryName(path)))
                return null;
            return new(path);
        }
        catch (Exception exception) when (exception is ArgumentException or
            IOException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    internal void RecordRequestFailure(
        long requestId,
        string requestType,
        Exception exception)
    {
        if (requestId <= 0 || !IsSafeToken(requestType, 64)) return;
        var code = exception is ProtocolValidationException
            ? WorkerErrorCodes.ProtocolValidationFailed
            : WorkerErrorCodes.RequestFailed;
        string? validationPath = null;
        string? validationCode = null;
        if (exception is ProtocolValidationException validation)
        {
            var first = validation.Errors.FirstOrDefault();
            if (first is not null &&
                IsSafeValidationPath(first.Path) &&
                IsSafeValidationCode(first.Code))
            {
                validationPath = first.Path;
                validationCode = first.Code;
            }
        }

        var record = JsonSerializer.Serialize(new WorkerFailureRecord(
            DateTimeOffset.UtcNow,
            Environment.ProcessId,
            requestId,
            requestType,
            code,
            validationPath,
            validationCode), JsonOptions);
        lock (_gate)
        {
            try
            {
                RotateIfRequired(record.Length + Environment.NewLine.Length);
                File.AppendAllText(_path, record + Environment.NewLine);
            }
            catch (Exception writeFailure) when (writeFailure is IOException or
                UnauthorizedAccessException or NotSupportedException or
                PathTooLongException)
            {
                // Diagnostics are observational and never own worker behavior.
            }
        }
    }

    private void RotateIfRequired(int incomingCharacters)
    {
        var currentLength = File.Exists(_path) ? new FileInfo(_path).Length : 0;
        if (currentLength + incomingCharacters <= MaximumFileBytes) return;
        for (var generation = RetainedGenerationCount; generation >= 1; generation--)
        {
            var destination = $"{_path}.{generation}";
            var source = generation == 1 ? _path : $"{_path}.{generation - 1}";
            if (generation == RetainedGenerationCount && File.Exists(destination))
                File.Delete(destination);
            if (File.Exists(source)) File.Move(source, destination);
        }
    }

    private static bool IsSafeToken(string value, int maximumLength) =>
        value.Length is > 0 && value.Length <= maximumLength &&
        value.All(character => char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_' or '.');

    private static bool IsSafeValidationCode(string value) =>
        value.Length is > 0 and <= 64 && value.All(character =>
            character is >= 'a' and <= 'z' || char.IsAsciiDigit(character) ||
            character == '_');

    private static bool IsSafeValidationPath(string path)
    {
        if (path.Length is < 1 or > 256 || path[0] != '$') return false;
        for (var index = 1; index < path.Length;)
        {
            if (path[index] == '.')
            {
                index++;
                var start = index;
                while (index < path.Length &&
                       (char.IsAsciiLetterOrDigit(path[index]) || path[index] == '_'))
                    index++;
                if (index == start) return false;
                continue;
            }
            if (path[index] == '[')
            {
                index++;
                var start = index;
                while (index < path.Length && char.IsAsciiDigit(path[index])) index++;
                if (index == start || index >= path.Length || path[index] != ']') return false;
                index++;
                continue;
            }
            return false;
        }
        return true;
    }

    private sealed record WorkerFailureRecord(
        DateTimeOffset Timestamp,
        int ProcessId,
        long RequestId,
        string RequestType,
        string Code,
        string? ValidationPath,
        string? ValidationCode);
}
