using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using GameBarAlternative.AvaloniaPrototype.Lifecycle;
using GameBarAlternative.AvaloniaPrototype.Composition;
using GameBarAlternative.AvaloniaPrototype.Navigation;
using GameBarAlternative.AvaloniaPrototype.Presentation;
using GameBarAlternative.AvaloniaPrototype.Remote;
using GameBarAlternative.AvaloniaPrototype.ViewModels;
using GameBarAlternative.AvaloniaPrototype.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameBarAlternative.AvaloniaPrototype.Tests;

[TestClass]
public sealed class InteractionTests
{
    [TestMethod]
    [Timeout(10_000)]
    public async Task Audio_slider_adjusts_horizontally_and_moves_spatially_in_the_aligned_column()
    {
        await RunOnUiThreadAsync(async () =>
        {
            var window = new MainWindow { Width = 1000, Height = 640 };
            window.Show();
            await window.NavigateAsync(PrototypeRoute.AudioMixer);
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            var page = (AudioMixerPage)window.ShellView.ActivePage!;
            var slider = page.FindControl<Slider>("MasterVolumeSlider")!;
            Assert.AreSame(slider, window.FocusManager?.GetFocusedElement(), "Down from tray must explicitly enter at the declared initial Slider.");
            var originalValue = slider.Value;
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.IsGreaterThan(originalValue, slider.Value, "Right must adjust the focused Slider.");
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            var nextSlider = window.FocusManager?.GetFocusedElement() as Control;
            Assert.IsNotNull(nextSlider);
            Assert.AreEqual("audio.game.volume", AutomationProperties.GetAutomationId(nextSlider));
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Up, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.AreSame(slider, window.FocusManager?.GetFocusedElement());
            window.Close();
        });
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task B_key_from_content_restores_the_selected_stationary_tray_item()
    {
        await RunOnUiThreadAsync(async () =>
        {
            var window = new MainWindow();
            window.Show();
            await window.NavigateAsync(PrototypeRoute.Settings);
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.AreNotSame(window.ShellView.SelectedTrayButton, window.FocusManager?.GetFocusedElement());

            window.KeyPress(Key.B, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.B, RawInputModifiers.None, PhysicalKey.None, null);

            Assert.IsTrue(window.ShellView.ContentRegionControl.IsVisible);
            Assert.AreSame(window.ShellView.SelectedTrayButton, window.FocusManager?.GetFocusedElement());
            window.Close();
        });
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Hiding_the_window_supersedes_destination_loading_and_retains_no_visible_work()
    {
        await RunOnUiThreadAsync(async () =>
        {
            var window = new MainWindow();
            window.Show();
            var pending = window.NavigateAsync(PrototypeRoute.GameLauncher);
            window.Hide();

            var result = await pending;
            Assert.AreNotEqual(NavigationOutcome.Committed, result.Outcome);
            Assert.AreEqual(PrototypeVisibility.Hidden, window.Lifecycle.Visibility);
            Assert.IsTrue(window.Lifecycle.VisibleLifetime.IsCancellationRequested);
            window.Close();
        });
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Virtualized_application_list_recycles_bounded_containers_and_restores_semantic_focus_and_scroll()
    {
        await RunOnUiThreadAsync(async () =>
        {
            var window = new MainWindow { Width = 900, Height = 420 };
            window.Show();
            await window.NavigateAsync(PrototypeRoute.GameLauncher);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            var page = (GameLauncherPage)window.ShellView.ActivePage!;
            var scroll = page.ApplicationScrollControl!;
            Assert.AreEqual(10_000, page.ViewModel.State.Items.Count);
            Assert.IsGreaterThan(0, scroll.Extent.Height - scroll.Viewport.Height, "Fixture must overflow its real ScrollViewer.");
            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.None, null);
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual("game-00001", page.FocusedSemanticId(window.FocusManager?.GetFocusedElement() as Control));
            Assert.IsTrue(page.RealizedContainerCount is >= 1 and <= 100,
                "Ordinary ListBox virtualization must not realize the 10,000-item model.");

            Assert.IsTrue(page.FocusIndex(9_000));
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual("game-09001", page.FocusedSemanticId(window.FocusManager?.GetFocusedElement() as Control));
            Assert.IsGreaterThan(0, scroll.Offset.Y);
            Assert.IsTrue(page.RealizedContainerCount is >= 1 and <= 100);

            await window.NavigateAsync(PrototypeRoute.Settings);
            await window.NavigateAsync(PrototypeRoute.GameLauncher);
            Assert.IsTrue(window.ShellView.TryEnterContent(window.ShellView.SelectedTrayButton));
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var replacement = (GameLauncherPage)window.ShellView.ActivePage!;
            Assert.AreEqual("game-09001", replacement.FocusedSemanticId(window.FocusManager?.GetFocusedElement() as Control),
                "Semantic item identity must restore after page replacement and container recycling.");
            Assert.IsGreaterThan(0, replacement.ApplicationScrollControl!.Offset.Y);

            Assert.IsTrue(replacement.FocusIndex(0));
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Assert.AreEqual(0, replacement.ApplicationScrollControl!.Offset.Y, 0.01);
            Assert.AreEqual(replacement.ViewModel.State.Items[0].Id.Value,
                replacement.FocusedSemanticId(window.FocusManager?.GetFocusedElement() as Control));
            window.Close();
        });
    }

    [TestMethod]
    [Timeout(15_000)]
    public async Task Latest_wins_reorder_restores_the_same_semantic_item_and_AutomationId_after_recycling()
    {
        await RunOnUiThreadAsync(async () =>
        {
            var endpoint = new ReorderingRemoteWidgetEndpoint();
            using var composition = PrototypeComposition.Create(endpoint);
            using var shell = new PrototypeShellView(composition);
            var window = new Window { Width = 900, Height = 420, Content = shell };
            window.Show();

            var navigation = shell.NavigateAsync(PrototypeRoute.GameLauncher);
            await endpoint.WaitForRequestsAsync(1);
            endpoint.Requests[0].Completion.SetResult(CreateOrderedSnapshot(1, targetIndex: 500, insertedPrefix: 0));
            await navigation;
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var page = (GameLauncherPage)shell.ActivePage!;
            var targetId = new RemoteWidgetItemId("stable/α:item");
            Assert.IsTrue(page.FocusIndex(500));
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var automationBefore = page.FocusedAutomationId(window.FocusManager?.GetFocusedElement() as Control);
            Assert.AreEqual(targetId.Value, page.FocusedSemanticId(window.FocusManager?.GetFocusedElement() as Control));
            Assert.AreEqual(SemanticAutomationIdentity.ForLauncherItem(targetId), automationBefore);

            var stale = page.ViewModel.RefreshAsync();
            var newest = page.ViewModel.RefreshAsync();
            await endpoint.WaitForRequestsAsync(3);
            await Task.Run(() => endpoint.Requests[2].Completion.SetResult(
                CreateOrderedSnapshot(3, targetIndex: 810, insertedPrefix: 10)));
            await newest;
            endpoint.Requests[1].Completion.SetResult(CreateOrderedSnapshot(2, targetIndex: 1, insertedPrefix: 1));
            await stale;
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();

            Assert.AreEqual(3L, composition.RemoteProjection.State.LastGoodSnapshot?.Revision);
            Assert.AreEqual(810, page.ViewModel.State.Items.ToList().FindIndex(item => item.Id == targetId),
                "The latest replacement must insert/reorder items ahead of the focused semantic item.");
            Assert.AreEqual(targetId.Value, page.FocusedSemanticId(window.FocusManager?.GetFocusedElement() as Control));
            Assert.AreEqual(automationBefore, page.FocusedAutomationId(window.FocusManager?.GetFocusedElement() as Control),
                "Automation identity must derive from RemoteWidgetItemId rather than list position.");
            Assert.IsTrue(page.RealizedContainerCount is >= 1 and <= 100);
            window.Close();
        });
    }

    private static async Task RunOnUiThreadAsync(Func<Task> action)
    {
        await Dispatcher.UIThread.InvokeAsync(action);
    }

    private static RemoteWidgetSnapshot CreateOrderedSnapshot(long revision, int targetIndex, int insertedPrefix)
    {
        var target = new RemoteWidgetItemSnapshot(
            new RemoteWidgetItemId("stable/α:item"),
            "Stable target",
            "Retains identity through reorder",
            [new RemoteWidgetDeclaredAction(GameLauncherViewModel.OpenActionId, "Open")]);
        var items = Enumerable.Range(0, 1_000)
            .Select(index => new RemoteWidgetItemSnapshot(
                new RemoteWidgetItemId($"fixture-{index:D4}"),
                $"Fixture {index:D4}",
                "Installed",
                [new RemoteWidgetDeclaredAction(GameLauncherViewModel.OpenActionId, "Open")]))
            .ToList();
        for (var index = 0; index < insertedPrefix; index++)
        {
            items.Insert(index, new RemoteWidgetItemSnapshot(
                new RemoteWidgetItemId($"inserted-{revision}-{index}"),
                $"Inserted {index}",
                "New",
                [new RemoteWidgetDeclaredAction(GameLauncherViewModel.OpenActionId, "Open")]));
        }

        items.Insert(targetIndex, target);
        return new RemoteWidgetSnapshot(revision, items, $"Revision {revision}");
    }

    private sealed class ReorderingRemoteWidgetEndpoint : IRemoteWidgetEndpoint
    {
        public List<PendingRequest> Requests { get; } = [];

        public Task<RemoteWidgetSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
        {
            var request = new PendingRequest(cancellationToken);
            Requests.Add(request);
            return request.Completion.Task;
        }

        public Task InvokeAsync(RemoteWidgetAction action, CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task WaitForRequestsAsync(int count)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (Requests.Count < count && DateTime.UtcNow < deadline)
            {
                await Task.Delay(5);
            }

            Assert.AreEqual(count, Requests.Count);
        }
    }

    private sealed record PendingRequest(CancellationToken CancellationToken)
    {
        public TaskCompletionSource<RemoteWidgetSnapshot> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
