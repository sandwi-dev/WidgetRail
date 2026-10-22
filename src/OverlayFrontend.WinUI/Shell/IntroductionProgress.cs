using System.Text.Json;
using System.Text.Json.Serialization;

namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal sealed record IntroductionProgress(int Version, string Step, bool Paused = false)
{
    internal static readonly string[] Steps = ["welcome", "guide", "commands", "utilities", "catalogue", "appearance", "appearance-details", "complete"];
    internal static IntroductionProgress New => new(1, "welcome");
    internal static IntroductionProgress SafeFallback => New with { Paused = true };
    internal bool Valid => Version == 1 && Step is not null && Steps.Contains(Step, StringComparer.Ordinal);
    internal bool Finished => Step == "complete";
    internal IntroductionProgress Next() => this with { Step = Steps[Math.Min(Array.IndexOf(Steps, Step) + 1, Steps.Length - 1)] };
}

/// <summary>Optional, bounded per-profile progress. Failure never prevents opening the overlay.</summary>
internal sealed class IntroductionProgressStore(string settingsRoot)
{
    internal string Path { get; } = System.IO.Path.Combine(settingsRoot, "introduction-progress.json");
    internal const int MaximumBytes = 8192;
    private readonly SemaphoreSlim writes = new(1, 1);
    private long revision;

    internal async Task<IntroductionProgress> LoadAsync(CancellationToken token = default)
    {
        try
        {
            await using var stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > MaximumBytes) return IntroductionProgress.SafeFallback;
            var bytes = new byte[MaximumBytes + 1];
            var count = 0;
            while (count < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(count), token);
                if (read == 0) break;
                count += read;
            }
            if (count > MaximumBytes) return IntroductionProgress.SafeFallback;
            var value = JsonSerializer.Deserialize(bytes.AsSpan(0, count), IntroductionJsonContext.Default.IntroductionProgress);
            return value is { Valid: true } ? value : IntroductionProgress.SafeFallback;
        }
        catch (FileNotFoundException) { return IntroductionProgress.New; }
        catch (DirectoryNotFoundException) { return IntroductionProgress.New; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        { return IntroductionProgress.SafeFallback; }
    }

    internal async Task<bool> SaveAsync(IntroductionProgress value)
    {
        if (!value.Valid) return false;
        var requested = Interlocked.Increment(ref revision);
        await writes.WaitAsync();
        var temporary = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (requested != Interlocked.Read(ref revision)) return true;
            Directory.CreateDirectory(settingsRoot);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, IntroductionJsonContext.Default.IntroductionProgress));
            File.Move(temporary, Path, overwrite: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        { return false; }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            writes.Release();
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, MaxDepth = 8)]
[JsonSerializable(typeof(IntroductionProgress))]
internal sealed partial class IntroductionJsonContext : JsonSerializerContext;
