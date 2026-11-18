using WidgetRail.WidgetProtocol;

namespace WidgetRail.PlatformBroker;

// Trusted host/bridge contracts. These are never exposed to widget capability clients.
public sealed record HostCaptureImageEdit(int QuarterTurns, int X, int Y, int Width, int Height);
public sealed record HostCaptureVideoEdit(long StartTicks, long EndTicks);
public sealed record HostCaptureEditRecipe(HostCaptureImageEdit? Image = null, HostCaptureVideoEdit? Video = null);
public sealed record HostCaptureEditRequest(string Id, BrokerWidgetIdentity Widget, CaptureAttachment Media,
    string PreviewUri, string ResultId);
public enum HostCaptureEditStage { Editing, Saving, Saved, Cancelled, Failed }
public sealed record HostCaptureEditStatus(bool Current, HostCaptureEditStage Stage, double? Progress = null,
    CaptureLibraryItem? Item = null, string? ErrorCode = null);

internal sealed record CaptureEditPreview(string Uri, IDisposable? Lifetime = null);

/// <summary>One host editor transaction. Capability admission owns its lifetime; the host alone supplies a recipe.</summary>
internal static class CaptureLibraryEditRequests
{
    private static readonly object gate = new();
    private static Job? active;
    private static readonly Dictionary<string, (DateTimeOffset Expires, HostCaptureEditStatus Status)> terminal = [];
    private static CaptureEditCodec? codec;
    private static Func<CaptureEditInput, CancellationToken, Task>? validate;
    private static Func<CaptureEditInput, CancellationToken, Task<CaptureEditPreview>>? preview;

    internal static void Configure(CaptureEditCodec encoder,
        Func<CaptureEditInput, CancellationToken, Task> validateSource,
        Func<CaptureEditInput, CancellationToken, Task<CaptureEditPreview>> createPreview)
    { lock (gate) { codec = encoder; validate = validateSource; preview = createPreview; } }

    internal static HostCaptureEditRequest? Pending { get { lock (gate) return active is { Saving: false, CancelRequested: false } job ? job.Request : null; } }
    internal static HostCaptureEditRequest? ActiveRequest(string id) { lock (gate) return active?.Id == id ? active.Request : null; }
    internal static string? SourcePermission(string id) { lock (gate) return active?.Id == id ? active.SourcePermission : null; }

    internal static async Task<CaptureLibraryEditResult> RunAsync(BrokerWidgetIdentity actor, CaptureLibraryEditRequest request,
        CaptureLibraryStore library, WindowCaptureRegistry registry, object owner, CancellationToken token)
    {
        if (!request.IsWellFormed()) throw new BrokerException("invalid_payload", "Choose one valid editor source.");
        Job job;
        lock (gate)
        {
            if (codec is null || validate is null || preview is null) throw new BrokerException("capture_library_edit_unavailable", "The host editor is unavailable.");
            if (active is not null) throw new BrokerException("capture_library_edit_busy", "Another capture is being edited.");
            active = job = new(request.Saved is null ? PlatformCapabilities.WindowCaptureV1 : PlatformCapabilities.CaptureLibraryReadV1,
                codec, validate, preview);
        }
        using var cancellation = token.Register(() => Cancel(job.Id));
        using var timeout = new Timer(_ => Cancel(job.Id), null, TimeSpan.FromMinutes(10), Timeout.InfiniteTimeSpan);
        job.Deadline = timeout;
        try
        {
            bool Authorized() => !job.Stop.IsCancellationRequested && !token.IsCancellationRequested;
            if (request.Saved is { } saved)
                job.Session = await library.OpenEditSessionAsync(saved, actor, Authorized, job.Stop.Token).ConfigureAwait(false);
            else
            {
                using var temporary = registry.AcquireEditLease(owner, request.Attachment!);
                job.Session = await library.OpenEditSessionAsync(temporary, actor, Authorized, job.Stop.Token).ConfigureAwait(false);
            }
            using (var input = job.Session.AcquireInput())
            {
                await job.Validate(input.Input, job.Stop.Token).ConfigureAwait(false);
                job.Preview = await job.PreviewFactory(input.Input, job.Stop.Token).ConfigureAwait(false);
            }
            job.Stop.Token.ThrowIfCancellationRequested();
            lock (gate)
            {
                job.Ready = true;
                if (job.CancelRequested || job.Stop.IsCancellationRequested) Finish(job, new(false, HostCaptureEditStage.Cancelled));
                else job.Request = new(job.Id, actor, job.Session.Media, job.Preview.Uri, job.Session.ResultId);
            }
            var result = await job.Done.Task.ConfigureAwait(false);
            if (result.Outcome != CaptureLibraryEditOutcome.Saved) token.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException)
        {
            Finish(job, new(false, HostCaptureEditStage.Cancelled));
            token.ThrowIfCancellationRequested();
            return new(CaptureLibraryEditOutcome.Cancelled);
        }
        catch (Exception error)
        {
            Finish(job, new(false, HostCaptureEditStage.Failed, ErrorCode: error is BrokerException broker ? broker.Code : "capture_library_edit_failed"));
            _ = job.Done.Task.Exception; // Observe preparation failures which occurred before awaiting Done.
            throw;
        }
        finally
        {
            // Completion has reconciled any in-flight commit before releasing its exact source.
            job.Preview?.Lifetime?.Dispose(); job.Session?.Dispose(); job.Stop.Dispose();
        }
    }

