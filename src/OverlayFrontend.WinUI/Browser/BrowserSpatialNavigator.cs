using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace WidgetRail.OverlayFrontend.WinUI.Browser;

internal sealed record SpatialMoveResult(double X, double Y, int Candidates, int Frames, int Sessions, bool Scrolled, bool Truncated, double Milliseconds);

/// <summary>One bounded navigation transaction over this WebView's owned DOM/frame tree.</summary>
internal sealed class BrowserSpatialNavigator(CoreWebView2 core, Func<bool> current, CancellationToken cancellationToken) : IAsyncDisposable
{
    private sealed record Frame(int Parent, int ParentIndex, string? Session, int Context, string Object,
        double X, double Y, double Sx, double Sy, SpatialRect Clip, int Depth);
    private readonly List<Frame> frames = [];
    private readonly List<SpatialCandidate> candidates = [];
    private readonly string objectGroup = "WidgetRail.Spatial." + Guid.NewGuid().ToString("N");
    private readonly HashSet<string> groupSessions = [];
    private readonly List<string> sessions = [];
    private readonly Dictionary<string, string> attachedFrames = [];
    private readonly long started = Stopwatch.GetTimestamp();
    private SpatialRect viewport;
    private bool truncated;
    private void DemandCurrent()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!current() || Stopwatch.GetElapsedTime(started).TotalMilliseconds > 1500) throw new OperationCanceledException();
    }

    internal async Task<SpatialMoveResult?> MoveAsync(SpatialDirection direction, double x, double y)
    {
        DemandCurrent();
        using var tree = JsonDocument.Parse(await Call(null, "Page.getFrameTree", "{}"));
        var rootId = tree.RootElement.GetProperty("frameTree").GetProperty("frame").GetProperty("id").GetString()!;
        var context = await World(null, rootId);
        using var metrics = JsonDocument.Parse(await Call(null, "Page.getLayoutMetrics", "{}"));
        var visual = metrics.RootElement.GetProperty("cssVisualViewport");
        var point = new SpatialPoint(visual.GetProperty("offsetX").GetDouble() + x * visual.GetProperty("clientWidth").GetDouble(),
            visual.GetProperty("offsetY").GetDouble() + y * visual.GetProperty("clientHeight").GetDouble());
        await Scan(null, context, -1, -1, 0, 0, 1, 1, null, 0, point);
        var origin = await Origin(point);
        var delta = direction switch { SpatialDirection.Left => new BrowserHitPoint(-1, 0), SpatialDirection.Right => new(1, 0), SpatialDirection.Up => new(0, -1), _ => new(0, 1) };
        var deltaJson = JsonSerializer.Serialize(delta, BrowserJsonContext.Default.BrowserHitPoint);
        SpatialPoint? destination = null;
        var preferScroll = false;
        // A channel/menu list should scroll before jumping out to an unrelated
        // nearby pane. At its edge, ordinary geometric navigation can leave it.
        foreach (var frame in frames.AsEnumerable().Reverse())
        {
            if (!frame.Clip.Contains(point) || !await AncestorsCurrent(frame, point)) continue;
            var scope = await Function(frame, BrowserSpatialScript.ScrollScope, deltaJson);
            if (scope.ValueKind != JsonValueKind.Object) continue;
            var local = Rect(scope.GetProperty("rect"));
            var bounds = new SpatialRect(frame.X + local.X * frame.Sx, frame.Y + local.Y * frame.Sy, local.Width * frame.Sx, local.Height * frame.Sy);
            destination = await ResolveBest(candidates.Where(item => bounds.Contains(item.Point)).ToArray(), origin, direction);
            preferScroll = scope.GetProperty("canScroll").GetBoolean();
            break;
        }
        if (destination is null && !preferScroll) destination = await ResolveBest(candidates, origin, direction);
        var scrolled = false;
        if (destination is null)
        {
            // Try the innermost visible frame under the pointer first, then its
            // ancestors. Scrolling never selects a different browser/page.
            for (var i = frames.Count - 1; i >= 0; --i)
            {
                var frame = frames[i];
                if (!frame.Clip.Contains(point) || !await AncestorsCurrent(frame, point)) continue;
                var value = await Function(frame, BrowserSpatialScript.Scroll, deltaJson);
                if (!value.TryGetProperty("moved", out var moved) || !moved.GetBoolean()) continue;
                point = Global(frame, new(value.GetProperty("x").GetDouble(), value.GetProperty("y").GetDouble()));
                scrolled = true;
                await Task.Delay(45, cancellationToken); // allow scroll events/virtualized lists to lay out
                DemandCurrent();
                // Snapshot references remain bounded until disposal. Rebuild the
                // geometry after scrolling, never reuse pre-scroll hit points.
                frames.Clear(); candidates.Clear();
                await Scan(null, context, -1, -1, 0, 0, 1, 1, null, 0, point);
                origin = await Origin(point);
                destination = await ResolveBest(candidates, origin, direction);
                break;
            }
        }
        if (destination is not { } at) return null;
        DemandCurrent();
        return new((at.X - viewport.X) / viewport.Width, (at.Y - viewport.Y) / viewport.Height,
            candidates.Count, frames.Count, sessions.Count, scrolled, truncated, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    private async Task<SpatialPoint?> ResolveBest(IReadOnlyList<SpatialCandidate> choices, SpatialRect origin, SpatialDirection direction)
    {
        var remaining = choices.ToList();
        // Removed controls or parent-frame overlays must not trap every repeat
        // on the same stale candidate. Try another bounded set of alternatives.
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var selected = BrowserSpatialGeometry.Choose(remaining, origin, direction);
            if (selected is null) break;
            remaining.Remove(selected);
            var target = frames[selected.Frame];
            var resolved = await Function(target, BrowserSpatialScript.Resolve, selected.Index.ToString(CultureInfo.InvariantCulture));
            if (resolved.ValueKind != JsonValueKind.Object) continue;
            var destination = Global(target, new(resolved.GetProperty("x").GetDouble(), resolved.GetProperty("y").GetDouble()));
            if (viewport.Contains(destination) && target.Clip.Contains(destination) && await AncestorsCurrent(target, destination)) return destination;
        }
        return null;
    }

    private async Task<SpatialRect> Origin(SpatialPoint point)
    {
        foreach (var source in candidates.Where(candidate => candidate.Source).Reverse())
            if (await AncestorsCurrent(frames[source.Frame], point)) return source.Rect;
        return new(point.X, point.Y, .01, .01);
    }

    private async Task<bool> AncestorsCurrent(Frame frame, SpatialPoint point)
    {
        for (var node = frame; node.Parent >= 0; node = frames[node.Parent])
        {
            var parent = frames[node.Parent];
            var local = Local(parent, point);
            var args = JsonSerializer.Serialize(new BrowserSpatialFramePoint(node.ParentIndex, local.X, local.Y), BrowserJsonContext.Default.BrowserSpatialFramePoint);
            var valid = await Function(parent, BrowserSpatialScript.ValidateFrame, args);
            if (valid.ValueKind != JsonValueKind.True) return false;
        }
        return true;
    }

    private async Task Scan(string? session, int context, int parent, int parentIndex, double x, double y,
        double sx, double sy, SpatialRect? clipping, int depth, SpatialPoint origin)
    {
        DemandCurrent();
        if (frames.Count >= 16 || depth >= 8 || candidates.Count >= 768) { truncated = true; return; }
        var point = new BrowserHitPoint((origin.X - x) / sx, (origin.Y - y) / sy);
        var expression = BrowserSpatialScript.Scan + "(" + JsonSerializer.Serialize(point, BrowserJsonContext.Default.BrowserHitPoint) + ")";
        using var remote = JsonDocument.Parse(await Call(session, "Runtime.evaluate",
            JsonSerializer.Serialize(new BrowserEvaluation(expression, context, objectGroup), BrowserJsonContext.Default.BrowserEvaluation)));
        var objectId = remote.RootElement.GetProperty("result").GetProperty("objectId").GetString()!;
        var frame = new Frame(parent, parentIndex, session, context, objectId, x, y, sx, sy, clipping ?? default, depth);
        var description = await Function(frame, BrowserSpatialScript.Describe);
        if (parent < 0) viewport = Rect(description.GetProperty("view"));
        frame = frame with { Clip = clipping?.Intersect(viewport) ?? viewport };
        var index = frames.Count; frames.Add(frame);
        truncated |= description.GetProperty("truncated").GetBoolean();
        foreach (var element in description.GetProperty("items").EnumerateArray())
        {
            if (candidates.Count >= 768) { truncated = true; break; }
            var rect = Rect(element.GetProperty("rect"));
            var global = new SpatialRect(x + rect.X * sx, y + rect.Y * sy, rect.Width * sx, rect.Height * sy).Intersect(frame.Clip);
            var at = element.GetProperty("point");
            var hit = Global(frame, new(at.GetProperty("x").GetDouble(), at.GetProperty("y").GetDouble()));
            if (global.Valid && frame.Clip.Contains(hit)) candidates.Add(new(index, element.GetProperty("index").GetInt32(), global, hit, element.GetProperty("source").GetBoolean()));
        }
        foreach (var child in description.GetProperty("frames").EnumerateArray())
        {
            DemandCurrent();
            if (frames.Count >= 16 || depth >= 7) { truncated = true; break; }
            try
            {
                var childIndex = child.GetProperty("index").GetInt32();
                using var reference = JsonDocument.Parse(await Call(session, "Runtime.callFunctionOn",
                    JsonSerializer.Serialize(new BrowserFieldCall(objectId, BrowserSpatialScript.FrameElement,
                        [new(childIndex.ToString(CultureInfo.InvariantCulture))], false, objectGroup), BrowserJsonContext.Default.BrowserFieldCall)));
                var frameObject = reference.RootElement.GetProperty("result").GetProperty("objectId").GetString()!;
                using var node = JsonDocument.Parse(await Call(session, "DOM.describeNode", JsonSerializer.Serialize(new BrowserObject(frameObject), BrowserJsonContext.Default.BrowserObject)));
                var frameId = node.RootElement.GetProperty("node").GetProperty("frameId").GetString()!;
                var childSession = session; int childContext;
                try { childContext = await World(session, frameId); }
                catch (Exception error) when (error is not OperationCanceledException && current())
                {
                    // Attach only to the frame proven to belong to this DOM node.
                    childSession = await AttachFrame(frameId);
                    childContext = await World(childSession, frameId);
                }
                var clip = Rect(child.GetProperty("clip"));
                var childClip = new SpatialRect(x + clip.X * sx, y + clip.Y * sy, clip.Width * sx, clip.Height * sy).Intersect(frame.Clip);
                await Scan(childSession, childContext, index, childIndex, x + child.GetProperty("x").GetDouble() * sx,
                    y + child.GetProperty("y").GetDouble() * sy, sx * child.GetProperty("sx").GetDouble(), sy * child.GetProperty("sy").GetDouble(), childClip, depth + 1, origin);
            }
            catch (Exception error) when (error is not OperationCanceledException and not OutOfMemoryException && current())
            { /* Removed/inaccessible frames retain free-pointer behavior. */ }
        }
    }
    private async Task<int> World(string? session, string frameId)
    {
        using var world = JsonDocument.Parse(await Call(session, "Page.createIsolatedWorld",
            JsonSerializer.Serialize(new BrowserIsolatedWorld(frameId, "WidgetRail.SpatialNavigation"), BrowserJsonContext.Default.BrowserIsolatedWorld)));
        return world.RootElement.GetProperty("executionContextId").GetInt32();
    }
    private async Task<JsonElement> Function(Frame frame, string script, string? argument = null)
    {
        using var result = JsonDocument.Parse(await Call(frame.Session, "Runtime.callFunctionOn", JsonSerializer.Serialize(
            new BrowserFieldCall(frame.Object, script, argument is null ? null : [new(argument)], objectGroup: objectGroup), BrowserJsonContext.Default.BrowserFieldCall)));
        return result.RootElement.GetProperty("result").TryGetProperty("value", out var value) ? value.Clone() : default;
    }
    private async Task<string> Call(string? session, string method, string parameters)
    {
        DemandCurrent();
        groupSessions.Add(session ?? "");
        var result = await core.CallDevToolsProtocolMethodForSessionAsync(session ?? "", method, parameters).AsTask().WaitAsync(TimeSpan.FromMilliseconds(750), cancellationToken);
        DemandCurrent(); return result;
    }
    private async Task<string> AttachFrame(string frameId)
    {
        DemandCurrent();
        if (attachedFrames.TryGetValue(frameId, out var existing)) return existing;
        var pending = core.CallDevToolsProtocolMethodAsync("Target.attachToTarget", JsonSerializer.Serialize(new BrowserAttachTarget(frameId), BrowserJsonContext.Default.BrowserAttachTarget)).AsTask();
        string response;
        try { response = await pending.WaitAsync(TimeSpan.FromMilliseconds(750), cancellationToken); }
        catch { _ = RetireLateAttachment(pending); throw; }
        using var attached = JsonDocument.Parse(response);
        var session = attached.RootElement.GetProperty("sessionId").GetString()!;
        sessions.Add(session);
        attachedFrames.Add(frameId, session);
        DemandCurrent();
        return session;
    }
    private async Task RetireLateAttachment(Task<string> pending)
    {
        try
        {
            using var attached = JsonDocument.Parse(await pending);
            var session = attached.RootElement.GetProperty("sessionId").GetString()!;
            await core.CallDevToolsProtocolMethodAsync("Target.detachFromTarget", JsonSerializer.Serialize(new BrowserDetachTarget(session), BrowserJsonContext.Default.BrowserDetachTarget)).AsTask().WaitAsync(TimeSpan.FromMilliseconds(250));
        }
        catch (Exception error) when (error is not OutOfMemoryException) { }
    }
    private static SpatialRect Rect(JsonElement value) => new(value.GetProperty("x").GetDouble(), value.GetProperty("y").GetDouble(), value.GetProperty("w").GetDouble(), value.GetProperty("h").GetDouble());
    private static SpatialPoint Global(Frame frame, SpatialPoint local) => new(frame.X + local.X * frame.Sx, frame.Y + local.Y * frame.Sy);
    private static SpatialPoint Local(Frame frame, SpatialPoint global) => new((global.X - frame.X) / frame.Sx, (global.Y - frame.Y) / frame.Sy);
    public async ValueTask DisposeAsync()
    {
        // Object groups also retire responses that arrived after cancellation,
        // before their individual object IDs could be observed by this caller.
        await Task.WhenAll(groupSessions.Select(async session =>
        {
            try { await core.CallDevToolsProtocolMethodForSessionAsync(session, "Runtime.releaseObjectGroup", JsonSerializer.Serialize(new BrowserObjectGroup(objectGroup), BrowserJsonContext.Default.BrowserObjectGroup)).AsTask().WaitAsync(TimeSpan.FromMilliseconds(250)); }
            catch (Exception error) when (error is not OutOfMemoryException) { }
        }));
        await Task.WhenAll(sessions.Select(async session =>
        {
            try { await core.CallDevToolsProtocolMethodAsync("Target.detachFromTarget", JsonSerializer.Serialize(new BrowserDetachTarget(session), BrowserJsonContext.Default.BrowserDetachTarget)).AsTask().WaitAsync(TimeSpan.FromMilliseconds(250)); }
            catch (Exception error) when (error is not OutOfMemoryException) { }
        }));
    }
}
