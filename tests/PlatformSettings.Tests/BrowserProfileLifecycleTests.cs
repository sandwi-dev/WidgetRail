using WidgetRail.PlatformSettings;

internal static class BrowserProfileLifecycleTests
{
    internal static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "wrail-profile-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const string owner = "example.browser";
            var first = new BrowserProfileLifecycleStore(root);
            var second = new BrowserProfileLifecycleStore(root);
            var original = await first.RegisterAsync(owner, "persistent-0123456789", "0123456789.json");
            var neighbor = await first.RegisterAsync("example.neighbor", "persistent-ABCDEF", "ABCDEF.json");
            await first.ActivateAsync(owner); // Same operation used after successful updates/retained-version selection.
            Equal(original, await second.RegisterAsync(owner, "persistent-0123456789", "0123456789.json"), "Update changed the profile");
            var operation = Guid.NewGuid().ToString("N");
            await first.ResetAsync(owner, operation, false);
            var reset = await second.ReadAsync(owner);
            Check(!reset.Removed && reset.Profiles.Length == 0 && reset.Pending.Contains(original!), "Reset lost deletion evidence");
            await second.ResetAsync(owner, operation, false);
            Equal(reset.Generation, (await first.ReadAsync(owner)).Generation, "Retry rotated identity twice");
            var replacement = await second.RegisterAsync(owner, "persistent-0123456789", "0123456789.json");
            Check(replacement!.Name != original!.Name && replacement.LibraryFile != original.LibraryFile, "Reset reused a profile pending deletion");
            await first.CompleteAsync(owner, original);
            Check((await first.ReadAsync(owner)).Profiles.Contains(replacement), "Old cleanup removed the new profile");
            await first.ResetAsync(owner, Guid.NewGuid().ToString("N"), true);
            Check(await second.RegisterAsync(owner, "persistent-0123456789", "0123456789.json") is null, "Removed worker recreated data");
            await second.ActivateAsync(owner);
            var reinstalled = await second.RegisterAsync(owner, "persistent-0123456789", "0123456789.json");
            Check(reinstalled!.Name != replacement.Name, "Reinstall inherited removed sign-in data");
            await first.CompleteAsync(owner, replacement);
            Check((await first.ReadAsync(owner)).Profiles.Contains(reinstalled), "Delayed deletion affected reinstall");
            Equal(neighbor, (await first.ReadAsync("example.neighbor")).Profiles.Single(), "Neighbor profile changed");
            await Task.WhenAll(Enumerable.Range(0, 8).Select(i => new BrowserProfileLifecycleStore(root).RegisterAsync(owner, "persistent-concurrent" + i, null)));
            Check((await first.ReadAsync(owner)).Profiles.Length == 9, "Concurrent registration lost profiles");
            try { await first.RegisterAsync(owner, "persistent-safe", "../outside.json"); throw new Exception("Accepted library traversal"); }
            catch (InvalidDataException) { }
            var webapps = new UserWebAppStore(new(root));
            var app = await webapps.CreateAsync("Example", "https://example.com");
            var webProfile = await first.RegisterAsync(app.Id, "persistent-webapp", "persistent-webapp.json");
            await webapps.UpdateAsync(app.Id, app.Revision, "Renamed", app.Url, false);
            Equal(webProfile, (await first.ReadAsync(app.Id)).Profiles.Single(), "Disable or rename removed browser data");
            await webapps.RemoveAsync(app.Id, app.Revision + 1);
            var removed = await first.ReadAsync(app.Id);
            Check(removed.Removed && removed.Pending.Contains(webProfile!), "Webapp removal did not queue its browser data");
            await new UserWebAppStore(new(root)).ReadAsync();
            Equal(removed.Generation, (await first.ReadAsync(app.Id)).Generation, "Removal recovery rotated twice");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual, string message) => Check(EqualityComparer<T>.Default.Equals(expected, actual), message);
}
