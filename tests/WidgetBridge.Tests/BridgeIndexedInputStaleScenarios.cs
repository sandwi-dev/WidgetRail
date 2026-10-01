using WidgetRail.WidgetBridge;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static partial class BridgeIndexedLeaseRegistryScenarios
{
    internal static async Task StaleInputIsRecoverableButMalformedInputIsNot()
    {
        await using var fixture = await Fixture.Start();
        var snapshot = await fixture.Snapshot();
        using var publication = await fixture.Registry.AcquireIndexedRangeAsync(fixture.Request("stale-input"), default);
        var lease = publication.Value;
        var input = fixture.Input(lease, snapshot.Sequence, ControllerButton.A);
        var runtime = fixture.Client.Leases.Single();

        await RejectStaleInput(() => fixture.Registry.AdmitIndexedInputAsync(input with
        {
            Context = input.Context with { SnapshotSequence = snapshot.Sequence + 1000 },
        }, default));
        await RejectInputFault(() => fixture.Registry.AdmitIndexedInputAsync(input with
        {
            Input = input.Input with { Item = input.Input.Item with { ItemKey = "undeclared" } },
        }, default));
        await RejectInputFault(() => fixture.Registry.AdmitIndexedInputAsync(input with
        {
            Context = input.Context with { InputScopeId = "foreign.scope" },
        }, default));
        await RejectInputFault(() => fixture.Registry.AdmitIndexedInputAsync(input with
        {
            Input = input.Input with { Button = (ControllerButton)int.MaxValue },
        }, default));
        await RejectInputFault(() => fixture.Registry.AdmitIndexedInputAsync(input with
        {
            Input = input.Input with { ContextActionId = "action", ContextActionOwnerId = "item.0", Phase = ControllerEventPhase.Released },
        }, default));

        // A worker fault must not be relabeled as superseded input merely because
        // its exception type can also be used by owner-path resolution.
        runtime.InputFailure = new InvalidOperationException("worker failed");
        await RejectInputFault(() => fixture.Registry.AdmitIndexedInputAsync(input, default));
        runtime.InputFailure = null;
        Check(runtime.Contexts.Count == 0, "Invalid inputs reached the worker.");
        Check(await fixture.Registry.AdmitIndexedInputAsync(input, default) == WidgetOperationAdmission.Enqueued,
            "Invalid requests damaged the retained lease.");

        // Advance beyond the retained origin window without replacing the query.
        // Its data lease remains valid, but the broker cannot prove old input.
        for (var update = 0; update < 20; update++) snapshot = await fixture.Snapshot();
        await RejectStaleInput(() => fixture.Registry.AdmitIndexedInputAsync(input, default));
        input = input with { Context = input.Context with { SnapshotSequence = snapshot.Sequence } };
        Check(await fixture.Registry.AdmitIndexedInputAsync(input, default) == WidgetOperationAdmission.Enqueued,
            "Origin expiration incorrectly retired the data lease.");

        Check(await fixture.Registry.ReleaseIndexedLeaseAsync(fixture.Release(lease)), "Lease was not released.");
        await RejectStaleInput(() => fixture.Registry.AdmitIndexedInputAsync(input, default));
        Check(runtime.Contexts.Count == 2, "Retired input reached the worker.");
    }

    private static async Task RejectStaleInput(Func<Task> action)
    {
        try { await action().WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (BridgeStaleIndexedInputAuthorityException exception)
        {
            Check(WidgetBridgeServer.CreateRequestFailure(exception).Code == "stale_indexed_input_authority",
                "Expected indexed supersession lost its recoverable wire code.");
            return;
        }
        throw new InvalidOperationException("Expected typed indexed input supersession.");
    }

    private static async Task RejectInputFault(Func<Task> action)
    {
        try { await action().WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (Exception exception) when (exception is BridgeProtocolException or ArgumentException or InvalidOperationException)
        {
            Check(WidgetBridgeServer.CreateRequestFailure(exception).Code == "request_failed",
                "Malformed input or worker failure was treated as expected supersession.");
            return;
        }
        throw new InvalidOperationException("Expected protocol or worker rejection.");
    }
}
