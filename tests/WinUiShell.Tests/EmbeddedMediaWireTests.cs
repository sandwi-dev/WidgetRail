using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WidgetRail.OverlayFrontend.WinUI.Media;
using WidgetRail.WidgetProtocol;

namespace WinUiShell.Tests;

[TestClass]
public sealed class EmbeddedMediaWireTests
{
    [TestMethod]
    public void NativeJsonPreservesAuthorityNumbersAndOmitsAbsentPlaybackFields()
    {
        var transport = new EmbeddedMediaTransport("session");
        using var initialize = JsonDocument.Parse(transport.Initialize());
        Assert.AreEqual("initialize", initialize.RootElement.GetProperty("command").GetString());
        Assert.IsFalse(initialize.RootElement.TryGetProperty("mediaKey", out _));
        var ready = new JsonObject
        {
            ["type"] = "ready", ["eventSequence"] = 1, ["commandSequence"] = 0,
            ["commandId"] = initialize.RootElement.GetProperty("commandId").GetInt64(),
            ["focus"] = "main", ["playing"] = false,
            ["bounds"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["width"] = 40, ["height"] = 40 },
        };
        foreach (var key in new[] { "environmentGeneration", "surfaceGeneration", "sessionGeneration", "controllerGeneration", "documentGeneration" })
            ready[key] = initialize.RootElement.GetProperty(key).GetInt64();
        Assert.IsTrue(transport.TryAccept(ready.ToJsonString(), out _));
        const long sequence = 9007199254740991;
        var command = transport.Dispatch(new EmbeddedMediaPlaybackCommand
            { Kind = EmbeddedMediaPlaybackCommandKind.SetVolume, Sequence = sequence, MediaKey = "song", Volume = .375 });
        Assert.IsNotNull(command);
        using var encoded = JsonDocument.Parse(command);
        var root = encoded.RootElement;
        Assert.AreEqual("volume", root.GetProperty("command").GetString());
        Assert.AreEqual(sequence, root.GetProperty("commandSequence").GetInt64());
        Assert.AreEqual(.375, root.GetProperty("volume").GetDouble());
        Assert.AreEqual(JsonValueKind.Number, root.GetProperty("volume").ValueKind);
        Assert.IsFalse(root.TryGetProperty("positionSeconds", out _));
        Assert.IsFalse(root.TryGetProperty("muted", out _));
        Assert.IsNull(transport.Dispatch(EmbeddedMediaCommand.Activate));
    }
}
