using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetRuntime;
using WidgetRail.WidgetSdk;

internal static class BridgeIndexedLeaseRegistryScenarios
{
    internal static async Task DataLifetimeAndInputAuthorityAreSeparate()
    {
        await using var fixture = await Fixture.Start();
        var origin = await fixture.Snapshot();
        var request = fixture.Request("first");
        using var publication = await fixture.Registry.AcquireIndexedRangeAsync(request, CancellationToken.None);
        var lease = publication.Value;
        var fake = fixture.Client.Leases.Single();
        fixture.Client.Caption = "Unrelated changed";
        var current = await fixture.Snapshot();
        var input = fixture.Input(lease, origin.Sequence, ControllerButton.A);
        var admitted = await fixture.Registry.AdmitIndexedInputAsync(input, CancellationToken.None);
        Check(admitted == WidgetOperationAdmission.Enqueued, "Unrelated snapshot rejected input.");
        Check(fake.Contexts.Single().SnapshotSequence == current.Sequence, "Input was not rebased after origin comparison.");
        fixture.Client.Shortcut = "changed";
        _ = await fixture.Snapshot();
        await Reject(() => fixture.Registry.AdmitIndexedInputAsync(fixture.Input(lease, origin.Sequence, ControllerButton.X), CancellationToken.None));
        Check(fake.Contexts.Count == 1, "Changed shortcut reached worker.");
        fixture.Client.ActiveOtherScope = true;
        _ = await fixture.Snapshot();
        Check(fake.Disposed == 0, "Changing active scope retired data lease.");
        await Reject(() => fixture.Registry.AdmitIndexedInputAsync(input, CancellationToken.None));
        fixture.Client.ActiveOtherScope = false;
        var restored = await fixture.Snapshot();
        Check(await fixture.Registry.AdmitIndexedInputAsync(fixture.Input(lease, restored.Sequence, ControllerButton.A), CancellationToken.None) == WidgetOperationAdmission.Enqueued, "Restored parent scope lost lease.");
        fixture.Client.Source = fixture.Client.Source with
        {
            QueryGeneration = 2
        };
        _ = await fixture.Snapshot();
        await fake.Released.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Check(fake.Disposed == 1, "Query change must release exact data owner.");
        await Reject(() => fixture.Registry.AdmitIndexedInputAsync(input, CancellationToken.None));
    }

    internal static async Task DuplicateAndCancellationPreserveOwnership()
    {
        await using var fixture = await Fixture.Start();
        _ = await fixture.Snapshot();
        var request = fixture.Request("kept");
        using var first = await fixture.Registry.AcquireIndexedRangeAsync(request, CancellationToken.None);
        var fake = fixture.Client.Leases.Single();
        await Reject(() => fixture.Registry.AcquireIndexedRangeAsync(request, CancellationToken.None));
        Check(fake.Disposed == 0, "Duplicate acquisition disposed original lease.");
        Check(await fixture.Registry.CancelIndexedRangeAsync(request), "Post-response cancellation missed delivered lease.");
        await fake.Released.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Check(fake.Disposed == 1, "Post-response cancellation leaked lease.");
        using var cancelled = new CancellationTokenSource();
        fixture.Client.AfterAcquire = () => cancelled.Cancel();
        try
        {
            using var invalid = await fixture.Registry.AcquireIndexedRangeAsync(fixture.Request("cancel-after-return"), cancelled.Token);
            throw new InvalidOperationException("Cancelled acquisition succeeded.");
        }
        catch (OperationCanceledException) { }
        Check(fixture.Client.Leases.Last().Disposed == 1, "Successful runtime acquisition leaked during caller cancellation.");
    }

