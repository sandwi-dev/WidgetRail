using System.Runtime.InteropServices;
using WidgetRail.WidgetBridge;

internal static class WindowsRecycleTests
{
    internal static async Task RoundTrip()
    {
        var directory = Path.Combine(Path.GetTempPath(), "widgetrail-recycle-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var name = "widgetrail-recycle-probe-" + Guid.NewGuid().ToString("N") + ".txt";
        var path = Path.Combine(directory, name);
        var bytes = System.Text.Encoding.UTF8.GetBytes("Only this uniquely named temporary fixture may be recycled.");
        await File.WriteAllBytesAsync(path, bytes);
        try
        {
            try { await WindowsFileOperations.RecycleAsync(path, new(true)); throw new Exception("Cancelled recycle unexpectedly succeeded"); }
            catch (OperationCanceledException) { }
            if (!File.Exists(path)) throw new Exception("Cancellation removed the original");
            if (await WindowsFileOperations.RecycleAsync(directory, default)) throw new Exception("Directory recycling must be rejected");
            if (!await WindowsFileOperations.RecycleAsync(path, default) || File.Exists(path)) throw new Exception("Windows did not recycle the fixture");
            await RestoreExactAsync(directory, name);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!File.Exists(path)) await Task.Delay(20, timeout.Token);
            if (!bytes.SequenceEqual(await File.ReadAllBytesAsync(path))) throw new Exception("Windows restore changed the fixture bytes");
        }
        finally
        {
            // Do not empty the Recycle Bin or operate on any other item. A failed
            // restore intentionally leaves the uniquely named fixture recoverable.
            if (File.Exists(path)) File.Delete(path);
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
    }
    private static Task RestoreExactAsync(string directory, string name)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            object? shell = null, bin = null, items = null, match = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
                bin = ((dynamic)shell!).NameSpace(10);
                items = ((dynamic)bin).Items();
                for (var i = 0; i < (int)((dynamic)items).Count; i++)
                {
                    object entry = ((dynamic)items).Item(i);
                    if ((string)((dynamic)entry).Name == name && string.Equals((string)((dynamic)entry).ExtendedProperty("System.Recycle.DeletedFrom"), directory, StringComparison.OrdinalIgnoreCase))
                    {
                        if (match is not null) throw new Exception("Duplicate unique recycle fixture");
                        match = entry;
                    }
                    else Marshal.ReleaseComObject(entry);
                }
                if (match is null) throw new Exception("Exact temporary fixture was not found in the Windows Recycle Bin");
                ((dynamic)match).InvokeVerb("undelete");
                completion.TrySetResult();
            }
            catch (Exception error) { completion.TrySetException(error); }
            finally
            {
                foreach (var item in new[] { match, items, bin, shell }) if (item is not null && Marshal.IsComObject(item)) Marshal.ReleaseComObject(item);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task;
    }
}