    internal static HostCaptureEditStatus Status(string id)
    {
        Job? job;
        lock (gate)
        {
            Sweep(); job = active?.Id == id ? active : null;
            if (job is null) return terminal.TryGetValue(id, out var saved) ? saved.Status : new(false, HostCaptureEditStage.Failed, ErrorCode: "capture_library_edit_retired");
        }
        var current = job.Ready && !job.CancelRequested && job.Session?.IsCurrent == true;
        lock (gate)
        {
            if (active != job) return terminal.TryGetValue(id, out var saved) ? saved.Status : new(false, HostCaptureEditStage.Failed, ErrorCode: "capture_library_edit_retired");
            return new(current, job.Saving ? HostCaptureEditStage.Saving : HostCaptureEditStage.Editing, job.Progress);
        }
    }

    internal static bool Complete(string id, HostCaptureEditRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        Job? job;
        lock (gate) job = active?.Id == id ? active : null;
        if (job is not { Ready: true } || job.CancelRequested || job.Session?.IsCurrent != true) return false;
        var mapped = new CaptureEditRecipe(recipe.Image is { } image ? new(image.QuarterTurns, image.X, image.Y, image.Width, image.Height) : null,
            recipe.Video is { } video ? new(video.StartTicks, video.EndTicks) : null);
        if (!mapped.IsValid(job.Session.Media)) return false;
        lock (gate)
        {
            if (active != job || job.CancelRequested) return false;
            if (job.Saving) return job.Recipe == mapped;
            job.Recipe = mapped; job.Saving = true;
            job.Deadline?.Change(TimeSpan.FromMinutes(10), Timeout.InfiniteTimeSpan);
        }
        _ = SaveAsync(job);
        return true;
    }

    internal static HostCaptureEditStatus Cancel(string id)
    {
        Job? job;
        lock (gate)
        {
            job = active?.Id == id ? active : null;
            if (job is not null) job.CancelRequested = true;
        }
        if (job is not null)
        {
            // Cancellation callbacks can release source/store locks; never run them under gate.
            try { job.Stop.Cancel(); } catch (ObjectDisposedException) { }
            lock (gate)
                if (active == job && job.Ready && !job.Saving) Finish(job, new(false, HostCaptureEditStage.Cancelled));
        }
        return Status(id);
    }

    private static async Task SaveAsync(Job job)
    {
        await Task.Yield();
        try
        {
            var item = await job.Session!.SaveAsync(job.Recipe!, job.Codec, new EditProgress(value =>
                { lock (gate) if (active == job && double.IsFinite(value)) job.Progress = Math.Clamp(value, 0, 1); }), job.Stop.Token).ConfigureAwait(false);
            Finish(job, new(false, HostCaptureEditStage.Saved, 1, item));
        }
        catch (OperationCanceledException) { Finish(job, new(false, HostCaptureEditStage.Cancelled)); }
        catch (Exception error)
        { Finish(job, job.CancelRequested ? new(false, HostCaptureEditStage.Cancelled) : new(false, HostCaptureEditStage.Failed, ErrorCode: error is BrokerException broker ? broker.Code : "capture_library_edit_failed")); }
    }

    private static void Finish(Job job, HostCaptureEditStatus status)
    {
        lock (gate)
        {
            if (active != job) return;
            active = null; Sweep();
            if (terminal.Count >= 8) terminal.Remove(terminal.MinBy(pair => pair.Value.Expires).Key);
            terminal[job.Id] = (DateTimeOffset.UtcNow.AddMinutes(10), status);
            if (status.Stage == HostCaptureEditStage.Failed)
                job.Done.TrySetException(new BrokerException(status.ErrorCode!, "The capture could not be edited."));
            else job.Done.TrySetResult(new(status.Stage == HostCaptureEditStage.Saved ? CaptureLibraryEditOutcome.Saved : CaptureLibraryEditOutcome.Cancelled, status.Item));
        }
    }
    private static void Sweep()
    { foreach (var id in terminal.Where(pair => pair.Value.Expires <= DateTimeOffset.UtcNow).Select(pair => pair.Key).ToArray()) terminal.Remove(id); }

    private sealed class EditProgress(Action<double> report) : IProgress<double>
    { public void Report(double value) => report(value); }
    private sealed class Job(string sourcePermission, CaptureEditCodec encoder,
        Func<CaptureEditInput, CancellationToken, Task> validator,
        Func<CaptureEditInput, CancellationToken, Task<CaptureEditPreview>> previewFactory)
    {
        internal string Id { get; } = Guid.NewGuid().ToString("N");
        internal string SourcePermission { get; } = sourcePermission;
        internal CaptureEditCodec Codec { get; } = encoder;
        internal Func<CaptureEditInput, CancellationToken, Task> Validate { get; } = validator;
        internal Func<CaptureEditInput, CancellationToken, Task<CaptureEditPreview>> PreviewFactory { get; } = previewFactory;
        internal CancellationTokenSource Stop { get; } = new();
        internal TaskCompletionSource<CaptureLibraryEditResult> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal HostCaptureEditRequest? Request;
        internal CaptureLibraryEditSession? Session;
        internal CaptureEditPreview? Preview;
        internal CaptureEditRecipe? Recipe;
        internal bool Ready, Saving;
        internal volatile bool CancelRequested;
        internal Timer? Deadline;
        internal double? Progress;
    }
}