    internal static async Task ArtworkReleaseDrainsBeforeRetirement()
    {
        await using var fixture = await Fixture.Start();
        _ = await fixture.Snapshot();
        using var publication = await fixture.Registry.AcquireIndexedRangeAsync(fixture.Request("art"), CancellationToken.None);
        var lease = publication.Value;
        var fake = fixture.Client.Leases.Single();
        var request = new BridgeIndexedArtworkRequest(lease.WidgetId, lease.InstanceId, lease.RuntimeGeneration, lease.PresentationGeneration,
            new(lease.Lease.LeaseId, "item.0"), "artwork", "art-demand");
        var artwork = fixture.Registry.ResolveIndexedArtworkAsync(request, CancellationToken.None);
        await fake.ArtworkStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Check(!await fixture.Registry.CancelIndexedArtworkAsync(request with
        {
            DemandId = "forged"
        }), "Forged artwork cancellation accepted.");
        Check(await fixture.Registry.CancelIndexedArtworkAsync(request), "Exact artwork cancel missing.");
        try
        {
            await artwork;
            throw new InvalidOperationException("Artwork cancellation lost.");
        }
        catch (BridgeProtocolException) { }
        Check(fake.Disposed == 0, "Artwork cancel released data lease.");
        Check(await fixture.Registry.ResolveIndexedArtworkAsync(request with { ArtworkHandle = "undeclared" }, CancellationToken.None) is null, "Undeclared artwork forwarded.");
        var active = fixture.Registry.ResolveIndexedArtworkAsync(request with
        {
            DemandId = "release-active"
        }, CancellationToken.None);
        Check(fake.ArtworkActive == 1, "Second artwork read did not start.");
        Check(await fixture.Registry.ReleaseIndexedLeaseAsync(fixture.Release(lease)), "Lease release missing.");
        try
        {
            await active;
            throw new InvalidOperationException("Released artwork returned content.");
        }
        catch (BridgeProtocolException) { }
        Check(fake.Disposed == 1 && fake.ArtworkActive == 0, "Lease release did not drain artwork.");
    }

    internal static async Task BoundedOwnersAndReplacementRelease()
    {
        await using var fixture = await Fixture.Start();
        _ = await fixture.Snapshot();
        var leases = new List<BridgeIndexedLeaseResponse>();
        for (var i = 0; i < 32; i++)
        {
            using var value = await fixture.Registry.AcquireIndexedRangeAsync(fixture.Request("d" + i), CancellationToken.None);
            leases.Add(value.Value);
        }
        await Reject(() => fixture.Registry.AcquireIndexedRangeAsync(fixture.Request("full"), CancellationToken.None));
        fixture.Registry.ApplyCatalog(new([fixture.Configured with { WorkerFingerprint = new('b', 64), CatalogFingerprint = new('b', 64) }]), 1);
        await fixture.Client.Disposal.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(fixture.Client.Leases.All(lease => lease.Disposed == 1), "Worker replacement leaked owned ranges.");
        Check(fixture.Client.DisposedWhileLeaseActive == false, "Worker disposed before semantic releases.");
        Check(!await fixture.Registry.ReleaseIndexedLeaseAsync(fixture.Release(leases[0])), "Retired lease should be absent.");
    }

