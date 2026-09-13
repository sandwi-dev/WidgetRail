using WidgetRail.PlatformSettings;

internal static class StartupRegistrationTests
{
    public static Task Run()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "WidgetRail startup fixture", "app"));
        var store = new Store { Root = root };
        var startup = new StartupRegistration(store, root);
        Check(!startup.Read().Registered && startup.Read().CanChange && store.Writes == 0, "Default must be off without writing.");
        Check(startup.SetEnabled(true).Registered && store.Command == $"\"{Path.Combine(root, "OverlayHost.exe")}\"", "Quote the whole path and start hidden.");
        store.Approval = [3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        var disabled = startup.Read();
        Check(disabled.Registered && disabled.Message.StartsWith("Disabled in Windows"), "Do not claim Windows-disabled entries are active.");
        startup.SetEnabled(false);
        startup.SetEnabled(true);
        Check(startup.Read().Message.StartsWith("Disabled in Windows") && store.Approval[0] == 3, "Toggling must not bypass Windows disablement.");
        store.Approval = [99];
        Check(startup.Read().Message.StartsWith("Registered."), "Unknown approval must remain unknown.");
        store.Command = "another application";
        var writes = store.Writes;
        Check(!startup.SetEnabled(false).CanChange && store.Writes == writes && store.Command == "another application", "Never mutate a foreign entry.");
        store.Command = null;
        store.Root = root + "-old";
        Check(!startup.SetEnabled(true).CanChange && store.Writes == writes, "A moved or development copy cannot manage the installed copy.");
        store.Root = root;
        store.FailWrite = true;
        Check(startup.SetEnabled(true).Message.StartsWith("Could not") && !startup.Read().Registered, "Write failure must not claim success.");
        store.FailRead = true;
        Check(!startup.Read().CanChange && !startup.SetEnabled(false).CanChange, "Read failure must be contained.");
        return Task.CompletedTask;
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private sealed class Store : IStartupRegistrationStore
    {
        public string? Root;
        public string? Command;
        public byte[]? Approval;
        public int Writes;
        public bool FailRead;
        public bool FailWrite;
        public string? ReadInstallRoot() => FailRead ? throw new IOException("fixture") : Root;
        public string? ReadCommand() => Command;
        public byte[]? ReadApproval() => Approval;
        public void ChangeOwnedCommand(string command, bool enabled)
        {
            if (FailWrite) throw new UnauthorizedAccessException("fixture");
            Writes++;
            Command = enabled ? command : null;
        }
    }
}
