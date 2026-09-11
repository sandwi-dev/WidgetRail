using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class WidgetCursorResourceTests
{
    public static async Task Run()
    {
        await EqualVisibleDemandJoinsPendingPage();
        await CapturedPresentationSurvivesConcurrentEviction();
        await PendingPagePreservesNewAnchor();
        await FailedFocusMappingDoesNotCommitSegments();
        await VisibleItemsCanExceedEvictionTarget();
        await TraversesTenThousandItemsWithinBound();
        await ProjectsVersionedTenThousandItemVirtualWindow();
        await HandlesEmptySparseFinalAndLastGoodError();
        await PreservesAnchorAcrossAppendPrependAndRefresh();
        await DirectionChangeAllowsEvictedRefetch();
        await TraversalHistoryFailsClosedAndRefreshResetsIt();
        await RejectsLateDuplicateAndLoopResults();
        await IdenticalIntentJoinsOneLoad();
        await DifferentIntentReplacesCurrentLoad();
        await ResetCancelsJoinedLoadAndAllowsFreshWork();
        await ActiveLifecycleDrainsJoinedLoad();
        ContractIsVersionedOpaqueAndBounded();
    }

    private static async Task EqualVisibleDemandJoinsPendingPage()
    {
        var started = Signal(); var release = Signal();
        var widget = await StartAsync(Options(40, pageSize: 4, maximumRetainedItems: 8,
            async: async (cursor, direction, limit, token) =>
            {
                if (direction is not null) { started.SetResult(); await release.Task.WaitAsync(token); }
                return Page(cursor is null ? 0 : int.Parse(cursor.Value.Value.AsSpan(1)), limit, 40);
            }));
        await widget.Resource.EnsureLoaded().Completion;
        WidgetActionEvent Demand() => new("test.cursor.cursor.after", "items.list")
            { VisibleCollectionKeys = widget.Resource.Snapshot.Items.Select(item => item.Id).ToArray() };
        True(widget.Resource.TryHandlePagination(Demand(), out var first), "First protected demand is handled.");
        await started.Task;
        True(widget.Resource.TryHandlePagination(Demand(), out var second), "Second protected demand is handled.");
        Equal(WidgetOperationAdmission.Joined, second.Admission);
        True(ReferenceEquals(first.Completion, second.Completion), "Equivalent visible-key sets must join one pending page.");
        release.SetResult(); await first.Completion;
        await StopAsync(widget);
    }

    private static async Task CapturedPresentationSurvivesConcurrentEviction()
    {
        var widget = await StartAsync(Options(40, pageSize: 4, maximumRetainedItems: 8));
        await widget.Resource.EnsureLoaded().Completion;
        var capture = widget.Resource.Capture();
        await widget.Resource.Prefetch(WidgetCursorDirection.After, "items.list").Completion;
        await widget.Resource.Prefetch(WidgetCursorDirection.After, "items.list").Completion;
        Equal<string?>(null, widget.Resource.Snapshot.RequestedFocusId);
        Equal<CollectionNavigationRequest?>(null, widget.Resource.Snapshot.NavigationRequest);
        var items = capture.Snapshot.Items.Select(item => capture.PresentItem(item,
            UI.Button(item.Id, "select", "focus." + item.Id))).ToArray();
        var snapshot = new WidgetView(capture.Present(UI.VerticalScroll("items.list", items)))
            .CreateSnapshot("capture.test", 1);
        Equal("item.0", snapshot.Root.CollectionAnchorKey);
        Equal<long?>(0, snapshot.Root.CollectionStartIndex);
        Equal(4L, widget.Resource.Snapshot.StartIndex);
        var oldProtocol = snapshot with { ProtocolVersion = 46 };
        True(ViewSnapshotValidator.Validate(oldProtocol).Any(error => error.Code == "feature_requires_version"),
            "Collection positions must require their protocol version.");
        await StopAsync(widget);
    }

    private static async Task PendingPagePreservesNewAnchor()
    {
        var started = Signal(); var release = Signal();
        var widget = await StartAsync(Options(40, pageSize: 4, maximumRetainedItems: 8,
            async: async (cursor, direction, limit, token) =>
            {
                if (direction is not null) { started.SetResult(); await release.Task.WaitAsync(token); }
                return Page(cursor is null ? 0 : int.Parse(cursor.Value.Value.AsSpan(1)), limit, 40);
            }));
        await widget.Resource.EnsureLoaded().Completion;
        var pending = widget.Resource.Move(WidgetCursorDirection.After, "items.list");
        await started.Task;
        widget.Resource.SelectAnchor(new("item.2"));
        release.SetResult(); await pending.Completion;
        Equal(new WidgetCollectionItemKey("item.2"), widget.Resource.Snapshot.Anchor);
        Equal<string?>(null, widget.Resource.Snapshot.RequestedFocusId);
        Equal<CollectionNavigationRequest?>(null, widget.Resource.Snapshot.NavigationRequest);
        await StopAsync(widget);
    }

    private static async Task FailedFocusMappingDoesNotCommitSegments()
    {
        bool fail = true;
        var widget = await StartAsync(Options(40, pageSize: 4, maximumRetainedItems: 8) with
        {
            Viewports = [new("items.list", item => new(item.Id),
                item => fail ? "invalid focus" : "focus." + item.Id)],
        });
        await widget.Resource.EnsureLoaded().Completion;
        Equal(WidgetOperationStatus.Failed,
            (await widget.Resource.Move(WidgetCursorDirection.After, "items.list").Completion).Status);
        Equal(4, widget.Resource.Snapshot.Items.Count);
        fail = false;
        Equal(WidgetOperationStatus.Succeeded, (await widget.Resource.Retry().Completion).Status);
        Equal(8, widget.Resource.Snapshot.Items.Count);
        Equal("focus.item.4", widget.Resource.Snapshot.NavigationRequest?.TargetFocusId);
        await StopAsync(widget);
    }

    private static async Task VisibleItemsCanExceedEvictionTarget()
    {
        var widget = await StartAsync(Options(34, pageSize: 6, maximumRetainedItems: 60) with
            { RetainedItemTarget = 24 });
        await widget.Resource.EnsureLoaded().Completion;
        for (int i = 0; i < 5; ++i)
        {
            var visible = widget.Resource.Snapshot.Items.Select(item => item.Id).ToArray();
            True(widget.Resource.TryHandlePagination(new("test.cursor.cursor.after", "items.list")
                { VisibleCollectionKeys = visible }, out var pending), "Cursor prefetch was not handled.");
            Equal(WidgetOperationStatus.Succeeded, (await pending.Completion).Status);
            True(visible.All(key => widget.Resource.Snapshot.Items.Any(item => item.Id == key)),
                "Filling a large viewport evicted a visible item.");
        }
        Equal(34, widget.Resource.Snapshot.Items.Count);
        True(!widget.Resource.Snapshot.HasAfter, "The partial final page did not end traversal.");
        await StopAsync(widget);
    }

    private static async Task IdenticalIntentJoinsOneLoad()
    {
        var started = Signal();
        var release = Signal();
        var calls = 0;
        var widget = await StartAsync(Options(200, async: async (_, _, limit, token) =>
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            await release.Task.WaitAsync(token);
            return Page(0, limit, 200);
        }));
        var first = widget.Resource.EnsureLoaded();
        await started.Task;
        var joined = widget.Resource.EnsureLoaded();
        Equal(WidgetOperationAdmission.Started, first.Admission);
        Equal(WidgetOperationAdmission.Joined, joined.Admission);
        True(ReferenceEquals(first.Completion, joined.Completion),
            "An identical cursor intent did not share the exact completion.");
        Equal(1, Volatile.Read(ref calls));
        release.SetResult();
        Equal(WidgetOperationStatus.Succeeded, (await first.Completion).Status);
        Equal(WidgetOperationStatus.Succeeded, (await joined.Completion).Status);
        Equal(1, Volatile.Read(ref calls));
        await StopAsync(widget);
    }

    private static async Task DifferentIntentReplacesCurrentLoad()
    {
        var firstStarted = Signal();
        var firstRelease = Signal();
        var secondStarted = Signal();
        var calls = 0;
        var widget = await StartAsync(Options(200, async: async (_, _, limit, _) =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                firstStarted.TrySetResult();
                await firstRelease.Task;
                return Page(0, limit, 200);
            }
            secondStarted.TrySetResult();
            return Page(100, limit, 200);
        }));
        var first = widget.Resource.EnsureLoaded();
        await firstStarted.Task;
        var replacement = widget.Resource.Refresh();
        Equal(WidgetOperationAdmission.Replaced, replacement.Admission);
        firstRelease.SetResult();
        await secondStarted.Task;
        Equal(WidgetOperationStatus.Superseded, (await first.Completion).Status);
        Equal(WidgetOperationStatus.Succeeded, (await replacement.Completion).Status);
        Equal(2, Volatile.Read(ref calls));
        Equal("item.100", widget.Resource.Snapshot.Items[0].Id);
        await StopAsync(widget);
    }

    private static async Task ResetCancelsJoinedLoadAndAllowsFreshWork()
    {
        var started = Signal();
        var canceled = Signal();
        var calls = 0;
        var widget = await StartAsync(Options(200, async: async (_, _, limit, token) =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                using var registration = token.Register(() => canceled.TrySetResult());
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            return Page(0, limit, 200);
        }));
        var first = widget.Resource.EnsureLoaded();
        await started.Task;
        var joined = widget.Resource.EnsureLoaded();
        widget.Resource.Reset(invalidate: false);
        await canceled.Task;
        Equal(WidgetOperationStatus.Canceled, (await first.Completion).Status);
        Equal(WidgetOperationStatus.Canceled, (await joined.Completion).Status);
        Equal(WidgetPagedResourceStatus.NotLoaded, widget.Resource.Snapshot.Status);
        Equal(WidgetOperationStatus.Succeeded,
            (await widget.Resource.EnsureLoaded().Completion).Status);
        Equal(2, Volatile.Read(ref calls));
        await StopAsync(widget);
    }

    private static async Task ActiveLifecycleDrainsJoinedLoad()
    {
        var started = Signal();
        var canceled = Signal();
        var calls = 0;
        var widget = await StartAsync(Options(200, async: async (_, _, limit, token) =>
        {
            Interlocked.Increment(ref calls);
            using var registration = token.Register(() => canceled.TrySetResult());
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Page(0, limit, 200);
        }));
        var first = widget.Resource.EnsureLoaded();
        await started.Task;
        var joined = widget.Resource.EnsureLoaded();
        var background = WidgetTestHost.SetLifecycleStateAsync(
            widget, WidgetLifecycleState.Background).AsTask();
        await canceled.Task;
        await background;
        Equal(WidgetOperationStatus.Canceled, (await first.Completion).Status);
        Equal(WidgetOperationStatus.Canceled, (await joined.Completion).Status);
        Equal(1, Volatile.Read(ref calls));
        await WidgetTestHost.DestroyAsync(widget);
    }

    private static async Task TraversesTenThousandItemsWithinBound()
    {
        await TraverseWithinBound(2_000);
        await TraverseWithinBound(10_000);
    }

    private static async Task TraverseWithinBound(int total)
    {
        const int pageSize = 64;
        const int retainedItems = 192;
        var widget = await StartAsync(Options(total,
            pageSize: pageSize, maximumRetainedItems: retainedItems));
        Equal(WidgetOperationStatus.Succeeded,
            (await widget.Resource.EnsureLoaded().Completion).Status);
        var pageCount = (total + pageSize - 1) / pageSize;
        for (var page = 1; page < pageCount; page++)
        {
            Equal(WidgetOperationStatus.Succeeded,
                (await widget.Resource.Move(WidgetCursorDirection.After, "items.list").Completion).Status);
            True(widget.Resource.RetainedItemCount <= retainedItems,
                "A 10,000-item provider escaped the retained window.");
            True(widget.Render().CreateSnapshot("cursor.fixture", page).Root.Children
                    .SelectMany(Flatten).Count() <= retainedItems + 3,
                "A 10,000-item provider serialized an unbounded snapshot.");
        }
        Equal($"item.{total - 1}", widget.Resource.Snapshot.Items[^1].Id);
        Equal($"focus.item.{total - (total % pageSize == 0 ? pageSize : total % pageSize)}",
            widget.Resource.Snapshot.RequestedFocusId);
        True(widget.Resource.RetainedCursorCount <= WidgetCursorResource<Item>.MaximumCursorHistory,
            "Cursor history exceeded its explicit bound.");
        await StopAsync(widget);
    }

    private static async Task ProjectsVersionedTenThousandItemVirtualWindow()
    {
        const int total = 10_000;
        const int pageSize = 32;
        var fail = false;
        var mutation = 0;
        var widget = await StartAsync(new()
        {
            PageSize = pageSize,
            MaximumRetainedItems = 96,
            PaginationThreshold = 2,
            Viewports = [Viewport() with { EstimatedItemExtent = 56 }],
            MapError = _ => WidgetResourceError.InvalidPage,
            LoadPage = (cursor, direction, limit, _) =>
            {
                if (fail)
                    return ValueTask.FromException<WidgetCursorPage<Item>>(
                        new InvalidOperationException("controlled virtual page failure"));
                var start = cursor is null ? 0 : int.Parse(cursor.Value.Value.AsSpan(1));
                var page = VirtualPage(start, limit, total);
                if (direction is null)
                {
                    page = mutation switch
                    {
                        1 => page with
                        {
                            Items = [new("item.inserted"), .. page.Items.Take(limit - 1)],
                            TotalItemCount = total + 1,
                        },
                        2 => page with
                        {
                            Items = [.. page.Items.Skip(1), new("item.replacement")],
                            TotalItemCount = total - 1,
                        },
                        3 => page with
                        {
                            Items = [page.Items[1], page.Items[0], .. page.Items.Skip(2)],
                        },
                        _ => page,
                    };
                }
                return ValueTask.FromResult(page);
            },
        });
        await widget.Resource.EnsureLoaded().Completion;
        var initial = widget.Render().CreateSnapshot("virtual.fixture", 1);
        Equal(ProtocolConstants.CollectionPositionVersion, initial.ProtocolVersion);
        var window = initial.Root.Children[0].VirtualCollectionWindow!;
        Equal(1L, window.RequestGeneration);
        Equal(VirtualCollectionWindowChange.Replace, window.Change);
        Equal(0L, window.FirstItemIndex);
        Equal((long)total, window.TotalItemCount);
        True(!window.HasBefore && window.HasAfter,
            "Initial virtual boundaries did not reflect the logical collection.");
        Equal(pageSize, initial.Root.Children[0].Children.Count);

        await widget.Resource.Move(WidgetCursorDirection.After, "items.list").Completion;
        await widget.Resource.Move(WidgetCursorDirection.After, "items.list").Completion;
        await widget.Resource.Move(WidgetCursorDirection.After, "items.list").Completion;
        var shifted = widget.Render().CreateSnapshot("virtual.fixture", 2);
        window = shifted.Root.Children[0].VirtualCollectionWindow!;
        Equal(VirtualCollectionWindowChange.Append, window.Change);
        Equal(32L, window.FirstItemIndex);
        Equal((long)total, window.TotalItemCount);
        Equal(4L, window.RequestGeneration);
        True(window.HasBefore && window.HasAfter,
            "A shifted virtual window lost its bidirectional cursor authority.");
        True(shifted.Root.Children[0].Children.Count <= 96,
            "The 10,000-item private collection escaped the admitted window.");
        True(Flatten(shifted.Root).Count() <= 98,
            "Virtual metadata materialized off-window semantic nodes.");

        var encoded = SnapshotJson.Serialize(shifted);
        True(System.Text.Encoding.UTF8.GetString(encoded).Contains(
                "\"change\":\"append\"", StringComparison.Ordinal),
            "Virtual window change did not use the closed camel-case wire value.");
        var restored = SnapshotJson.Deserialize(encoded);
        Equal(window, restored.Root.Children[0].VirtualCollectionWindow);
        var legacy = shifted with
        {
            ProtocolVersion = ProtocolConstants.AtomicPresentationUpdateVersion,
        };
        True(ViewSnapshotValidator.Validate(legacy).Any(error =>
                error.Code == "feature_requires_version"),
            "Protocol v18 admitted virtual collection metadata.");
        var malformedScroll = shifted.Root.Children[0] with
        {
            VirtualCollectionWindow = window with { TotalItemCount = 33 },
        };
        var malformed = shifted with
        {
            Root = shifted.Root with { Children = [malformedScroll] },
        };
        True(ViewSnapshotValidator.Validate(malformed).Any(error =>
                error.Code == "virtual_collection_window_out_of_range"),
            "An out-of-range logical window was not rejected.");
        var unknownExtent = shifted with
        {
            Root = shifted.Root with
            {
                Children =
                [
                    shifted.Root.Children[0] with
                    {
                        VirtualCollectionWindow = window with
                        {
                            Change = VirtualCollectionWindowChange.Replace,
                            FirstItemIndex = null,
                            TotalItemCount = null,
                        },
                    },
                ],
            },
        };
        Equal(0, ViewSnapshotValidator.Validate(unknownExtent).Count);
        var unboundedUnknownExtent = unknownExtent with
        {
            Root = unknownExtent.Root with
            {
                Children =
                [
                    unknownExtent.Root.Children[0] with
                    {
                        VirtualCollectionWindow = unknownExtent.Root.Children[0]
                            .VirtualCollectionWindow! with
                        {
                            FirstItemIndex = ProtocolConstants.MaximumVirtualCollectionItems,
                        },
                    },
                ],
            },
        };
        True(ViewSnapshotValidator.Validate(unboundedUnknownExtent).Any(error =>
                error.Code == "invalid_virtual_collection_first_index"),
            "An unknown-total window escaped the bounded logical item domain.");
        var unsafeGeneration = shifted with
        {
            Root = shifted.Root with
            {
                Children =
                [
                    shifted.Root.Children[0] with
                    {
                        VirtualCollectionWindow = window with
                        {
                            RequestGeneration =
                                ProtocolConstants.MaximumVirtualCollectionRequestGeneration + 1,
                        },
                    },
                ],
            },
        };
        True(ViewSnapshotValidator.Validate(unsafeGeneration).Any(error =>
                error.Code == "invalid_virtual_collection_generation"),
            "Managed admission exceeded the native JSON-safe generation bound.");
        var unknownDirectional = unknownExtent with
        {
            Root = unknownExtent.Root with
            {
                Children =
                [
                    unknownExtent.Root.Children[0] with
                    {
                        VirtualCollectionWindow = unknownExtent.Root.Children[0]
                            .VirtualCollectionWindow! with
                        {
                            Change = VirtualCollectionWindowChange.Append,
                        },
                    },
                ],
            },
        };
        True(ViewSnapshotValidator.Validate(unknownDirectional).Any(error =>
                error.Code == "virtual_collection_direction_requires_position"),
            "An unknown-position window claimed an unverifiable direction.");

        var retained = widget.Resource.Snapshot;
        var retainedScroll = shifted.Root.Children[0];
        fail = true;
        var failedRefresh = await widget.Resource.Refresh().Completion;
        Equal(WidgetOperationStatus.Failed, failedRefresh.Status);
        Equal(WidgetPagedResourceStatus.Error, widget.Resource.Snapshot.Status);
        Equal(4L, widget.Resource.Snapshot.WindowGeneration);
        Equal(retained.WindowChange, widget.Resource.Snapshot.WindowChange);
        Equal(96, widget.Resource.Snapshot.Items.Count);
        Equal(retained.Before, widget.Resource.Snapshot.Before);
        Equal(retained.After, widget.Resource.Snapshot.After);
        Equal(retained.Anchor, widget.Resource.Snapshot.Anchor);
        Equal(retained.FirstItemIndex, widget.Resource.Snapshot.FirstItemIndex);
        Equal(retained.TotalItemCount, widget.Resource.Snapshot.TotalItemCount);
        for (var index = 0; index < retained.Items.Count; index++)
            Equal(retained.Items[index], widget.Resource.Snapshot.Items[index]);
        var errorScroll = widget.Render().CreateSnapshot("virtual.fixture", 3).Root.Children[0];
        Equal<string?>(null, errorScroll.ScrollNearStartActionId);
        Equal<string?>(null, errorScroll.ScrollNearEndActionId);
        Equal(retainedScroll.CollectionAnchorKey, errorScroll.CollectionAnchorKey);
        Equal(retainedScroll.VirtualCollectionWindow! with
            {
                HasBefore = false,
                HasAfter = false,
            }, errorScroll.VirtualCollectionWindow);
        fail = false;
        var retry = widget.Resource.Retry();
        Equal(WidgetOperationAdmission.Started, retry.Admission);
        Equal(WidgetOperationStatus.Succeeded, (await retry.Completion).Status);
        Equal(WidgetPagedResourceStatus.Ready, widget.Resource.Snapshot.Status);
        Equal(5L, widget.Resource.Snapshot.WindowGeneration);
        var recoveredScroll = widget.Render().CreateSnapshot("virtual.fixture", 4).Root.Children[0];
        Equal("test.cursor.cursor.before", recoveredScroll.ScrollNearStartActionId);
        Equal("test.cursor.cursor.after", recoveredScroll.ScrollNearEndActionId);
        True(recoveredScroll.VirtualCollectionWindow is { HasBefore: true, HasAfter: true },
            "Explicit Retry did not restore non-Error virtual boundary availability.");
        foreach (var nextMutation in new[] { 1, 2, 3 })
        {
            mutation = nextMutation;
            await widget.Resource.Refresh().Completion;
            Equal(WidgetPagedResourceStatus.Ready, widget.Resource.Snapshot.Status);
            Equal(VirtualCollectionWindowChange.Replace,
                widget.Resource.Snapshot.WindowChange);
        }
        widget.Resource.Reset();
        Equal(0L, widget.Resource.Snapshot.WindowGeneration);
        mutation = 0;
        await widget.Resource.EnsureLoaded().Completion;
        Equal(9L, widget.Resource.Snapshot.WindowGeneration);
        Equal(VirtualCollectionWindowChange.Replace, widget.Resource.Snapshot.WindowChange);
        await StopAsync(widget);

        var unknownPositionWidget = await StartAsync(new()
        {
            PageSize = pageSize,
            MaximumRetainedItems = 64,
            PaginationThreshold = 2,
            Viewports = [Viewport() with { EstimatedItemExtent = 56 }],
            MapError = _ => WidgetResourceError.InvalidPage,
            LoadPage = (cursor, _, limit, _) =>
            {
                var start = cursor is null ? 0 : int.Parse(cursor.Value.Value.AsSpan(1));
                return ValueTask.FromResult(Page(start, limit, total));
            },
        });
        await unknownPositionWidget.Resource.EnsureLoaded().Completion;
        Equal(VirtualCollectionWindowChange.Replace,
            unknownPositionWidget.Resource.Snapshot.WindowChange);
        await unknownPositionWidget.Resource.Move(
            WidgetCursorDirection.After, "items.list").Completion;
        Equal(VirtualCollectionWindowChange.Replace,
            unknownPositionWidget.Resource.Snapshot.WindowChange);
        var unknownPositionSnapshot = unknownPositionWidget.Render()
            .CreateSnapshot("virtual.unknown-position", 1);
        Equal(VirtualCollectionWindowChange.Replace,
            unknownPositionSnapshot.Root.Children[0].VirtualCollectionWindow?.Change);
        Equal(0, ViewSnapshotValidator.Validate(unknownPositionSnapshot).Count);
        await StopAsync(unknownPositionWidget);
    }

    private static async Task PreservesAnchorAcrossAppendPrependAndRefresh()
    {
        var inserted = false;
        var removed = false;
        var widget = await StartAsync(Options(2_000, transform: items =>
        {
            if (removed) return items.Where(item => item.Id != "item.150").ToArray();
            if (inserted) return [new("item.inserted"), .. items.Take(99)];
            return items;
        }));
        await widget.Resource.EnsureLoaded().Completion;
        await widget.Resource.Move(WidgetCursorDirection.After, "items.list").Completion;
        widget.Resource.SelectAnchor(new("item.150"), invalidate: false);
        await widget.Resource.Move(WidgetCursorDirection.After, "items.list").Completion;
        Equal(new WidgetCollectionItemKey("item.150"), widget.Resource.Snapshot.Anchor);
        await widget.Resource.Move(WidgetCursorDirection.Before, "items.list").Completion;
        Equal("focus.item.99", widget.Resource.Snapshot.RequestedFocusId);

        inserted = true;
        await widget.Resource.Refresh().Completion;
        // A refresh whose current window no longer contains the old key uses
        // a deterministic nearest visible fallback, never an ordinal identity.
        Equal(new WidgetCollectionItemKey("item.150"), widget.Resource.Snapshot.Anchor);
        removed = true;
        inserted = false;
        await widget.Resource.Refresh().Completion;
        True(widget.Resource.Snapshot.Anchor is not null,
            "Deletion produced an unanchored non-empty collection.");
        await StopAsync(widget);
    }

    private static async Task HandlesEmptySparseFinalAndLastGoodError()
    {
        var empty = await StartAsync(new()
        {
            PageSize = 4,
            MaximumRetainedItems = 8,
            Viewports = [Viewport()],
            MapError = _ => WidgetResourceError.InvalidPage,
            LoadPage = (_, _, _, _) => ValueTask.FromResult(
                new WidgetCursorPage<Item>([], null, null)),
        });
        Equal(WidgetOperationStatus.Succeeded,
            (await empty.Resource.EnsureLoaded().Completion).Status);
        Equal(WidgetPagedResourceStatus.Ready, empty.Resource.Snapshot.Status);
        Equal(0, empty.Resource.Snapshot.Items.Count);
        True(!empty.Resource.Snapshot.HasBefore && !empty.Resource.Snapshot.HasAfter,
            "An empty final page exposed a transport boundary.");
        True(empty.Resource.Snapshot.Anchor is null,
            "An empty final page retained a phantom anchor.");
        await StopAsync(empty);

        var calls = 0;
        var sparse = await StartAsync(new()
        {
            PageSize = 4,
            MaximumRetainedItems = 8,
            Viewports = [Viewport()],
            MapError = _ => WidgetResourceError.InvalidPage,
            LoadPage = (_, _, _, _) => ++calls switch
            {
                1 => ValueTask.FromResult(new WidgetCursorPage<Item>(
                    [new("sparse.0")], null, new("sparse.next"))),
                2 => ValueTask.FromResult(new WidgetCursorPage<Item>(
                    [new("sparse.1"), new("sparse.2")], new("sparse.previous"), null)),
                _ => ValueTask.FromException<WidgetCursorPage<Item>>(
                    new InvalidOperationException("fixture unavailable")),
            },
        });
        await sparse.Resource.EnsureLoaded().Completion;
        Equal(WidgetOperationStatus.Succeeded,
            (await sparse.Resource.Move(WidgetCursorDirection.After, "items.list").Completion).Status);
        Equal(3, sparse.Resource.Snapshot.Items.Count);
        Equal("sparse.0", sparse.Resource.Snapshot.Items[0].Id);
        Equal("sparse.2", sparse.Resource.Snapshot.Items[^1].Id);
        True(!sparse.Resource.Snapshot.HasAfter,
            "A partial final page exposed another forward cursor.");
        var lastGood = sparse.Resource.Snapshot;

        Equal(WidgetOperationStatus.Failed,
            (await sparse.Resource.Refresh().Completion).Status);
        Equal(WidgetPagedResourceStatus.Error, sparse.Resource.Snapshot.Status);
        Equal(lastGood.Items.Count, sparse.Resource.Snapshot.Items.Count);
        for (var index = 0; index < lastGood.Items.Count; index++)
            Equal(lastGood.Items[index].Id, sparse.Resource.Snapshot.Items[index].Id);
        Equal(lastGood.Before, sparse.Resource.Snapshot.Before);
        Equal(lastGood.After, sparse.Resource.Snapshot.After);
        Equal(lastGood.Anchor, sparse.Resource.Snapshot.Anchor);
        Equal(WidgetResourceError.InvalidPage, sparse.Resource.Snapshot.Error);
        await StopAsync(sparse);
    }

    private static async Task RejectsLateDuplicateAndLoopResults()
    {
        var first = Signal();
        var release = Signal();
        var calls = 0;
        var widget = await StartAsync(Options(2_000, async: async (cursor, direction, limit, token) =>
        {
            calls++;
            if (calls == 1)
            {
                first.SetResult();
                await release.Task;
                return Page(0, limit, 2_000);
            }
            return Page(100, limit, 2_000);
        }));
        var stale = widget.Resource.EnsureLoaded();
        await first.Task;
        var current = widget.Resource.Refresh();
        release.SetResult();
        await Task.WhenAll(stale.Completion, current.Completion);
        Equal("item.100", widget.Resource.Snapshot.Items[0].Id);

        var duplicate = await StartAsync(new()
        {
            PageSize = 2,
            MaximumRetainedItems = 4,
            Viewports = [Viewport()],
            MapError = _ => WidgetResourceError.InvalidPage,
            LoadPage = (_, _, _, _) => ValueTask.FromResult(new WidgetCursorPage<Item>(
                [new("same"), new("same")], null, null)),
        });
        Equal(WidgetOperationStatus.Failed,
            (await duplicate.Resource.EnsureLoaded().Completion).Status);
        Equal(WidgetPagedResourceStatus.Error, duplicate.Resource.Snapshot.Status);
        await StopAsync(duplicate);

        var loop = await StartAsync(new()
        {
            PageSize = 2,
            MaximumRetainedItems = 4,
            Viewports = [Viewport()],
            MapError = _ => WidgetResourceError.InvalidPage,
            LoadPage = (cursor, _, _, _) => cursor is null
                ? ValueTask.FromResult(new WidgetCursorPage<Item>([new("a")], null, new("loop")))
                : ValueTask.FromResult(new WidgetCursorPage<Item>([new("b")], null, cursor)),
        });
        await loop.Resource.EnsureLoaded().Completion;
        Equal(WidgetOperationStatus.Failed,
            (await loop.Resource.Move(WidgetCursorDirection.After, "items.list").Completion).Status);
        Equal("a", loop.Resource.Snapshot.Items[0].Id);
        await StopAsync(loop);

        var cycle = await StartAsync(new()
        {
            PageSize = 2,
            MaximumRetainedItems = 4,
            Viewports = [Viewport()],
            MapError = _ => WidgetResourceError.InvalidPage,
            LoadPage = (cursor, _, _, _) => cursor?.Value switch
            {
                null => ValueTask.FromResult(CyclePage(0, "A")),
                "A" => ValueTask.FromResult(CyclePage(2, "B")),
                "B" => ValueTask.FromResult(CyclePage(4, "C")),
                "C" => ValueTask.FromResult(CyclePage(6, "A")),
                _ => throw new InvalidOperationException("Unexpected cursor."),
            },
        });
        await cycle.Resource.EnsureLoaded().Completion;
        await cycle.Resource.Move(WidgetCursorDirection.After, "items.list").Completion;
        await cycle.Resource.Move(WidgetCursorDirection.After, "items.list").Completion;
        Equal("cycle.2", cycle.Resource.Snapshot.Items[0].Id);
        Equal("cycle.5", cycle.Resource.Snapshot.Items[^1].Id);
        Equal(WidgetOperationStatus.Failed,
            (await cycle.Resource.Move(WidgetCursorDirection.After, "items.list").Completion).Status);
        Equal(WidgetPagedResourceStatus.Error, cycle.Resource.Snapshot.Status);
        Equal("cycle.2", cycle.Resource.Snapshot.Items[0].Id);
        Equal("cycle.5", cycle.Resource.Snapshot.Items[^1].Id);
        True(cycle.Resource.Snapshot.Items.All(item => item.Id != "cycle.6"),
            "A multi-hop cycle partially changed the retained window.");
        True(cycle.Resource.RetainedCursorCount <= WidgetCursorResource<Item>.MaximumCursorHistory,
            "Cycle detection escaped the cursor-history bound.");
        await StopAsync(cycle);
    }

    private static async Task DirectionChangeAllowsEvictedRefetch()
    {
        var widget = await StartAsync(new()
        {
            PageSize = 2,
            MaximumRetainedItems = 4,
            Viewports = [Viewport()],
            MapError = _ => WidgetResourceError.InvalidPage,
            LoadPage = (cursor, _, limit, _) =>
            {
                var start = cursor is null ? 0 : int.Parse(cursor.Value.Value.AsSpan(1));
                return ValueTask.FromResult(Page(start, limit, 8));
            },
        });
        await widget.Resource.EnsureLoaded().Completion;
        await widget.Resource.Move(WidgetCursorDirection.After, "items.list").Completion;
        await widget.Resource.Move(WidgetCursorDirection.After, "items.list").Completion;
        Equal("item.2", widget.Resource.Snapshot.Items[0].Id);

        Equal(WidgetOperationStatus.Succeeded,
            (await widget.Resource.Move(WidgetCursorDirection.Before, "items.list").Completion).Status);
        Equal("item.0", widget.Resource.Snapshot.Items[0].Id);
        Equal(WidgetOperationStatus.Succeeded,
            (await widget.Resource.Move(WidgetCursorDirection.After, "items.list").Completion).Status);
        Equal("item.2", widget.Resource.Snapshot.Items[0].Id);
        Equal("item.5", widget.Resource.Snapshot.Items[^1].Id);
        await StopAsync(widget);
    }

    private static async Task TraversalHistoryFailsClosedAndRefreshResetsIt()
    {
        var widget = await StartAsync(new()
        {
            PageSize = 1,
            MaximumRetainedItems = 2,
            Viewports = [Viewport()],
            MapError = _ => WidgetResourceError.InvalidPage,
            LoadPage = (cursor, _, limit, _) =>
            {
                var start = cursor is null ? 0 : int.Parse(cursor.Value.Value.AsSpan(1));
                return ValueTask.FromResult(Page(start, limit, 1_000));
            },
        });
        await widget.Resource.EnsureLoaded().Completion;
        for (var index = 1;
             index < WidgetCursorResource<Item>.MaximumCursorHistory;
             index++)
            Equal(WidgetOperationStatus.Succeeded,
                (await widget.Resource.Move(
                    WidgetCursorDirection.After, "items.list").Completion).Status);

        Equal(WidgetCursorResource<Item>.MaximumCursorHistory,
            widget.Resource.RetainedCursorCount);
        var lastGood = widget.Resource.Snapshot.Items.Select(item => item.Id).ToArray();
        Equal(WidgetOperationStatus.Failed,
            (await widget.Resource.Move(
                WidgetCursorDirection.After, "items.list").Completion).Status);
        Equal(lastGood.Length, widget.Resource.Snapshot.Items.Count);
        for (var index = 0; index < lastGood.Length; index++)
            Equal(lastGood[index], widget.Resource.Snapshot.Items[index].Id);

        Equal(WidgetOperationStatus.Succeeded,
            (await widget.Resource.Refresh().Completion).Status);
        Equal(0, widget.Resource.RetainedCursorCount);
        Equal(WidgetOperationStatus.Succeeded,
            (await widget.Resource.Move(
                WidgetCursorDirection.After, "items.list").Completion).Status);
        True(widget.Resource.RetainedCursorCount <=
                WidgetCursorResource<Item>.MaximumCursorHistory,
            "A refreshed traversal escaped the cursor-history bound.");
        await StopAsync(widget);
    }

    private static WidgetCursorPage<Item> CyclePage(int start, string after) => new(
        [new($"cycle.{start}"), new($"cycle.{start + 1}")], null, new(after));

    private static void ContractIsVersionedOpaqueAndBounded()
    {
        var artwork = new WidgetArtworkHandle("library.art.42");
        var view = new WidgetView(UI.Stack("root",
            UI.Button("Play", "play", "header.play"),
            UI.VerticalScroll("items.list",
                UI.ResponsiveGrid("items.grid", 160, 4,
                    UI.Artwork(artwork, "art", "Artwork")
                        .CollectionItem(new("item.42")))) with
            {
                CollectionAnchorKey = "item.42",
            }));
        var snapshot = view.CreateSnapshot("cursor.contract", 1);
        Equal(ProtocolConstants.TrustedEncodedArtworkVersion, snapshot.ProtocolVersion);
        var image = snapshot.Root.Children[1].Children[0].Children[0];
        Equal("library.art.42", image.ArtworkHandle);
        True(image.ImageSource is null, "An opaque artwork handle became fetch authority.");
        Equal("header.play", snapshot.Root.Children[0].Id);
        var legacy = snapshot with { ProtocolVersion = ProtocolConstants.FocusPersistenceVersion };
        True(ViewSnapshotValidator.Validate(legacy).Any(error => error.Code == "feature_requires_version"),
            "Cursor collection fields were accepted by protocol v13.");
        Throws<ArgumentException>(() => new WidgetArtworkHandle("https://bad/path"));
        Throws<ArgumentOutOfRangeException>(() => new FixtureWidget(new()
        {
            PageSize = 100,
            MaximumRetainedItems = 101,
            Viewports = [Viewport()],
            MapError = _ => WidgetResourceError.Unexpected,
            LoadPage = (_, _, _, _) => ValueTask.FromResult(Page(0, 100, 2_000)),
        }));
    }

    private static WidgetCursorResourceOptions<Item> Options(int total,
        Func<IReadOnlyList<Item>, IReadOnlyList<Item>>? transform = null,
        Func<WidgetCollectionCursor?, WidgetCursorDirection?, int, CancellationToken,
            ValueTask<WidgetCursorPage<Item>>>? @async = null,
        int pageSize = 100,
        int maximumRetainedItems = 200) => new()
    {
        PageSize = pageSize,
        MaximumRetainedItems = maximumRetainedItems,
        Viewports = [Viewport()],
        MapError = _ => WidgetResourceError.InvalidPage,
        LoadPage = @async ?? DefaultLoader(total, transform),
    };

    private static Func<WidgetCollectionCursor?, WidgetCursorDirection?, int,
        CancellationToken, ValueTask<WidgetCursorPage<Item>>> DefaultLoader(
        int total, Func<IReadOnlyList<Item>, IReadOnlyList<Item>>? transform) =>
        (cursor, _, limit, _) =>
        {
            var start = cursor is null ? 0 : int.Parse(cursor.Value.Value.AsSpan(1));
            var page = Page(start, limit, total);
            return ValueTask.FromResult(page with
            {
                Items = transform?.Invoke(page.Items) ?? page.Items,
            });
        };

    private static WidgetCursorPage<Item> Page(int start, int limit, int total)
    {
        var count = Math.Min(limit, Math.Max(0, total - start));
        var items = Enumerable.Range(start, count).Select(i => new Item($"item.{i}")).ToArray();
        return new(items,
            start > 0 ? new WidgetCollectionCursor($"c{Math.Max(0, start - limit)}") : null,
            start + count < total ? new WidgetCollectionCursor($"c{start + count}") : null);
    }

    private static WidgetCursorPage<Item> VirtualPage(int start, int limit, int total) =>
        Page(start, limit, total) with
        {
            FirstItemIndex = start,
            TotalItemCount = total,
        };

    private static WidgetCursorViewport<Item> Viewport() => new(
        "items.list", item => new(item.Id), item => "focus." + item.Id, "empty");

    private static async Task<FixtureWidget> StartAsync(WidgetCursorResourceOptions<Item> options)
    {
        var widget = new FixtureWidget(options);
        await WidgetTestHost.InitializeAsync(widget);
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Visible);
        return widget;
    }
    private static async Task StopAsync(FixtureWidget widget)
    {
        await WidgetTestHost.SetLifecycleStateAsync(widget, WidgetLifecycleState.Background);
        await WidgetTestHost.DestroyAsync(widget);
    }
    private static IEnumerable<ViewNode> Flatten(ViewNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in Flatten(child)) yield return descendant;
    }
    private static TaskCompletionSource Signal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed record Item(string Id);
    private sealed class FixtureWidget : Widget
    {
        public FixtureWidget(WidgetCursorResourceOptions<Item> options) =>
            Resource = CreateCursorResource("test.cursor", options);
        public WidgetCursorResource<Item> Resource { get; }
        public override WidgetView Render()
        {
            var capture = Resource.Capture();
            var value = capture.Snapshot;
            var items = value.Items.Select(item => capture.PresentItem(item,
                UI.Button(item.Id, "select", "focus." + item.Id))).ToArray();
            var scroll = capture.Present(UI.VerticalScroll("items.list", items));
            return new(UI.Stack("root", scroll), value.RequestedFocusId);
        }
    }
}