    internal static async Task RealWorkerLeaseRoundTrip()
    {
        var configured = new ConfiguredWidget
        {
            Id = "indexed", Name = "Indexed", InstanceId = "indexed-real.instance", PackageId = "dev.indexed",
            PublisherId = "dev", WorkerExecutable = Environment.ProcessPath!,
            WorkerFingerprint = new('a', 64), CatalogFingerprint = new('a', 64),
        };
        await using var registry = new BridgeClientRegistry(new([configured]), new(),
            (value, reserve) => new WidgetProcessBridgeClient(new(new()
            {
                ExecutablePath = Environment.ProcessPath!, WidgetInstanceId = value.InstanceId,
                IsolationPolicy = WidgetWorkerIsolationPolicy.HostTrustedJobOnly, ProcessLeaseFactory = reserve,
                ConnectTimeout = TimeSpan.FromSeconds(3), RequestTimeout = TimeSpan.FromSeconds(2),
            })), (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask);
        using (var visible = await registry.SetLifecycleAsync(configured.Id, WidgetLifecycleState.Interactive,
            CancellationToken.None, CancellationToken.None)) { }
        ViewSnapshot parent;
        using (var publication = await registry.GetSnapshotAsync(configured.Id, CancellationToken.None, CancellationToken.None))
            parent = publication.Value.Snapshot;
        using (var release = await registry.AdmitActionAsync(configured.Id, new("release", "release"),
            CancellationToken.None, CancellationToken.None)) { }
        var descriptor = configured.PublicDescriptor();
        var request = new BridgeIndexedRangeRequest(descriptor.Id, descriptor.InstanceId,
            descriptor.RuntimeGeneration, descriptor.PresentationGeneration,
            new("items", parent.Root.Children.Single(node => node.Id == "items").IndexedCollection!, 0, 1, "real"));
        BridgeIndexedLeaseResponse acquired;
        using (var publication = await registry.AcquireIndexedRangeAsync(request, CancellationToken.None))
            acquired = publication.Value;
        var admission = await registry.AdmitIndexedInputAsync(new(acquired.WidgetId, acquired.InstanceId,
            acquired.RuntimeGeneration, acquired.PresentationGeneration,
            new(new(acquired.Lease.LeaseId, acquired.Lease.Range.Items[0].Key), ControllerButton.A),
            new(acquired.Lease.Range.ScopeId, parent.Sequence)), CancellationToken.None);
        Check(admission == WidgetOperationAdmission.Enqueued, "Real worker did not admit indexed item action.");
        Check(await registry.ReleaseIndexedLeaseAsync(new(acquired.WidgetId, acquired.InstanceId,
            acquired.RuntimeGeneration, acquired.PresentationGeneration, acquired.Lease.LeaseId)), "Real worker lease release failed.");
    }

    private static void Check(bool value, string message)
    {
        if (!value)
            throw new InvalidOperationException(message);
    }
    private static async Task Reject(Func<Task> action)
    {
        try
        {
            await action().WaitAsync(TimeSpan.FromSeconds(3));
            throw new InvalidOperationException("Expected stale request rejection.");
        }
        catch (BridgeProtocolException) { }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        internal readonly ConfiguredWidget Configured = new()
        {
            Id = "indexed",
            Name = "Indexed",
            InstanceId = "indexed.instance",
            PackageId = "dev.indexed",
            PublisherId = "dev",
            WorkerExecutable = Environment.ProcessPath!,
            WorkerFingerprint = new('a', 64),
            CatalogFingerprint = new('a', 64)
        };
        internal readonly Client Client = new();
        internal readonly BridgeClientRegistry Registry;
        private Fixture()
        {
            Registry = new(new([Configured]), new(), (_, _) => Client, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask);
        }
        internal static async Task<Fixture> Start()
        {
            var value = new Fixture();
            using var lifecycle = await value.Registry.SetLifecycleAsync(value.Configured.Id, WidgetLifecycleState.Interactive, CancellationToken.None, CancellationToken.None);
            return value;
        }
        internal async Task<ViewSnapshot> Snapshot()
        {
            using var value = await Registry.GetSnapshotAsync(Configured.Id, CancellationToken.None, CancellationToken.None);
            return value.Value.Snapshot;
        }
        internal BridgeIndexedRangeRequest Request(string demand)
        {
            var d = Configured.PublicDescriptor();
            return new(d.Id, d.InstanceId, d.RuntimeGeneration, d.PresentationGeneration, new("items", Client.Source, 0, 1, demand));
        }
        internal BridgeIndexedInputRequest Input(BridgeIndexedLeaseResponse lease, long sequence, ControllerButton button) => new(lease.WidgetId, lease.InstanceId, lease.RuntimeGeneration,
            lease.PresentationGeneration, new(new(lease.Lease.LeaseId, "item.0"), button), new("root", sequence));
        internal BridgeIndexedLeaseRequest Release(BridgeIndexedLeaseResponse lease) => new(lease.WidgetId, lease.InstanceId, lease.RuntimeGeneration, lease.PresentationGeneration, lease.Lease.LeaseId);
        public ValueTask DisposeAsync() => Registry.DisposeAsync();
    }
    private sealed class Client : IBridgeWidgetClient
    {
        public event EventHandler<long>? Invalidated { add { } remove { } }
        public event EventHandler<WidgetActionFailure>? ActionFailed { add { } remove { } }
        public event EventHandler<WidgetFailure>? Failed { add { } remove { } }
        public event EventHandler<WidgetProcessLifetimeDiagnostic>? LifetimeChanged { add { } remove { } }
        public bool IsRunning
        {
            get; private set;
        }
        public int Starts
        {
            get; private set;
        }
        private long sequence;
        private ViewSnapshot? lastSnapshot;
        internal IndexedCollectionDescriptor Source = new("source", 1, 0, 100);
        internal string Shortcut = "shortcut";
        internal string Caption = "caption";
        internal bool ActiveOtherScope;
        internal Action? AfterAcquire;
        internal readonly List<FakeLease> Leases = [];
        internal readonly TaskCompletionSource Disposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool DisposedWhileLeaseActive;
        public Task<ViewSnapshot> GetSnapshotAsync(CancellationToken token)
        {
            var snapshot = new ViewSnapshot
            {
                WidgetInstanceId = "indexed.instance",
                Sequence = ++sequence,
                ActiveInputScopeId = ActiveOtherScope ? "other" : "root",
                InitialFocusId = ActiveOtherScope ? "other.button" : "button",
                Root = new()
                {
                    Id = "root",
                    Kind = ViewNodeKind.Stack,
                    InputScopeId = "root",
                    Shortcuts = [new(ControllerButton.X, Shortcut)],
                    Children =
                    [
                        new() { Id = "button", Kind = ViewNodeKind.Button, Text = Caption, ActionId = "action" },
                        new()
                        {
                            Id = "items", Kind = ViewNodeKind.IndexedCollection, IndexedCollection = Source,
                            ScrollAxis = ScrollAxis.Vertical, AccessibilityLabel = "Items",
                            CollectionLayout = new() { Kind = CollectionLayoutKind.List, EstimatedItemExtent = 64 },
                        },
                        new()
                        {
                            Id = "other", Kind = ViewNodeKind.Stack, InputScopeId = "other",
                            Children = [new() { Id = "other.button", Kind = ViewNodeKind.Button, Text = "Other", ActionId = "other.action" }],
                        },
                    ]
                },
            };
            if (ActiveOtherScope)
                snapshot = snapshot with
                {
                    Root = new()
                    {
                        Id = "modal",
                        Kind = ViewNodeKind.ModalLayer,
                        Children =
                        [
                            snapshot.Root with { Children = snapshot.Root.Children.Take(2).ToArray() },
                            snapshot.Root.Children[2],
                        ]
                    }
                };
            lastSnapshot = snapshot;
            return Task.FromResult(snapshot);
        }
        public Task SetLifecycleStateAsync(WidgetLifecycleState state, CancellationToken token)
        {
            if (!IsRunning)
            {
                IsRunning = true;
                Starts++;
            }
            return Task.CompletedTask;
        }
        public Task<IBridgeIndexedLease> AcquireIndexedRangeAsync(IndexedCollectionRangeRequest request, int ordinal, CancellationToken token)
        {
            var item = new IndexedCollectionItem("item.0", new()
            {
                Id = "item.0", Kind = ViewNodeKind.ActionSurface,
                ActionSurfaceOrientation = ActionSurfaceOrientation.Vertical,
                ActionId = "item.action", AccessibilityLabel = "Item", CollectionItemKey = "item.0",
                Children =
                [
                    new()
                    {
                        Id = "item.image", Kind = ViewNodeKind.Image, ArtworkHandle = "artwork",
                        ImageFit = ImageFit.Cover, AccessibilityLabel = "Cover",
                    },
                ],
            });
            var range = new IndexedCollectionRange("indexed.instance", request.CollectionId, request.Source,
                "root", request.StartIndex, request.DemandId, [item]);
            try { IndexedCollectionContract.ValidateRange(lastSnapshot!, request, range); }
            catch (ProtocolValidationException exception)
            {
                throw new InvalidOperationException(string.Join("; ", exception.Errors.Select(error => $"{error.Path}: {error.Code}: {error.Message}")), exception);
            }
            var lease = new FakeLease(new(Guid.NewGuid().ToString("N"), range));
            Leases.Add(lease);
            AfterAcquire?.Invoke();
            return Task.FromResult<IBridgeIndexedLease>(lease);
        }
        public Task<WidgetOperationAdmission> AdmitActionAsync(WidgetActionEvent action, CancellationToken token) => Task.FromResult(WidgetOperationAdmission.Enqueued);
        public Task SendEmbeddedMediaPlaybackEventAsync(EmbeddedMediaPlaybackEvent playback, CancellationToken token) => Task.CompletedTask;
        public Task<bool> SendControllerInputAsync(ControllerInputEvent input, WidgetDashboardGestureAuthority? authority, CancellationToken token) => Task.FromResult(false);
        public Task UnloadAsync(CancellationToken token)
        {
            IsRunning = false;
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            DisposedWhileLeaseActive = Leases.Any(lease => lease.Disposed == 0);
            IsRunning = false;
            Disposal.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
    private sealed class FakeLease(IndexedCollectionLease lease) : IBridgeIndexedLease
    {
        public IndexedCollectionLease Lease { get; } = lease;
        internal int Disposed;
        internal bool BlockArtwork = true;
        internal int ArtworkActive;
        internal readonly TaskCompletionSource Released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ArtworkStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<IndexedCollectionInputContext> Contexts = [];
        public Task<WidgetOperationAdmission?> AdmitInputAsync(IndexedCollectionInputRequest input, IndexedCollectionInputContext correlation, CancellationToken token)
        {
            Contexts.Add(correlation);
            return Task.FromResult<WidgetOperationAdmission?>(WidgetOperationAdmission.Enqueued);
        }
        public async Task<WidgetEncodedArtwork?> ResolveArtworkAsync(string key, string handle, CancellationToken token)
        {
            ArtworkActive++;
            ArtworkStarted.TrySetResult();
            try
            {
                if (BlockArtwork)
                    await Task.Delay(Timeout.Infinite, token);
                return null;
            }
            finally { ArtworkActive--; }
        }
        public ValueTask DisposeAsync()
        {
            Disposed++;
            Released.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}
