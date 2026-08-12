using System.Collections.Concurrent;
using GameBarAlternative.WidgetProtocol;

namespace GameBarAlternative.WidgetSdk;

/// <summary>A deterministic typed fake-operation barrier for semantic scenarios.</summary>
public sealed class WidgetScenarioOperationBarrier<TRequest, TResponse>
{
    private const int MaximumPending = 8;
    private readonly ConcurrentQueue<WidgetScenarioOperationInvocation<TRequest, TResponse>>
        _pending = new();
    private readonly SemaphoreSlim _available = new(0, MaximumPending);
    private int _active;

    public int ActiveInvocations => Volatile.Read(ref _active);

    public async ValueTask<TResponse> InvokeAsync(
        TRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Interlocked.Increment(ref _active) > MaximumPending)
        {
            Interlocked.Decrement(ref _active);
            throw new WidgetCapabilityException(
                "test_barrier_capacity", "The scenario operation barrier is full.");
        }
        var invocation = new WidgetScenarioOperationInvocation<TRequest, TResponse>(
            request, cancellationToken);
        _pending.Enqueue(invocation);
        _available.Release();
        try
        {
            return await invocation.Response.ConfigureAwait(false);
        }
        finally
        {
            invocation.MarkHandlerCompleted();
            Interlocked.Decrement(ref _active);
        }
    }

    public async ValueTask<WidgetScenarioOperationInvocation<TRequest, TResponse>>
        WaitForInvocationAsync(CancellationToken cancellationToken = default)
    {
        await _available.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (!_pending.TryDequeue(out var invocation))
            throw new InvalidOperationException("The scenario operation barrier lost an invocation.");
        return invocation;
    }
}

/// <summary>One exact admitted typed fake operation controlled by a scenario.</summary>
public sealed class WidgetScenarioOperationInvocation<TRequest, TResponse>
{
    private readonly TaskCompletionSource<TResponse> _response = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _canceled = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _handlerCompleted = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenRegistration _registration;
    private int _resolved;

    internal WidgetScenarioOperationInvocation(
        TRequest request,
        CancellationToken cancellationToken)
    {
        Request = request;
        _registration = cancellationToken.Register(() => _canceled.TrySetResult());
        if (cancellationToken.IsCancellationRequested) _canceled.TrySetResult();
    }

    public TRequest Request { get; }
    public Task CancellationObserved => _canceled.Task;
    public Task HandlerCompleted => _handlerCompleted.Task;
    internal Task<TResponse> Response => _response.Task;

    public void Complete(TResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (Interlocked.Exchange(ref _resolved, 1) != 0)
            throw new InvalidOperationException("The scenario invocation was already resolved.");
        _response.TrySetResult(response);
    }

    public void Fail(WidgetCapabilityException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (Interlocked.Exchange(ref _resolved, 1) != 0)
            throw new InvalidOperationException("The scenario invocation was already resolved.");
        _response.TrySetException(exception);
    }

    internal void MarkHandlerCompleted()
    {
        _registration.Dispose();
        _handlerCompleted.TrySetResult();
    }
}

/// <summary>
/// Optional high-level semantic scenario runner. It sequences named lifecycle,
/// action, barrier, and assertion steps over the real widget test host.
/// </summary>
public sealed class WidgetScenario
{
    private const int MaximumSteps = 64;
    private readonly WidgetScenarioDefinition _definition;
    private readonly List<Step> _steps = [];
    private long _sequence;

