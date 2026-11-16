namespace WidgetRail.OverlayFrontend.WinUI.Shell;

internal static class PinnedAdjustmentToolbarPlacement
{
    internal static PinnedBounds Choose(PinnedBounds pin,PinnedBounds area,int width,int height,int gap)
    {
        gap=Math.Clamp(gap,0,Math.Max(0,Math.Min(area.Width,area.Height)/4));
        width=Math.Clamp(width,1,Math.Max(1,area.Width-2*gap));
        height=Math.Clamp(height,1,Math.Max(1,area.Height-2*gap));
        var x=Math.Clamp(pin.X+pin.Width-width,area.X+gap,area.X+area.Width-width-gap);
        if(pin.Y-height-gap>=area.Y+gap)return new(x,pin.Y-height-gap,width,height);
        if(pin.Y+pin.Height+height+gap<=area.Y+area.Height-gap)return new(x,pin.Y+pin.Height+gap,width,height);
        var y=Math.Clamp(pin.Y,area.Y+gap,area.Y+area.Height-height-gap);
        if(pin.X+pin.Width+width+gap<=area.X+area.Width-gap)return new(pin.X+pin.Width+gap,y,width,height);
        if(pin.X-width-gap>=area.X+gap)return new(pin.X-width-gap,y,width,height);
        return new(x,y,width,height);
    }
}
