namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class WidgetStylesValidationPage
{
    private Task PobGemInspectionAsync(string directory)=>PobPreviewMatrixAsync(directory,["gem"]);
}
