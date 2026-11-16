using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace WidgetRail.Playnite;

/// <summary>Bounded, server-instance-local replay protection; client cancellation is not rollback.</summary>
public sealed class CommandRegistry<T>
{
    public const int RetentionSeconds = 600;
    private readonly object gate = new object();
    private readonly Dictionary<string, Entry> commands = new Dictionary<string, Entry>(StringComparer.Ordinal);
    private int running;
    private sealed class Entry
    {
        internal readonly string Signature;
        internal readonly DateTime Expires = DateTime.UtcNow.AddSeconds(RetentionSeconds);
        internal readonly TaskCompletionSource<T> Completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Entry(string signature) { Signature = signature; }
    }
    public Task<T> Execute(string id, string signature, Func<Task<T>> action)
    {
        if (!Guid.TryParseExact(id, "D", out _) || signature.Length != 64) throw new ArgumentException("Invalid command identity.");
        Entry entry;
        lock (gate)
        {
            foreach (var expired in commands.Where(pair => pair.Value.Expires < DateTime.UtcNow && pair.Value.Completion.Task.IsCompleted).Select(pair => pair.Key).ToArray())
                commands.Remove(expired);
            if (commands.TryGetValue(id, out var previous))
            {
                if (previous.Signature != signature) throw new CommandConflictException();
                return previous.Completion.Task;
            }
            if (running >= 4 || commands.Count >= 256) throw new CommandCapacityException();
            entry = new Entry(signature); commands.Add(id, entry); running++;
        }
        _ = ExecuteCoreAsync(entry, action);
        return entry.Completion.Task;
    }
    private async Task ExecuteCoreAsync(Entry entry, Func<Task<T>> action)
    {
        try { entry.Completion.TrySetResult(await action().ConfigureAwait(false)); }
        catch (OperationCanceledException) { entry.Completion.TrySetCanceled(); }
        catch (Exception error) { entry.Completion.TrySetException(error); }
        finally { lock (gate) running--; }
    }
    public Task<T>? Find(string id)
    {
        lock (gate) return commands.TryGetValue(id, out var entry) ? entry.Completion.Task : null;
    }
}
public sealed class CommandConflictException : InvalidOperationException { }
public sealed class CommandCapacityException : InvalidOperationException { }
