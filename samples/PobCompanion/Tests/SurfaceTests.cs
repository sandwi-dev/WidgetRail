using System.Reflection;
using WidgetRail.Samples.PobCompanion;
using WidgetRail.WidgetProtocol;
using WidgetRail.WidgetSdk;

internal static class SurfaceTests
{
    internal static async Task RunAsync()
    {
        foreach (var game in new[] { 1, 2 })
        {
            var directory = Directory.CreateTempSubdirectory("pob-surfaces-");
            var closed = 0;
            var widget = WidgetTestHost.Attach(new PobWidget(game,
                new PobEngine(directory.FullName, directory.FullName, "unused"), directory.FullName),
                new WidgetTestHostServicesBuilder().WithHandler(WidgetWebSessionCapabilities.Command, async (command, token) =>
                {
                    if (command.Operation is WebSessionOperation.Open or WebSessionOperation.OpenAdapted)
                        return new WebSessionReply { Session = new(new string('a', 32)) };
                    if (command.Operation == WebSessionOperation.ReadMessages) await Task.Delay(Timeout.Infinite, token);
                    if (command.Operation == WebSessionOperation.Close) closed++;
                    return new WebSessionReply();
                }).Build());
            try
            {
                CheckNative("import workspace");
                typeof(PobWidget).GetField("status", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(widget, "Preparing build");
                typeof(PobWidget).GetField("busy", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(widget, true);
                CheckNative("engine loading");
                typeof(PobWidget).GetField("busy", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(widget, false);
                typeof(PobWidget).GetField("detailKind", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(widget, "library");
                CheckNative("build library");
                await (Task)typeof(PobWidget).GetMethod("OpenWebsiteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(widget, [$"https://www.pathofexile.com/{(game == 1 ? "trade" : "trade2")}/search/Standard/test", CancellationToken.None])!;
                var browser = widget.Render().CreateSnapshot("pob.surface", 2);
                if (browser.Surface?.Appearance != WidgetSurfaceAppearance.Theme || browser.Root.Kind != ViewNodeKind.ModalLayer ||
                    !Nodes(browser.Root).Any(node => node.Id == "pob.website" && node.WebBrowser?.HostedSession?.Id == new string('a', 32)) ||
                    ViewSnapshotValidator.Validate(browser).Count != 0)
                    throw new InvalidOperationException($"PoE{game} website did not preserve its hosted browser without desktop blur.");
                await widget.OnActionAsync(new("website-close", "pob.website.close"));
                CheckNative("return from browser");
                if (closed != 0) throw new InvalidOperationException("Closing the modal disposed its retained browser session.");
            }
            finally
            {
                await WidgetTestHost.DestroyAsync(widget);
                directory.Delete(true);
            }

            void CheckNative(string state)
            {
                var snapshot = widget.Render().CreateSnapshot("pob.surface", 1);
                if (snapshot.Surface?.Appearance != WidgetSurfaceAppearance.DesktopAcrylic ||
                    snapshot.ProtocolVersion < ProtocolConstants.DesktopAcrylicVersion ||
                    ViewSnapshotValidator.Validate(snapshot).Count != 0)
                    throw new InvalidOperationException($"PoE{game} {state} did not publish a valid desktop surface.");
                if (!ViewSnapshotValidator.Validate(snapshot with { ProtocolVersion = ProtocolConstants.DesktopAcrylicVersion - 1 })
                    .Any(error => error.Path == "$.surface" && error.Code == "feature_requires_version"))
                    throw new InvalidOperationException($"PoE{game} {state} accepted desktop acrylic on an older host.");
            }
        }
    }

    private static IEnumerable<ViewNode> Nodes(ViewNode node) => new[] { node }.Concat(node.Children.SelectMany(Nodes));
}