    public WidgetScenario(string caseId, WidgetScenarioDefinition definition)
    {
        CaseId = ValidateId(caseId, nameof(caseId));
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public string CaseId { get; }

    public WidgetScenario Activate(string stepId = "activate") => Add(
        stepId,
        async cancellationToken =>
        {
            await WidgetTestHost.SetLifecycleStateAsync(
                _definition.Widget, WidgetLifecycleState.Interactive, cancellationToken);
        });

    public WidgetScenario Deactivate(string stepId = "deactivate") => Add(
        stepId,
        async cancellationToken =>
        {
            await WidgetTestHost.SetLifecycleStateAsync(
                _definition.Widget, WidgetLifecycleState.Background, cancellationToken);
        });

    public WidgetScenario Action(
        string stepId,
        WidgetActionEvent action) => Add(
        stepId,
        cancellationToken => _definition.Widget.IsActive
            ? _definition.Widget.OnActionAsync(action, cancellationToken)
            : ValueTask.CompletedTask);

    public WidgetScenario Wait(
        string stepId,
        Func<CancellationToken, ValueTask> barrier)
    {
        ArgumentNullException.ThrowIfNull(barrier);
        return Add(stepId, barrier);
    }

    public WidgetScenario ExpectText(string stepId, string nodeId, string expected) =>
        ExpectNode(stepId, nodeId, node =>
            string.Equals(node.Text, expected, StringComparison.Ordinal)
                ? null
                : $"Expected text '{expected}', received '{node.Text}'.");

    public WidgetScenario ExpectBusy(string stepId, string nodeId, bool expected) =>
        ExpectNode(stepId, nodeId, node => (node.IsBusy == true) == expected
            ? null
            : $"Expected busy={expected}, received {node.IsBusy == true}.");

    public WidgetScenario ExpectFocus(string stepId, string nodeId) => Add(
        stepId,
        _ =>
        {
            var snapshot = Snapshot();
            if (!string.Equals(snapshot.InitialFocusId, nodeId, StringComparison.Ordinal))
                throw new WidgetScenarioAssertionException(
                    $"Expected focus '{nodeId}', received '{snapshot.InitialFocusId}'.");
            return ValueTask.CompletedTask;
        });

    public WidgetScenario ExpectIdle<TRequest, TResponse>(
        string stepId,
        WidgetScenarioOperationBarrier<TRequest, TResponse> barrier)
    {
        ArgumentNullException.ThrowIfNull(barrier);
        return Add(stepId, _ =>
        {
            if (barrier.ActiveInvocations != 0)
                throw new WidgetScenarioAssertionException(
                    $"Expected no active fake operations, received {barrier.ActiveInvocations}.");
            return ValueTask.CompletedTask;
        });
    }

    public async Task<WidgetScenarioRunResult> RunAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<WidgetScenarioStepResult>(_steps.Count + 1);
        ViewSnapshot? finalSnapshot = null;
        WidgetTestHost.Attach(_definition.Widget, _definition.HostServices);
        try
        {
            await WidgetTestHost.InitializeAsync(_definition.Widget, cancellationToken);
            foreach (var step in _steps)
            {
                try
                {
                    await step.Execute(cancellationToken).ConfigureAwait(false);
                    results.Add(new(step.Id, true, "passed", null));
                }
                catch (Exception exception) when (exception is not OutOfMemoryException &&
                    !(exception is OperationCanceledException &&
                        cancellationToken.IsCancellationRequested))
                {
                    results.Add(new(step.Id, false,
                        exception is WidgetScenarioAssertionException
                            ? "semantic_mismatch"
                            : exception is WidgetCapabilityException capability
                                ? capability.ErrorCode
                                : "step_failed",
                        Bounded(exception.Message)));
                    break;
                }
            }
            if (results.Count == _steps.Count && results.All(result => result.Passed))
                finalSnapshot = Snapshot();
        }
        finally
        {
            try { await WidgetTestHost.DestroyAsync(_definition.Widget, cancellationToken); }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                results.Add(new("destroy", false, "destroy_failed", Bounded(exception.Message)));
            }
        }
        var passed = results.Count == _steps.Count && results.All(result => result.Passed);
        return new(CaseId, passed, results.ToArray(), passed ? finalSnapshot : null);
    }

    private WidgetScenario ExpectNode(
        string stepId,
        string nodeId,
        Func<ViewNode, string?> assertion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentNullException.ThrowIfNull(assertion);
        return Add(stepId, _ =>
        {
            var node = Descendants(Snapshot().Root)
                .SingleOrDefault(candidate => candidate.Id == nodeId) ??
                throw new WidgetScenarioAssertionException($"Node '{nodeId}' was not found.");
            var failure = assertion(node);
            if (failure is not null) throw new WidgetScenarioAssertionException(failure);
            return ValueTask.CompletedTask;
        });
    }

    private WidgetScenario Add(
        string stepId,
        Func<CancellationToken, ValueTask> execute)
    {
        if (_steps.Count >= MaximumSteps)
            throw new InvalidOperationException(
                $"A scenario may contain at most {MaximumSteps} steps.");
        var id = ValidateId(stepId, nameof(stepId));
        if (_steps.Any(step => step.Id == id))
            throw new ArgumentException("Scenario step IDs must be unique.", nameof(stepId));
        _steps.Add(new(id, execute));
        return this;
    }

    private ViewSnapshot Snapshot() => _definition.Widget.RenderSnapshot(
        "scenario." + CaseId, Interlocked.Increment(ref _sequence));

    private static IEnumerable<ViewNode> Descendants(ViewNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Descendants(child))
                yield return descendant;
    }

    private static string ValidateId(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64 ||
            !char.IsAsciiLetterOrDigit(value[0]) ||
            value.Any(character => !(char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_')))
            throw new ArgumentException(
                "Scenario IDs must be bounded ASCII identifiers.", parameter);
        return value;
    }

    private static string Bounded(string value)
    {
        var single = value.Replace('\r', ' ').Replace('\n', ' ');
        return single.Length <= 256 ? single : single[..256];
    }

    private sealed record Step(string Id, Func<CancellationToken, ValueTask> Execute);
    private sealed class WidgetScenarioAssertionException(string message) : Exception(message);
}

public sealed record WidgetScenarioStepResult(
    string StepId,
    bool Passed,
    string Code,
    string? Message);

public sealed record WidgetScenarioRunResult(
    string CaseId,
    bool Passed,
    IReadOnlyList<WidgetScenarioStepResult> Steps,
    ViewSnapshot? FinalSnapshot);
