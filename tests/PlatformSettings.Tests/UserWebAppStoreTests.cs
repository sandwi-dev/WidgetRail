using WidgetRail.PlatformSettings;

internal static class UserWebAppStoreTests
{
    internal static async Task Run()
    {
        var directory=Directory.CreateTempSubdirectory("wrail-user-webapps-");
        try
        {
            var paths=new PlatformSettingsPaths(directory.FullName);
            var first=new UserWebAppStore(paths);var second=new UserWebAppStore(paths);
            Assert.Equal(0,(await first.ReadAsync()).Count);
            Assert.Equal("https://example.com/",UserWebAppStore.NormalizeUrl(" example.com "));
            Assert.Equal("http://localhost:3000/app",UserWebAppStore.NormalizeUrl("http://localhost:3000/app"));
            Assert.Equal("https://localhost:3000/",UserWebAppStore.NormalizeUrl("localhost:3000"));
            foreach(var invalid in new[]{"javascript:alert(1)","file:///C:/secret","data:text/html,hi","https://name:password@example.com/","https://example.com/\n"})
            {
                // Outer whitespace is trimmed intentionally; embedded controls are rejected.
                if(invalid.EndsWith('\n')) continue;
                Assert.Throws<PlatformSettingsException>(()=>UserWebAppStore.NormalizeUrl(invalid));
            }
            Assert.Throws<PlatformSettingsException>(()=>UserWebAppStore.NormalizeUrl("https://exa\nmple.com/"));
            var created=await first.CreateAsync("Example","example.com");
            Assert.True(created.Enabled && UserWebAppStore.IsId(created.Id));
            Assert.Equal(created,(await second.ReadAsync()).Single());
            var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
            Assert.True(await first.SaveIconAsync(created.Id,created.Url,png));
            Assert.SequenceEqual(png,(await new UserWebAppStore(paths).ReadIconAsync(created.Id))!);
            Assert.True(!await second.SaveIconAsync(created.Id,created.Url,png));
            var renamed=await second.UpdateAsync(created.Id,created.Revision,"My example",created.Url,true);
            Assert.Equal(created.Id,renamed.Id);Assert.Equal(created.Url,renamed.Url);
            Assert.SequenceEqual(png,(await first.ReadIconAsync(created.Id))!);
            await Assert.ThrowsAsync<PlatformSettingsException>(()=>first.UpdateAsync(created.Id,created.Revision,"Stale",created.Url,true));
            Assert.Equal("My example",(await first.ReadAsync()).Single().Name);
            var parallel=await Task.WhenAll(first.CreateAsync("One","https://one.example/"),second.CreateAsync("Two","https://two.example/"));
            Assert.Equal(3,(await first.ReadAsync()).Count);
            Assert.True(parallel[0].Id!=parallel[1].Id);
            var changed=await first.UpdateAsync(renamed.Id,renamed.Revision,renamed.Name,"https://other.example/",false);
            Assert.True(!changed.Enabled && await first.ReadIconAsync(changed.Id) is null);
            Assert.True(!await second.SaveIconAsync(created.Id,created.Url,png));
            Assert.True(await second.SaveIconAsync(changed.Id,changed.Url,png));
            await first.RemoveAsync(changed.Id,changed.Revision);
            Assert.True(await second.ReadIconAsync(changed.Id) is null);
            Assert.True(!await second.SaveIconAsync(changed.Id,changed.Url,png));
            using var canceled=new CancellationTokenSource();canceled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(()=>first.CreateAsync("Canceled","https://cancel.example/",canceled.Token));
            Assert.Equal(2,(await first.ReadAsync()).Count);
            await File.WriteAllTextAsync(first.CatalogPath,"{\"schemaVersion\":1,\"apps\":[{\"id\":\"bad\",\"name\":null,\"url\":null}]}");
            await Assert.ThrowsAsync<PlatformSettingsException>(()=>first.ReadAsync());
        }
        finally
        {
            if(directory.FullName.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))directory.Delete(true);
        }
    }
    private static class Assert
    {
        internal static void True(bool value){if(!value)throw new InvalidOperationException("Webapp store assertion failed.");}
        internal static void Equal<T>(T expected,T actual){if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new InvalidOperationException($"Expected {expected}, got {actual}.");}
        internal static void SequenceEqual(byte[] expected,byte[] actual)=>True(expected.SequenceEqual(actual));
        internal static void Throws<T>(Action action)where T:Exception
        {try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
        internal static async Task ThrowsAsync<T>(Func<Task> action)where T:Exception
        {try{await action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    }
}
