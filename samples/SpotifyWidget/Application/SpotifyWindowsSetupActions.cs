using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WidgetRail.Samples.SpotifyWidget;

internal interface ISpotifySetupActions
{
    ValueTask OpenDeveloperDashboardAsync(CancellationToken cancellationToken);
    ValueTask CopyRedirectUriAsync(CancellationToken cancellationToken);
}

internal sealed class SpotifyWindowsSetupActions : ISpotifySetupActions
{
    private const uint ClipboardUnicodeText = 13;
    private const uint MoveableMemory = 0x0002;
    private const int MaximumClipboardAttempts = 5;
    private static readonly TimeSpan ClipboardRetryDelay = TimeSpan.FromMilliseconds(25);

    public ValueTask OpenDeveloperDashboardAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var process = Process.Start(new ProcessStartInfo(
                SpotifyApplicationContract.DeveloperDashboardUri)
            {
                UseShellExecute = true,
            });
            if (process is null)
                throw new InvalidOperationException("No browser process was started.");
            process.Dispose();
            return ValueTask.CompletedTask;
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException)
        {
            throw new SpotifyApplicationException(
                "dashboard_unavailable",
                "The Spotify developer dashboard could not be opened.", exception);
        }
    }

    public async ValueTask CopyRedirectUriAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaximumClipboardAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (NativeMethods.OpenClipboard(nint.Zero))
            {
                try
                {
                    WriteClipboardText(SpotifyApplicationContract.ExactRedirectUri);
                    return;
                }
                finally
                {
                    _ = NativeMethods.CloseClipboard();
                }
            }
            if (attempt + 1 < MaximumClipboardAttempts)
                await Task.Delay(ClipboardRetryDelay, cancellationToken)
                    .ConfigureAwait(false);
        }
        throw new SpotifyApplicationException(
            "clipboard_unavailable", "The redirect URI could not be copied.");
    }

    private static void WriteClipboardText(string value)
    {
        var characters = (value + '\0').ToCharArray();
        var memory = NativeMethods.GlobalAlloc(
            MoveableMemory, checked((nuint)(characters.Length * sizeof(char))));
        if (memory == nint.Zero) throw ClipboardFailure();
        var transferred = false;
        try
        {
            var destination = NativeMethods.GlobalLock(memory);
            if (destination == nint.Zero) throw ClipboardFailure();
            try { Marshal.Copy(characters, 0, destination, characters.Length); }
            finally { _ = NativeMethods.GlobalUnlock(memory); }
            if (!NativeMethods.EmptyClipboard()) throw ClipboardFailure();
            if (NativeMethods.SetClipboardData(ClipboardUnicodeText, memory) == nint.Zero)
                throw ClipboardFailure();
            transferred = true;
        }
        finally
        {
            if (!transferred) _ = NativeMethods.GlobalFree(memory);
        }
    }

    private static SpotifyApplicationException ClipboardFailure() => new(
        "clipboard_unavailable", "The redirect URI could not be copied.",
        new Win32Exception(Marshal.GetLastWin32Error()));

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenClipboard(nint owner);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern nint SetClipboardData(uint format, nint memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern nint GlobalAlloc(uint flags, nuint bytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern nint GlobalLock(nint memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalUnlock(nint memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern nint GlobalFree(nint memory);
    }
}
