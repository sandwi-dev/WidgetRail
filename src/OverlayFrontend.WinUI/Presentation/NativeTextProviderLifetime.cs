using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Text;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Windows.Foundation;

namespace WidgetRail.OverlayFrontend.WinUI.Presentation;

/// <summary>
/// Keeps a native text owner alive for each exported text provider/range. WinUI's
/// native TextAdapter retains a raw text-owner pointer; a UIA client may retain
/// the adapter after the virtualized document and its peer have been collected.
/// No global cache, delay, or accessibility-pattern suppression is involved.
/// </summary>
internal sealed partial class NativeTextProviderLifetime(
    FrameworkElement owner, ITextProvider inner, Func<IRawElementProviderSimple> enclosing) : ITextProvider
{
    private T Read<T>(Func<T> read) { try { return read(); } finally { GC.KeepAlive(owner); } }
    private ITextRangeProvider Wrap(ITextRangeProvider range) => range is null ? null! : new Range(owner, range, enclosing);
    private ITextRangeProvider[] Wrap(ITextRangeProvider[] ranges) => ranges?.Select(Wrap).ToArray() ?? [];
    public ITextRangeProvider DocumentRange => Read(() => Wrap(inner.DocumentRange));
    public SupportedTextSelection SupportedTextSelection => Read(() => inner.SupportedTextSelection);
    public ITextRangeProvider[] GetSelection() => Read(() => Wrap(inner.GetSelection()));
    public ITextRangeProvider[] GetVisibleRanges() => Read(() => Wrap(inner.GetVisibleRanges()));
    public ITextRangeProvider RangeFromChild(IRawElementProviderSimple childElement) => Read(() => Wrap(inner.RangeFromChild(childElement)));
    public ITextRangeProvider RangeFromPoint(Point screenLocation) => Read(() => Wrap(inner.RangeFromPoint(screenLocation)));

    private sealed partial class Range(FrameworkElement owner, ITextRangeProvider inner,
        Func<IRawElementProviderSimple> enclosing) : ITextRangeProvider
    {
        private ITextRangeProvider Inner => inner;
        private static ITextRangeProvider Unwrap(ITextRangeProvider other) => other is Range range ? range.Inner : other;
        private T Read<T>(Func<T> read) { try { return read(); } finally { GC.KeepAlive(owner); } }
        private void Run(Action action) { try { action(); } finally { GC.KeepAlive(owner); } }
        private ITextRangeProvider Wrap(ITextRangeProvider range) => range is null ? null! : new Range(owner, range, enclosing);
        public ITextRangeProvider Clone() => Read(() => Wrap(inner.Clone()));
        public bool Compare(ITextRangeProvider range) => Read(() => inner.Compare(Unwrap(range)));
        public int CompareEndpoints(TextPatternRangeEndpoint endpoint, ITextRangeProvider targetRange, TextPatternRangeEndpoint targetEndpoint) =>
            Read(() => inner.CompareEndpoints(endpoint, Unwrap(targetRange), targetEndpoint));
        public void ExpandToEnclosingUnit(TextUnit unit) => Run(() => inner.ExpandToEnclosingUnit(unit));
        public ITextRangeProvider FindAttribute(int attributeId, object value, bool backward) => Read(() => Wrap(inner.FindAttribute(attributeId, value, backward)));
        public ITextRangeProvider FindText(string text, bool backward, bool ignoreCase) => Read(() => Wrap(inner.FindText(text, backward, ignoreCase)));
        public object GetAttributeValue(int attributeId) => Read(() => inner.GetAttributeValue(attributeId));
        public void GetBoundingRectangles(out double[] rectangles)
        { try { inner.GetBoundingRectangles(out rectangles); } finally { GC.KeepAlive(owner); } }
        public IRawElementProviderSimple[] GetChildren() => Read(inner.GetChildren);
        public IRawElementProviderSimple GetEnclosingElement() => Read(enclosing);
        public string GetText(int maxLength) => Read(() => inner.GetText(maxLength));
        public int Move(TextUnit unit, int count) => Read(() => inner.Move(unit, count));
        public void MoveEndpointByRange(TextPatternRangeEndpoint endpoint, ITextRangeProvider targetRange, TextPatternRangeEndpoint targetEndpoint) =>
            Run(() => inner.MoveEndpointByRange(endpoint, Unwrap(targetRange), targetEndpoint));
        public int MoveEndpointByUnit(TextPatternRangeEndpoint endpoint, TextUnit unit, int count) => Read(() => inner.MoveEndpointByUnit(endpoint, unit, count));
        public void Select() => Run(inner.Select);
        public void AddToSelection() => Run(inner.AddToSelection);
        public void RemoveFromSelection() => Run(inner.RemoveFromSelection);
        public void ScrollIntoView(bool alignToTop) => Run(() => inner.ScrollIntoView(alignToTop));
    }
}
