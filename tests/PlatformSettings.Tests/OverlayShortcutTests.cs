using System.Text.Json.Nodes;
using WidgetRail.PlatformSettings;

internal static class OverlayShortcutTests
{
    internal static async Task Run()
    {
        if (new ControllerSettings().EffectiveOverlayShortcut != OverlayShortcutSettings.Default)
            throw new Exception("Fresh profiles must leave the keyboard shortcut disabled.");
        if(new ControllerSettings().EffectiveReplaySaveShortcut!=OverlayShortcutSettings.ReplayDefault||OverlayShortcutSettings.ReplayDefault.Enabled)
            throw new Exception("Replay shortcut must remain opt-in and independent of overlay opening.");
        if (new ControllerSettings { F1ShortcutEnabled = true }.EffectiveOverlayShortcut != new OverlayShortcutSettings { Enabled = true })
            throw new Exception("Legacy opt-in must retain F1.");
        var explicitOff = new ControllerSettings { F1ShortcutEnabled = true, OverlayShortcut = new() { Enabled = false, Key = 0x4A } };
        if (explicitOff.EffectiveOverlayShortcut.Enabled || explicitOff.EffectiveOverlayShortcut.Key != 0x4A)
            throw new Exception("An explicit disabled shortcut must override a legacy F1 opt-in.");
        var choice = new OverlayShortcutSettings { Enabled = true, Key = 0x4A, Modifiers = OverlayShortcutModifiers.Control | OverlayShortcutModifiers.Alt };
        if (!choice.IsWellFormed() || choice.DisplayName != "Ctrl + Alt + J") throw new Exception("Keyboard chord formatting is incorrect.");
        foreach (var key in new[] { -1, 0, 1, 0x10, 0x11, 0x12, 0x5B, 0xA0, 0xE7, 256 })
            if ((choice with { Key = key }).IsWellFormed()) throw new Exception("Non-key or modifier admitted as a primary key.");
        if ((choice with { Modifiers = (OverlayShortcutModifiers)16 }).IsWellFormed()) throw new Exception("Unknown modifiers admitted.");
        var root = Path.Combine(Path.GetTempPath(), "wrail-shortcut-" + Guid.NewGuid().ToString("N"));
        var store = new PlatformSettingsStore(new(root));
        try
        {
            await store.UpdateAsync(value => value with { Controllers = new() { F1ShortcutEnabled = true, HoldDpadToScroll = true } });
            var legacy = JsonNode.Parse(await File.ReadAllTextAsync(store.Paths.SettingsFile))!.AsObject();
            legacy["controllers"]!.AsObject().Remove("overlayShortcut");
            await File.WriteAllTextAsync(store.Paths.SettingsFile, legacy.ToJsonString());
            var loaded = await store.LoadAsync();
            if (!loaded.Controllers.EffectiveOverlayShortcut.Enabled || !loaded.Controllers.HoldDpadToScroll)
                throw new Exception("Legacy profile migration changed unrelated settings.");
            await store.UpdateAsync(value => value with { Controllers = value.Controllers with { OverlayShortcut = choice } });
            if ((await new PlatformSettingsStore(new(root)).LoadAsync()).Controllers.EffectiveOverlayShortcut != choice)
                throw new Exception("Custom chord did not survive restart.");
            var canonical = await File.ReadAllTextAsync(store.Paths.SettingsFile);
            foreach (var corrupt in new[] { "{\"enabled\":true,\"key\":0,\"modifiers\":\"none\"}",
                "{\"enabled\":true,\"key\":74,\"modifiers\":\"unknown\"}",
                "{\"enabled\":\"true\",\"key\":74,\"modifiers\":\"none\"}",
                "{\"enabled\":true,\"key\":74,\"modifiers\":16}" })
            {
                var document = JsonNode.Parse(canonical)!;
                document["controllers"]!["overlayShortcut"] = JsonNode.Parse(corrupt);
                await File.WriteAllTextAsync(store.Paths.SettingsFile, document.ToJsonString());
                try { await store.LoadAsync(); throw new Exception("Malformed shortcut was accepted."); }
                catch (PlatformSettingsException) { }
            }
            await File.WriteAllTextAsync(store.Paths.SettingsFile, canonical);
            var replay=OverlayShortcutSettings.ReplayDefault with{Enabled=true};
            await store.UpdateAsync(value=>value with{Controllers=value.Controllers with{ReplaySaveShortcut=replay}});
            var both=await store.LoadAsync();
            if(both.Controllers.EffectiveOverlayShortcut!=choice||both.Controllers.EffectiveReplaySaveShortcut!=replay)
                throw new Exception("Replay shortcut overwrote the overlay chord.");
            try{await store.UpdateAsync(value=>value with{Controllers=value.Controllers with{ReplaySaveShortcut=replay with{Key=0}}});throw new Exception("Invalid replay chord admitted.");}
            catch(PlatformSettingsException) { }
            var omitted = JsonNode.Parse(canonical)!;
            omitted["controllers"]!["overlayShortcut"] = JsonNode.Parse("{\"enabled\":false}");
            omitted["controllers"]!["replaySaveShortcut"] = JsonNode.Parse("{\"enabled\":false}");
            await File.WriteAllTextAsync(store.Paths.SettingsFile, omitted.ToJsonString());
            if ((await store.LoadAsync()).Controllers.EffectiveOverlayShortcut != OverlayShortcutSettings.Default)
                throw new Exception("Omitted chord fields did not retain safe defaults.");
            if((await store.LoadAsync()).Controllers.EffectiveReplaySaveShortcut!=OverlayShortcutSettings.ReplayDefault)
                throw new Exception("Omitted replay chord fields did not retain safe defaults.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
