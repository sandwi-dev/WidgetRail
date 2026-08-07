using GameBarAlternative.WidgetSdk;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Intervals are resource bounded", IntervalsAreBounded),
    ("An already hidden widget never ticks", HiddenWidgetNeverTicks),
    ("Deactivation cancels a waiting ticker", DeactivationStopsTicker),
    ("Slow callbacks never overlap", SlowCallbacksDoNotOverlap),
    ("Callback failures remain observable", CallbackFailuresRemainObservable),
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL {test.Name}: {exception}");
        Console.Error.WriteLine(failures[^1]);
    }
}

Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed.");
return failures.Count == 0 ? 0 : 1;

static async Task IntervalsAreBounded()
{
    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => WidgetTicker.RunWhileActiveAsync(
        WidgetTicker.MinimumInterval - TimeSpan.FromMilliseconds(1),
        _ => ValueTask.CompletedTask, CancellationToken.None));
    await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => WidgetTicker.RunWhileActiveAsync(
        WidgetTicker.MaximumInterval + TimeSpan.FromMilliseconds(1),
        _ => ValueTask.CompletedTask, CancellationToken.None));
}

static async Task DeactivationStopsTicker()
{
    using var lifetime = new CancellationTokenSource();
    var ticks = 0;
    var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var loop = WidgetTicker.RunWhileActiveAsync(
        WidgetTicker.MinimumInterval,
        _ =>
        {
            if (Interlocked.Increment(ref ticks) == 1) first.TrySetResult();
            return ValueTask.CompletedTask;
        },
        lifetime.Token);

    await first.Task.WaitAsync(TimeSpan.FromSeconds(2));
    lifetime.Cancel();
    await loop.WaitAsync(TimeSpan.FromSeconds(1));
    var stoppedAt = Volatile.Read(ref ticks);
    await Task.Delay(WidgetTicker.MinimumInterval + TimeSpan.FromMilliseconds(100));
    Assert.Equal(stoppedAt, Volatile.Read(ref ticks));
}

static async Task HiddenWidgetNeverTicks()
{
    using var hiddenLifetime = new CancellationTokenSource();
    hiddenLifetime.Cancel();
    var ticks = 0;
    await WidgetTicker.RunWhileActiveAsync(
        WidgetTicker.MinimumInterval,
        _ =>
        {
            Interlocked.Increment(ref ticks);
            return ValueTask.CompletedTask;
        },
        hiddenLifetime.Token,
        tickImmediately: true);
    Assert.Equal(0, ticks);
}

static async Task SlowCallbacksDoNotOverlap()
{
    using var lifetime = new CancellationTokenSource();
    var concurrent = 0;
    var maximumConcurrent = 0;
    var ticks = 0;
    var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var loop = WidgetTicker.RunWhileActiveAsync(
        WidgetTicker.MinimumInterval,
        async token =>
        {
            var now = Interlocked.Increment(ref concurrent);
            InterlockedExtensions.Max(ref maximumConcurrent, now);
            if (Interlocked.Increment(ref ticks) == 2) second.TrySetResult();
            try { await Task.Delay(WidgetTicker.MinimumInterval + TimeSpan.FromMilliseconds(80), token); }
            finally { Interlocked.Decrement(ref concurrent); }
        },
        lifetime.Token,
        tickImmediately: true);

    await second.Task.WaitAsync(TimeSpan.FromSeconds(3));
    lifetime.Cancel();
    await loop.WaitAsync(TimeSpan.FromSeconds(1));
    Assert.Equal(1, maximumConcurrent);
}

static async Task CallbackFailuresRemainObservable()
{
    var expected = new InvalidOperationException("metric failed");
    var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        WidgetTicker.RunWhileActiveAsync(
            WidgetTicker.MinimumInterval,
            _ => ValueTask.FromException(expected),
            CancellationToken.None,
            tickImmediately: true));
    Assert.True(ReferenceEquals(expected, failure), "The original callback failure should be preserved.");
}

file static class InterlockedExtensions
{
    public static void Max(ref int location, int candidate)
    {
        var current = Volatile.Read(ref location);
        while (candidate > current)
        {
            var observed = Interlocked.CompareExchange(ref location, candidate, current);
            if (observed == current) return;
            current = observed;
        }
    }
}

file static class Assert
{
    public static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    public static async Task<T> ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
