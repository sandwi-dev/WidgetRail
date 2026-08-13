using Avalonia.Threading;
using GameBarAlternative.AvaloniaPrototype.Presentation;
using GameBarAlternative.AvaloniaPrototype.Remote;
using GameBarAlternative.AvaloniaPrototype.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameBarAlternative.AvaloniaPrototype.Tests;

[TestClass]
public sealed class RemoteProjectionTests
{
    [TestMethod]
    [Timeout(10_000)]
    public async Task Projection_is_latest_wins_retains_last_good_dispatches_exact_action_and_cancels_on_deactivation()
    {
        var endpoint = new ControlledRemoteWidgetEndpoint();
        using var projection = new RemoteWidgetProjection(endpoint);
        using var viewModel = new GameLauncherViewModel(projection, new ImmediatePresentationScheduler());

        var first = viewModel.ActivateAsync();
        Assert.AreEqual(1, endpoint.Requests.Count);
        var second = viewModel.RefreshAsync();
        Assert.AreEqual(2, endpoint.Requests.Count);
        endpoint.Requests[1].Completion.SetResult(Snapshot(2, "Newest"));
        await second;
        endpoint.Requests[0].Completion.SetResult(Snapshot(1, "Stale"));
        await first;

        Assert.AreEqual(2L, projection.State.LastGoodSnapshot?.Revision);
        Assert.AreEqual("Newest", viewModel.State.Items[0].Title);

        var failed = viewModel.RefreshAsync();
        endpoint.Requests[2].Completion.SetException(new InvalidOperationException("fixture failure"));
        await failed;
        Assert.AreEqual(2L, projection.State.LastGoodSnapshot?.Revision, "Failure must retain the last good snapshot.");
        Assert.IsNotNull(projection.State.Failure);
        StringAssert.Contains(viewModel.State.Status, "last good snapshot");

        var selected = viewModel.State.Items[0];
        await viewModel.InvokeItemAsync(selected);
        Assert.AreEqual(1, endpoint.Actions.Count);
        Assert.AreEqual(new RemoteWidgetAction(selected.Id, GameLauncherViewModel.OpenActionId), endpoint.Actions[0]);

        await AssertRejectedAsync(() => projection.InvokeAsync(
            new RemoteWidgetAction(new RemoteWidgetItemId("unknown-item"), GameLauncherViewModel.OpenActionId)));
        await AssertRejectedAsync(() => projection.InvokeAsync(
            new RemoteWidgetAction(selected.Id, new RemoteWidgetActionId("delete"))));
        Assert.AreEqual(1, endpoint.Actions.Count, "Rejected item/action tuples must not reach the endpoint.");

        var cancelled = viewModel.RefreshAsync();
        var pending = endpoint.Requests[3];
        viewModel.Deactivate();
        Assert.IsTrue(pending.CancellationToken.IsCancellationRequested);
        pending.Completion.SetResult(Snapshot(3, "Must not publish"));
        await cancelled;
        Assert.AreEqual(2L, projection.State.LastGoodSnapshot?.Revision);
        Assert.IsFalse(projection.State.IsLoading);
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Worker_completion_publishes_bound_state_only_through_the_Avalonia_scheduler()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var endpoint = new ControlledRemoteWidgetEndpoint();
            using var projection = new RemoteWidgetProjection(endpoint);
            var scheduler = new AvaloniaUiScheduler();
            using var viewModel = new GameLauncherViewModel(projection, scheduler);
            var publicationThreads = new List<int>();
            var allPublicationsHadUiAccess = true;
            viewModel.PropertyChanged += (_, _) =>
            {
                publicationThreads.Add(Environment.CurrentManagedThreadId);
                allPublicationsHadUiAccess &= Dispatcher.UIThread.CheckAccess();
            };

            var activation = viewModel.ActivateAsync();
            Assert.AreEqual(1, endpoint.Requests.Count);
            var workerThread = 0;
            await Task.Run(() =>
            {
                workerThread = Environment.CurrentManagedThreadId;
                endpoint.Requests[0].Completion.SetResult(Snapshot(1, "Worker result"));
            });
            await activation;

            Assert.IsFalse(Dispatcher.UIThread.CheckAccess() && Environment.CurrentManagedThreadId == workerThread);
            Assert.IsTrue(publicationThreads.Count > 0);
            Assert.IsTrue(allPublicationsHadUiAccess, "Every bound ObservableObject mutation must occur with Avalonia UI-thread access.");
            Assert.AreNotEqual(workerThread, scheduler.LastExecutionThreadId);
            Assert.IsGreaterThan(0, scheduler.MarshalledInvocationCount,
                "A worker completion must be explicitly marshalled by the presentation scheduler.");
            Assert.AreEqual("Worker result", viewModel.State.Items[0].Title);
        });
    }

    [TestMethod]
    public void Semantic_automation_identity_is_bounded_reversible_and_collision_safe()
    {
        RemoteWidgetItemId[] ids =
        [
            new("game/one"),
            new("game?one"),
            new("é"),
            new("e\u0301"),
            new(new string('x', RemoteWidgetItemId.MaximumUtf8Bytes)),
        ];
        var encoded = ids.Select(SemanticAutomationIdentity.ForLauncherItem).ToArray();
        Assert.AreEqual(encoded.Length, encoded.Distinct(StringComparer.Ordinal).Count());
        foreach (var pair in ids.Zip(encoded))
        {
            Assert.IsTrue(pair.Second.StartsWith(SemanticAutomationIdentity.LauncherItemPrefix, StringComparison.Ordinal));
            Assert.IsLessThanOrEqualTo(SemanticAutomationIdentity.MaximumAutomationIdLength, pair.Second.Length);
            Assert.IsTrue(SemanticAutomationIdentity.TryDecodeLauncherItem(pair.Second, out var decoded));
            Assert.AreEqual(pair.First, decoded);
        }

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            _ = new RemoteWidgetItemId(new string('x', RemoteWidgetItemId.MaximumUtf8Bytes + 1)));
    }

    [TestMethod]
    public void Remote_contract_is_ui_framework_neutral_and_shell_commands_only_request_presentation_navigation()
    {
        var boundaryTypes = typeof(IRemoteWidgetEndpoint).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(IRemoteWidgetEndpoint).Namespace)
            .ToArray();
        foreach (var type in boundaryTypes)
        {
            foreach (var referenced in ReferencedTypes(type))
            {
                Assert.IsFalse(referenced.Namespace?.StartsWith("Avalonia", StringComparison.Ordinal) == true,
                    $"Remote boundary {type.Name} leaked Avalonia type {referenced.FullName}.");
            }
        }

        var shell = new PrototypeShellViewModel();
        PrototypeRoute? requested = null;
        shell.RouteRequested += (_, route) => requested = route;
        shell.ShowLauncherCommand.Execute(null);
        Assert.AreEqual(PrototypeRoute.GameLauncher, requested);
        Assert.AreEqual(PrototypeRoute.Settings, shell.State.SelectedRoute,
            "The view model requests a route; the presentation service owns admission and committed state.");
    }

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        foreach (var property in type.GetProperties())
        {
            foreach (var referenced in Flatten(property.PropertyType)) yield return referenced;
        }

        foreach (var method in type.GetMethods().Where(method => method.DeclaringType == type))
        {
            foreach (var referenced in Flatten(method.ReturnType)) yield return referenced;
            foreach (var parameter in method.GetParameters())
            {
                foreach (var referenced in Flatten(parameter.ParameterType)) yield return referenced;
            }
        }
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                foreach (var nested in Flatten(argument)) yield return nested;
            }
        }
    }

    private static RemoteWidgetSnapshot Snapshot(long revision, string title) =>
        new(
            revision,
            [new RemoteWidgetItemSnapshot(
                new RemoteWidgetItemId("game-00001"),
                title,
                "Fixture",
                [new RemoteWidgetDeclaredAction(GameLauncherViewModel.OpenActionId, "Open")])],
            $"Revision {revision}");

    private static async Task AssertRejectedAsync(Func<Task> action)
    {
        try
        {
            await action();
            Assert.Fail("Undeclared remote action was admitted.");
        }
        catch (ArgumentException)
        {
        }
    }

    private sealed class ControlledRemoteWidgetEndpoint : IRemoteWidgetEndpoint
    {
        public List<Request> Requests { get; } = [];
        public List<RemoteWidgetAction> Actions { get; } = [];

        public Task<RemoteWidgetSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            var request = new Request(cancellationToken);
            Requests.Add(request);
            return request.Completion.Task;
        }

        public Task InvokeAsync(RemoteWidgetAction action, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Actions.Add(action);
            return Task.CompletedTask;
        }
    }

    private sealed record Request(CancellationToken CancellationToken)
    {
        public TaskCompletionSource<RemoteWidgetSnapshot> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ImmediatePresentationScheduler : IPresentationScheduler
    {
        public bool CheckAccess() => true;

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }
    }
}
