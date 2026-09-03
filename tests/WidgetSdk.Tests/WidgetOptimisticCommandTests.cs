using WidgetRail.WidgetSdk;

internal static class WidgetOptimisticCommandTests
{
    public static async Task Run()
    {
        await RejectedInactiveDoesNotProjectAsync();
        await LatestReplacementPreservesNewIntentAndExternalStateAsync();
        await LatestFailureRollsBackTheFirstBaselineAsync();
        await FailureRollsBackOnceWithoutRetryAsync();
        await MapperFailureUsesSafeFallbackAsync();
        await SingleFlightJoinsWithoutProjectingDuplicateAsync();
        await SerialRequestsProjectInExecutionOrderAsync();
        await LifecycleCancellationRollsBackAndDrainsAsync();
        await ExecuteCanInspectCurrentAttemptAsync();
        await InvalidatedReentryKeepsTheNewOwnerAsync();
        await ReentrantApplyPublicationKeepsTheNewOwnerAsync();
        await ReentrantReconcilePublicationKeepsTheNewOwnerAsync();
    }

    private static async Task RejectedInactiveDoesNotProjectAsync()
    {
        var executions = 0;
        var widget = new CommandWidget(async (_, _) =>
        {
            executions++;
            await Task.Yield();
            return 1;
        });
        await WidgetTestHost.InitializeAsync(widget);

        var handle = widget.Run(1);
        Equal(WidgetOperationAdmission.RejectedInactive, handle.Admission);
        Equal(WidgetOperationStatus.Rejected, (await handle.Completion).Status);
        Equal(CommandState.Initial, widget.State);
        Equal(0, executions);
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task LatestReplacementPreservesNewIntentAndExternalStateAsync()
    {
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondResult = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var widget = new CommandWidget(async (execution, context) =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                firstStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
            }
            return await secondResult.Task.WaitAsync(context.CancellationToken);
        });
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);

        var first = widget.Run(1);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Equal(1, widget.State.Value);
        widget.PublishProviderRevision(7);

        var second = widget.Run(10);
        Equal(WidgetOperationAdmission.Replaced, second.Admission);
        Equal(11, widget.State.Value);
        Equal(7, widget.State.ProviderRevision);
        secondResult.TrySetResult(42);

        Equal(WidgetOperationStatus.Superseded, (await first.Completion).Status);
        Equal(WidgetOperationStatus.Succeeded, (await second.Completion).Status);
        Equal(42, widget.State.Value);
        Equal(7, widget.State.ProviderRevision);
        False(widget.State.Pending, "The newest successful command remained pending.");
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task FailureRollsBackOnceWithoutRetryAsync()
    {
        var calls = 0;
        var widget = new CommandWidget((_, _) =>
        {
            calls++;
            return ValueTask.FromException<int>(new InvalidOperationException("provider"));
        });
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);

        var result = await widget.Run(5).Completion;
        Equal(WidgetOperationStatus.Failed, result.Status);
        Equal(1, calls);
        Equal(0, widget.State.Value);
        Equal("provider_error", widget.State.ErrorCode);
        False(widget.State.Pending, "A failed command remained pending.");
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task LatestFailureRollsBackTheFirstBaselineAsync()
    {
        var firstStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var widget = new CommandWidget(async (_, context) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                firstStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
            }
            throw new InvalidOperationException("replacement failed");
        });
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);

        var first = widget.Run(1);
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var replacement = widget.Run(10);
        Equal(11, widget.State.Value);
        Equal(WidgetOperationStatus.Superseded, (await first.Completion).Status);
        Equal(WidgetOperationStatus.Failed, (await replacement.Completion).Status);
        Equal(0, widget.State.Value);
        Equal("provider_error", widget.State.ErrorCode);
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task MapperFailureUsesSafeFallbackAsync()
    {
        var widget = new CommandWidget(
            (_, _) => ValueTask.FromException<int>(new InvalidOperationException("provider")),
            throwFromMapper: true);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);

        var result = await widget.Run(2).Completion;
        Equal(WidgetOperationStatus.Failed, result.Status);
        Equal(WidgetCommandError.Unexpected.Code, widget.State.ErrorCode);
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task SingleFlightJoinsWithoutProjectingDuplicateAsync()
    {
        var result = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var widget = new CommandWidget((_, context) =>
        {
            calls++;
            return new(result.Task.WaitAsync(context.CancellationToken));
        }, policy: WidgetCommandPolicy.SingleFlight);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);

        var first = widget.Run(2);
        await WaitUntil(() => calls == 1);
        var duplicate = widget.Run(50);
        Equal(WidgetOperationAdmission.Joined, duplicate.Admission);
        Equal(2, widget.State.Value);
        Equal(1, calls);

        result.TrySetResult(7);
        Equal(WidgetOperationStatus.Succeeded, (await first.Completion).Status);
        Equal(WidgetOperationStatus.Succeeded, (await duplicate.Completion).Status);
        Equal(7, widget.State.Value);
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task SerialRequestsProjectInExecutionOrderAsync()
    {
        var first = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var widget = new CommandWidget((_, context) =>
        {
            var gate = Interlocked.Increment(ref calls) == 1 ? first : second;
            return new(gate.Task.WaitAsync(context.CancellationToken));
        }, policy: WidgetCommandPolicy.Serial);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);

        var firstHandle = widget.Run(1);
        await WaitUntil(() => calls == 1);
        var secondHandle = widget.Run(10);
        Equal(WidgetOperationAdmission.Enqueued, secondHandle.Admission);
        Equal(1, widget.State.Value);

        first.TrySetResult(2);
        Equal(WidgetOperationStatus.Succeeded, (await firstHandle.Completion).Status);
        await WaitUntil(() => calls == 2);
        Equal(12, widget.State.Value);
        second.TrySetResult(20);
        Equal(WidgetOperationStatus.Succeeded, (await secondHandle.Completion).Status);
        Equal(20, widget.State.Value);
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task LifecycleCancellationRollsBackAndDrainsAsync()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var widget = new CommandWidget(async (_, context) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
            return 0;
        });
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);

        var handle = widget.Run(3);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Equal(3, widget.State.Value);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);

        Equal(WidgetOperationStatus.Canceled, (await handle.Completion).Status);
        Equal(0, widget.State.Value);
        False(widget.State.Pending, "Lifecycle cancellation did not roll back pending state.");
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task ExecuteCanInspectCurrentAttemptAsync()
    {
        WidgetOperationContext? observed = null;
        var wasCurrentDuringExecute = false;
        var widget = new CommandWidget((_, context) =>
        {
            observed = context;
            wasCurrentDuringExecute = context.IsCurrent;
            return ValueTask.FromResult(4);
        });
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);

        Equal(WidgetOperationStatus.Succeeded, (await widget.Run(4).Completion).Status);
        True(observed is not null, "Execute did not receive an operation context.");
        True(wasCurrentDuringExecute, "The attempt was not current during execution.");
        Equal(4, widget.State.Value);
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task ReentrantApplyPublicationKeepsTheNewOwnerAsync()
    {
        var replacementStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var replacementResult = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var widget = new CommandWidget(async (execution, context) =>
        {
            if (execution.Delta == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
                return 0;
            }
            replacementStarted.TrySetResult();
            return await replacementResult.Task.WaitAsync(context.CancellationToken);
        });
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);

        WidgetOperationHandle? replacement = null;
        var replaced = 0;
        widget.Model.Changed += (_, change) =>
        {
            if (change.Current.Value.Pending &&
                Interlocked.CompareExchange(ref replaced, 1, 0) == 0)
                replacement = widget.Run(2);
        };

        var original = widget.Run(1);
        True(replacement is not null, "Changed did not reenter the command during Apply publication.");
        await replacementStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        replacementResult.TrySetResult(20);

        Equal(WidgetOperationStatus.Superseded, (await original.Completion).Status);
        Equal(WidgetOperationStatus.Succeeded, (await replacement!.Value.Completion).Status);
        Equal(20, widget.State.Value);
        False(widget.State.Pending, "The reentrant Apply owner was cleared by its predecessor.");
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task InvalidatedReentryKeepsTheNewOwnerAsync()
    {
        var replacementStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var replacementResult = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var widget = new CommandWidget(async (execution, context) =>
        {
            if (execution.Delta == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
                return 0;
            }
            replacementStarted.TrySetResult();
            return await replacementResult.Task.WaitAsync(context.CancellationToken);
        }, runtimeModel: true);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);

        WidgetOperationHandle? replacement = null;
        CommandState? firstInvalidatedState = null;
        var reentered = 0;
        widget.Invalidated += (_, _) =>
        {
            if (Interlocked.CompareExchange(ref reentered, 1, 0) != 0) return;
            firstInvalidatedState = widget.State;
            replacement = widget.Run(2);
        };

        var original = widget.Run(1);
        True(replacement is not null,
            "The synchronous widget invalidation did not reenter the command.");
        Equal(1, firstInvalidatedState!.Value);
        True(firstInvalidatedState.Pending,
            "Latest admission published busy before installing its projection owner.");
        Equal(WidgetOperationAdmission.Replaced, replacement!.Value.Admission);
        await replacementStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        replacementResult.TrySetResult(20);

        Equal(WidgetOperationStatus.Superseded, (await original.Completion).Status);
        Equal(WidgetOperationStatus.Succeeded, (await replacement.Value.Completion).Status);
        Equal(20, widget.State.Value);
        False(widget.State.Pending,
            "The invalidation-reentrant owner was cleared by its predecessor.");
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task ReentrantReconcilePublicationKeepsTheNewOwnerAsync()
    {
        var first = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var widget = new CommandWidget((_, context) => new(
            (Interlocked.Increment(ref calls) == 1 ? first : second).Task
                .WaitAsync(context.CancellationToken)));
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Interactive);

        WidgetOperationHandle? replacement = null;
        widget.Model.Changed += (_, change) =>
        {
            if (!change.Current.Value.Pending && change.Current.Value.Value == 10)
                replacement = widget.Run(2);
        };

        var original = widget.Run(1);
        await WaitUntil(() => calls == 1);
        first.TrySetResult(10);
        await WaitUntil(() => replacement is not null);
        Equal(WidgetOperationAdmission.Replaced, replacement!.Value.Admission);
        var originalStatus = (await original.Completion).Status;
        True(originalStatus == WidgetOperationStatus.Superseded,
            "A terminal-publication successor did not supersede the still-active predecessor.");
        await WaitUntil(() => calls == 2);
        second.TrySetResult(20);

        True(replacement is not null,
            "Changed did not reenter the command during Reconcile publication.");
        Equal(WidgetOperationStatus.Succeeded, (await replacement!.Value.Completion).Status);
        Equal(20, widget.State.Value);
        False(widget.State.Pending, "The reentrant Reconcile owner was cleared by its predecessor.");
        await WidgetTestHost.DestroyAsync(widget);
    }

    private sealed record CommandState(
        int Value,
        bool Pending,
        int ProviderRevision,
        string? ErrorCode)
    {
        internal static CommandState Initial { get; } = new(0, false, 0, null);
    }

    private sealed record Execution(int Delta);

    private sealed class CommandWidget : Widget
    {
        private readonly WidgetModel<CommandState> _model;
        private readonly WidgetOptimisticCommand<CommandState, int, Execution, int> _command;

        internal CommandWidget(
            Func<Execution, WidgetOperationContext, ValueTask<int>> execute,
            WidgetCommandPolicy policy = WidgetCommandPolicy.Latest,
            bool throwFromMapper = false,
            bool runtimeModel = false)
        {
            _model = runtimeModel
                ? CreateModel(CommandState.Initial)
                : WidgetModel<CommandState>.CreateForTesting(CommandState.Initial);
            _command = CreateOptimisticCommand(
                "test.optimistic",
                _model,
                new WidgetOptimisticCommandOptions<CommandState, int, Execution, int>
                {
                    Policy = policy,
                    Apply = (state, delta) => new(
                        state with
                        {
                            Value = state.Value + delta,
                            Pending = true,
                            ErrorCode = null,
                        },
                        new Execution(delta)),
                    Execute = execute,
                    Reconcile = (state, _, result) => state with
                    {
                        Value = result,
                        Pending = false,
                    },
                    Rollback = (state, baseline, _) => state with
                    {
                        Value = baseline.Value,
                        Pending = false,
                    },
                    MapError = throwFromMapper
                        ? _ => throw new InvalidOperationException("mapper")
                        : _ => new WidgetCommandError("provider_error", "Provider failed."),
                    Fail = (state, baseline, _, error) => state with
                    {
                        Value = baseline.Value,
                        Pending = false,
                        ErrorCode = error.Code,
                    },
                });
        }

        internal CommandState State => _model.Value;
        internal WidgetModel<CommandState> Model => _model;
        internal WidgetOperationHandle Run(int delta) => _command.Run(delta);
        internal void PublishProviderRevision(int revision) =>
            _model.Update(state => state with { ProviderRevision = revision });

        public override WidgetView Render() => new(
            UI.Stack("command.root", UI.Text(State.Value.ToString(), "command.value")));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Condition was not reached.");
            await Task.Delay(10);
        }
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void False(bool value, string message) => True(!value, message);

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}
