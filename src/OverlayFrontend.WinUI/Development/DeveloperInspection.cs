namespace WidgetRail.OverlayFrontend.WinUI.Development;

internal sealed record DeveloperInspection(string WidgetId, long Sequence, string FocusId,
    double Width, double Height, IReadOnlyList<DeveloperInspectionNode> Nodes, int Omitted);

internal sealed record DeveloperInspectionNode(string Id, string? ParentId, string Kind,
    bool Visible, bool Focused, double X, double Y, double Width, double Height, string Details);
