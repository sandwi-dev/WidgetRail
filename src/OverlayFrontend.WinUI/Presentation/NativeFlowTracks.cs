using Microsoft.UI.Xaml;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>Projects bounded flow intent to native Grid tracks; Grid owns measurement.</summary>
internal static class NativeFlowTracks
{
    internal sealed record Plan(IReadOnlyList<GridLength> Tracks, IReadOnlyList<int> Slots);

    internal static Plan Create(IReadOnlyList<double> grows, double gap, string? justify)
    {
        if (grows.Any(grow => grow > 0)) justify = "start";
        var tracks = new List<GridLength>();
        var slots = new int[grows.Count];
        if (grows.Count > 0 && justify is "center" or "end" or "space-around")
            tracks.Add(new(justify == "space-around" ? .5 : 1, GridUnitType.Star));
        for (var index = 0; index < grows.Count; ++index)
        {
            if (index > 0)
            {
                // A minimum on a star track is not additive: it changes the
                // distribution ratios. Reserve gap before distributing free space.
                tracks.Add(new(gap));
                if (justify is "space-between" or "space-around") tracks.Add(new(1, GridUnitType.Star));
            }
            slots[index] = tracks.Count;
            tracks.Add(grows[index] > 0 ? new(grows[index], GridUnitType.Star) : GridLength.Auto);
        }
        if (grows.Count > 0 && justify is "center" or "space-around")
            tracks.Add(new(justify == "space-around" ? .5 : 1, GridUnitType.Star));
        return new(tracks, slots);
    }
}
