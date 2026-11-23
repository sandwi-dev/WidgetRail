using System.Runtime.ExceptionServices;
using System.Text.Json;

// Bounded diagnostics for the account-free fixture only. Observe the original
// exception before the widget deliberately maps it to a safe UI error message.
internal sealed class SyntheticCursorDiagnostics : IDisposable
{
    private readonly string path;
    private readonly object gate = new();
    private int count;
    [ThreadStatic] private static bool writing;

    private SyntheticCursorDiagnostics(string path)
    {
        this.path = path;
        AppDomain.CurrentDomain.FirstChanceException += Observe;
    }

    internal static SyntheticCursorDiagnostics Open()
    {
        var audit = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixture-messages.enabled")).Trim();
        if (!Path.IsPathFullyQualified(audit) || Path.GetFileName(audit) != "messages-provider.json")
            throw new InvalidDataException("Synthetic cursor diagnostics require the isolated message audit.");
        return new(Path.Combine(Path.GetDirectoryName(audit)!, "cursor-exceptions.jsonl"));
    }

    private void Observe(object? sender, FirstChanceExceptionEventArgs args)
    {
        if (writing || Volatile.Read(ref count) >= 32) return;
        var stack = args.Exception.StackTrace ?? "";
        if (!stack.Contains("WidgetCursorResource", StringComparison.Ordinal) &&
            !stack.Contains("LoadMessageHistoryAsync", StringComparison.Ordinal)) return;
        lock (gate)
        {
            if (count >= 32) return;
            writing = true;
            try
            {
                File.AppendAllText(path, JsonSerializer.Serialize(new { utc = DateTime.UtcNow,
                    type = args.Exception.GetType().FullName, args.Exception.Message, stack }) + Environment.NewLine);
                count++;
            }
            finally { writing = false; }
        }
    }

    public void Dispose() => AppDomain.CurrentDomain.FirstChanceException -= Observe;
}
