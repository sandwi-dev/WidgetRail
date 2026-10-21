using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WidgetRail.Samples.PlayniteLibrary;

/// <summary>Ask the existing desktop shell to launch Playnite outside the widget's process tree/job.</summary>
internal static class PlayniteDesktopLauncher
{
    internal static Task StartAsync(ProcessStartInfo start, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // No command interpreter, URI handler, inherited handles or widget environment.
        // A plain ShellExecute from this process can still inherit our kill-on-close job.
        var executable = Path.GetFullPath(start.FileName);
        var directory = Path.GetFullPath(start.WorkingDirectory);
        if (!File.Exists(executable) || !Directory.Exists(directory))
            throw new PlayniteSetupException("Playnite's application folder is unavailable. Select it again in connection settings.");
        if (!string.IsNullOrEmpty(start.Arguments))
            throw new ArgumentException("Use explicit launch arguments.", nameof(start));
        var arguments = string.Join(' ', start.ArgumentList.Select(QuoteArgument));
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            object? windows = null, desktop = null, document = null, shell = null;
            try
            {
                token.ThrowIfCancellationRequested();
                // IShellWindows::FindWindowSW(SWC_DESKTOP, SWFO_NEEDDISPATCH) returns
                // the actual Explorer desktop, not an in-process Shell.Application object.
                var type = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), throwOnError: true)!;
                windows = Activator.CreateInstance(type)!;
                object location = null!, root = null!; // VT_EMPTY, as required by FindWindowSW.
                int window;
                desktop = ((dynamic)windows).FindWindowSW(ref location, ref root, 8, out window, 1);
                if (desktop is null) throw new InvalidOperationException("The desktop shell is unavailable.");
                document = ((dynamic)desktop).Document;
                shell = ((dynamic)document).Application;
                token.ThrowIfCancellationRequested();
                ((dynamic)shell).ShellExecute(executable, arguments, directory, "open", 0 /* SW_HIDE */);
                completion.TrySetResult();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            { completion.TrySetCanceled(token); }
            catch (Exception error)
            {
                // Never fall back to a worker-owned process. The existing connection UI
                // keeps the failure recoverable without claiming Playnite was started.
                // This dedicated thread must transfer failures to its awaiting caller.
                var failure = new PlayniteSetupException(
                    "Windows could not start Playnite independently. Start it manually, then retry the connection.");
                failure.Data["ShellErrorHResult"] = error.HResult;
                completion.TrySetException(failure);
            }
            finally
            {
                // These RCWs are created and used exclusively by this STA operation.
                Release(shell); Release(document); Release(desktop); Release(windows);
            }
        }) { IsBackground = true, Name = "Playnite desktop launch" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        // Do not race a timeout against an already-dispatched shell command: it could
        // otherwise launch after the caller has reported cancellation and retried.
        return completion.Task;
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    internal static string QuoteArgument(string value)
    {
        // Windows argv quoting, not cmd/PowerShell escaping. Trailing backslashes
        // must be doubled before the closing quote, including custom profile roots.
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            result.Append(character); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
}
