# Line graphs

`UI.LineGraph` is an informational native WinUI element, available from protocol
78. It adds no controller focus stop or pointer interaction. The host uses native
path geometry rather than a WebView, bitmap stream, or preview chart dependency.

```csharp
UI.LineGraph("CPU usage over the last minute",
    new LineGraphDefinition(
        [new(-60, 20), new(-40, 35), new(-30, null), new(-10, 60), new(0, 45)],
        XMinimum: -60, XMaximum: 0, Minimum: 0, Maximum: 100, Unit: "%"),
    "cpu.history");
```

Up to 120 samples must have strictly increasing finite X coordinates inside the
horizontal bounds. Non-null values must be finite and within the vertical bounds.
Null values break the line; never substitute zero for unavailable measurements.
An omitted maximum scales above the largest available value, including negative
series. Empty series show a collecting state. Authors should label the time window
and meaning outside the graph and use stable element IDs for live updates.

The readable name is required. The native graph also exposes latest/min/max values
as its accessible description. Plot and labels use the control's foreground brush,
including theme/high-contrast styling. Updates change geometry and accessible
values without replacing the native control or taking focus. Graph data updates
are paint/accessibility changes, not controller-authority changes.

The WRSS element role is `lineGraph`, for example
`lineGraph { color: var(--accent); height: 160px; }`. Authored classes can style
individual graphs through the same Bridge/native style path.

The SDK Gallery's controls page contains an offline example with a deliberate
gap. Performance Monitor provides the live application, keeping only a bounded
minute of graph samples and resetting application traces on target changes.
