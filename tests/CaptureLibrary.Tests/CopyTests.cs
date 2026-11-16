using WidgetRail.PlatformBroker;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class CopyTests
{
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    private static CaptureLibraryMutationReference Reference(CaptureLibraryItem item) => new(item.Id, item.Revision, item.MetadataRevision);
    internal static async Task Files()
    {
        using var f = new Fixture();
        var a = await f.Store.SaveAsync(f.Source, default);
        var b = await f.Store.SaveAsync(f.Source with { Attachment = f.Source.Attachment with { SourceApplication = new("Other", "Other") } }, default);
        var destination = Directory.CreateDirectory(Path.Combine(f.Root, "copies")).FullName;
        var before = await f.Store.QueryAsync(new(new()), default);
        var copied = await f.Store.CopyAsync(new([Reference(a), Reference(b)]), destination, default);
        Check(copied.Results.All(item => item.ErrorCode is null), "Copy failed");
        Check(Directory.GetFiles(destination).Length == 2 && Directory.GetFiles(destination).All(path => File.ReadAllBytes(path).SequenceEqual(f.Bytes)), "Copy changed original bytes");
        Check((await f.Store.QueryAsync(new(new()), default)).Revision == before.Revision, "Export mutated the library index");
        var aName = Path.GetFileName(f.MediaPath(a.Id));
        File.WriteAllText(Path.Combine(destination, aName), "Existing user file");
        var c = await f.Store.SaveAsync(f.Source with { Attachment = f.Source.Attachment with { SourceApplication = new("Third", "Third") } }, default);
        var mixed = await f.Store.CopyAsync(new([Reference(a), Reference(c)]), destination, default);
        Check(mixed.Results[0].ErrorCode == "capture_library_copy_exists" && mixed.Results[1].ErrorCode is null, "Collision should not prevent independent copies");
        Check(File.ReadAllText(Path.Combine(destination, aName)) == "Existing user file", "Existing file overwritten");
        Check(File.ReadAllBytes(f.MediaPath(a.Id)).SequenceEqual(f.Bytes), "Original changed");
        var changed = await f.Store.UpdateMetadataAsync(new(b.Id, b.MetadataRevision, "Updated", true), default);
        var stale = await f.Store.CopyAsync(new([Reference(b)]), destination, default);
        Check(stale.Results[0].ErrorCode == "capture_library_metadata_conflict", "Stale selected metadata accepted");
        using var stopped = new CancellationTokenSource(); stopped.Cancel();
        var cancelled = await f.Store.CopyAsync(new([Reference(changed)]), destination, stopped.Token);
        Check(cancelled.Results[0].ErrorCode == "cancelled", "Cancelled copy was not reported");
        Check(!Directory.GetFiles(destination, ".widgetrail-copy-*").Any(), "Temporary copies leaked");
        await Reject(() => f.Store.CopyAsync(new([Reference(a), Reference(a)]), destination, default), "invalid_payload");
    }

    internal static async Task MidCopyCancellation()
    {
        using var f = new Fixture();
        // A large local synthetic file gives cancellation an actual async copy to interrupt.
        var path = Path.Combine(f.Root, "large.png");
        var bytes = new byte[64 * 1024 * 1024]; f.Bytes.CopyTo(bytes, 0); bytes[^1] = 42;
        await File.WriteAllBytesAsync(path, bytes);
        var source = f.Source with { Path = path, Attachment = f.Source.Attachment with { ByteLength = bytes.Length } };
        var item = await f.Store.SaveAsync(source, default);
        var destination = Directory.CreateDirectory(Path.Combine(f.Root, "interrupted")).FullName;
        using var cancel = new CancellationTokenSource();
        using var watcher = new FileSystemWatcher(destination) { Filter = ".widgetrail-copy-*.tmp", EnableRaisingEvents = true };
        watcher.Created += (_, _) => cancel.Cancel();
        var result = await f.Store.CopyAsync(new([Reference(item)]), destination, cancel.Token);
        Check(result.Results[0].ErrorCode == "cancelled", "Copy cancellation did not interrupt staged output");
        Check(Directory.GetFiles(destination).Length == 0, "Cancelled output or temporary file remains");
        Check(new FileInfo(f.MediaPath(item.Id)).Length == bytes.Length, "Cancellation changed original");
    }

    internal static async Task PickerAndSdk()
    {
        using var f = new Fixture();
        var item = await f.Store.SaveAsync(f.Source, default);
        using var cancel = new CancellationTokenSource();
        var first = CaptureLibraryCopyRequests.ChooseAsync(Fixture.Identity, 1, cancel.Token);
        var retired = CaptureLibraryCopyRequests.Pending!;
        cancel.Cancel();
        try { await first; throw new Exception("Cancelled picker completed"); } catch (OperationCanceledException) { }
        var next = CaptureLibraryCopyRequests.ChooseAsync(Fixture.Identity, 1, default);
        var current = CaptureLibraryCopyRequests.Pending!;
        CaptureLibraryCopyRequests.Complete(retired.Id, f.Exports);
        Check(!next.IsCompleted, "Late picker result completed another request");
        CaptureLibraryCopyRequests.Complete(current.Id, null);
        Check(await next is null && CaptureLibraryCopyRequests.Pending is null, "Picker cancellation did not release ownership");
        var calls = 0;
        var services = new WidgetTestHostServicesBuilder().WithHandler(WidgetCaptureLibraryCapabilities.Copy, (request, _) =>
        { calls++; return ValueTask.FromResult(new CaptureLibraryBatchResult(request.Items.Select(reference => new CaptureLibraryMutationResult(reference.Id)).ToArray())); }).Build();
        await services.CaptureLibrary.CopyAsync([Reference(item)]);
        Check(calls == 1, "SDK did not invoke the copy capability");
        try { await services.CaptureLibrary.CopyAsync([Reference(item), Reference(item)]); throw new Exception("SDK accepted duplicate selection"); }
        catch (ArgumentException) { }
        Check(calls == 1, "Malformed selection reached host");
        var maximum = Enumerable.Range(1, CaptureLibraryLimits.MaximumCopyItems)
            .Select(index => new CaptureLibraryMutationReference(index.ToString("x32"), long.MaxValue, long.MaxValue)).ToArray();
        Check((await services.CaptureLibrary.CopyAsync(maximum)).Results.Count == 256, "Maximum selected batch was not admitted in one request");
        Check(CaptureLibraryStore.ValidateCopy(new(maximum)).Length == 256, "Broker maximum copy batch differs from SDK");
        var wire = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new BrokerRequestEnvelope(BrokerJson.ProtocolVersion, 1, Fixture.Identity,
            PlatformCapabilities.CaptureLibraryManageV1, "capture-library.copy", BrokerJson.ToElement(new CaptureLibraryCopyRequest(maximum))), BrokerJson.StrictOptions);
        Check(wire.Length <= BrokerJson.MaximumRequestBytes && BrokerJson.ParseRequest(wire).Operation == "capture-library.copy", "Maximum batch exceeds transport bounds");
        try { await services.CaptureLibrary.CopyAsync(maximum.Append(new("ffffffffffffffffffffffffffffffff", 0, 0)).ToArray()); throw new Exception("Oversized batch accepted"); }
        catch (ArgumentException) { }
        var malformed = new WidgetTestHostServicesBuilder().WithResponse(WidgetCaptureLibraryCapabilities.Copy,
            new CaptureLibraryBatchResult([new(new string('f', 32))])).Build();
        try { await malformed.CaptureLibrary.CopyAsync([Reference(item)]); throw new Exception("Mismatched result accepted"); }
        catch (WidgetCapabilityException error) { Check(error.ErrorCode == "malformed_response", "Unexpected SDK rejection"); }
    }
    internal static async Task Authority()
    {
        using var f = new Fixture();
        var item = await f.Store.SaveAsync(f.Source, default);
        var consent = new ConsentStore(Path.Combine(f.Root, "consent"));
        var simulation = new SimulatedPlatformBrokerBackend();
        await using var backend = new CompositePlatformBrokerBackend(simulation, simulation, captureLibrary: f.Store);
        await using var broker = new PlatformCapabilityBroker(Fixture.Identity, [PlatformCapabilities.CaptureLibraryManageV1], consent, backend);
        broker.SetLifecycle(BrokerLifecycleState.Interactive);
        long sequence = 0;
        Task<System.Text.Json.JsonElement> Copy() => broker.ExecuteAsync(new(BrokerJson.ProtocolVersion, ++sequence, Fixture.Identity,
            PlatformCapabilities.CaptureLibraryManageV1, "capture-library.copy", BrokerJson.ToElement(new CaptureLibraryCopyRequest([Reference(item)]))));
        try { await Copy(); throw new Exception("Unconsented copy accepted"); } catch (BrokerException) { }
        Check(CaptureLibraryCopyRequests.Pending is null, "Unconsented copy opened a picker");
        await consent.SetDecisionAsync(Fixture.Identity, PlatformCapabilities.CaptureLibraryManageV1, ConsentDecision.Grant);
        var copying = Copy();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (CaptureLibraryCopyRequests.Pending is null) await Task.Delay(10, deadline.Token);
        var pending = CaptureLibraryCopyRequests.Pending!;
        var destination = Directory.CreateDirectory(Path.Combine(f.Root, "allowed-copy")).FullName;
        broker.SetLifecycle(BrokerLifecycleState.Background);
        CaptureLibraryCopyRequests.Complete(pending.Id, destination);
        var result = BrokerJson.ParsePayload<CaptureLibraryBatchResult>(await copying);
        Check(result.Results.Single().ErrorCode is null && Directory.GetFiles(destination).Length == 1, "Admitted picker lost its in-flight authority");
        try { await Copy(); throw new Exception("Background copy accepted"); } catch (BrokerException) { }
        broker.SetLifecycle(BrokerLifecycleState.Interactive);
        var revoked = Copy();
        while (CaptureLibraryCopyRequests.Pending is null) await Task.Delay(10, deadline.Token);
        var revokedId = CaptureLibraryCopyRequests.Pending!.Id;
        await consent.SetDecisionAsync(Fixture.Identity, PlatformCapabilities.CaptureLibraryManageV1, ConsentDecision.Deny);
        await broker.RefreshConsentAsync();
        try { await revoked; throw new Exception("Revoked copy continued"); } catch (BrokerException) { } catch (OperationCanceledException) { }
        Check(CaptureLibraryCopyRequests.Pending is null, "Revoked picker retained destination authority");
        CaptureLibraryCopyRequests.Complete(revokedId, destination);
        Check(Directory.GetFiles(destination).Length == 1, "Late revoked completion copied more files");
    }
    private static async Task Reject(Func<Task> operation, string code)
    {
        try { await operation(); } catch (BrokerException error) when (error.Code == code) { return; }
        throw new Exception("Expected " + code);
    }
}

