using GameBarAlternative.WidgetRuntime;
using GameBarAlternative.WidgetSdk;

return await WidgetApplicationBootstrap.RunAsync(args, () => new BetaWidget());

file sealed class BetaWidget : Widget
{
    public override WidgetView Render() => new(UI.Stack("beta-root",
        UI.Text("Independent beta application", "beta-title", "Fixture title"),
        UI.Text($"os={Environment.OSVersion.Platform};user={Environment.UserInteractive}",
            "beta-result", "Operating system result")));
}
