using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WidgetRail.OverlayFrontend.WinUI.Presentation;
using WidgetRail.OverlayPlatformClient;
using WidgetRail.WidgetProtocol;

namespace WidgetRail.OverlayFrontend.WinUI.Validation;

internal sealed partial class GlyphValidationPage
{
    private sealed record Specimen(ControllerPrompt Prompt, int Size, Grid Stage, FontIcon Icon);
    private sealed record InkSample(string Family, string Prompt, int Size, string Font, int PixelWidth, int PixelHeight,
        int InkPixels, double InkWidth, double InkHeight, double CenterErrorX, double CenterErrorY,
        double Envelope, string Sha256);
    private readonly Grid rasterMatrix = new() { ColumnSpacing = 8, RowSpacing = 4,
        HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Microsoft.UI.Colors.Black) };
    private readonly List<Specimen> specimens = [];
    private readonly List<InkSample> inkSamples = [];

    private void InitializeRasterSpecimens(Panel content)
    {
        AutomationProperties.SetAutomationId(rasterMatrix, "Glyph.Specimens");
        var familyButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var family in new[] { ControllerFamily.Xbox, ControllerFamily.PlayStation })
        {
            var button = new Button { Content = family.ToString() };
            AutomationProperties.SetAutomationId(button, "Glyph." + family);
            button.Click += (_, _) => presenter.SetControllerFamily(family);
            familyButtons.Children.Add(button);
        }
        content.Children.Add(familyButtons);
        content.Children.Add(rasterMatrix);
        var prompts = new[] { ControllerPrompt.A, ControllerPrompt.LeftBumper, ControllerPrompt.LeftTrigger, ControllerPrompt.Guide };
        var sizes = new[] { 18, 24, 32 };
        for (var column = 0; column < prompts.Length; ++column) rasterMatrix.ColumnDefinitions.Add(new() { Width = new(140) });
        for (var row = 0; row < sizes.Length; ++row)
        {
            rasterMatrix.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (var column = 0; column < prompts.Length; ++column)
            {
                var prompt = prompts[column];
                var icon = new FontIcon { FontSize = sizes[row], Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var stage = new Grid { Width = 96, Height = 48, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
                stage.Children.Add(icon);
                AutomationProperties.SetAutomationId(stage, $"Glyph.Ink.{prompt}.{sizes[row]}");
                var cell = new StackPanel { Children = { stage, new TextBlock { Text = $"{prompt} · {sizes[row]} DIP",
                    FontSize = 12, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), HorizontalAlignment = HorizontalAlignment.Center } } };
                Grid.SetColumn(cell, column); Grid.SetRow(cell, row); rasterMatrix.Children.Add(cell);
                specimens.Add(new(prompt, sizes[row], stage, icon));
            }
        }
        WidgetControllerPrompts.Changed += RefreshRasterSpecimens;
        RefreshRasterSpecimens();
    }

    private void RefreshRasterSpecimens()
    {
        foreach (var specimen in specimens)
            WidgetGlyphs.Apply(specimen.Icon, new() { Id = "ink", Kind = ViewNodeKind.ControllerGlyph, ControllerPrompt = specimen.Prompt },
                WidgetControllerPrompts.PlayStation);
        AutomationProperties.SetName(rasterMatrix, WidgetControllerPrompts.PlayStation ? "PlayStation optical glyph matrix" : "Xbox optical glyph matrix");
    }

    private async Task CheckNativeInkAsync(FontIcon[] nativeIcons)
    {
        var retained = specimens.Select(value => value.Icon).ToArray();
        foreach (var family in new[] { ControllerFamily.Xbox, ControllerFamily.PlayStation })
        {
            presenter.SetControllerFamily(family);
            await NativeFrameAsync();
            foreach (var specimen in specimens)
            {
                var result = await SampleAsync(specimen, family);
                inkSamples.Add(result);
                // FontSize is an optical size. The authored shoulder advance is
                // 1.35 em; all other representatives use 1 em. Tolerance covers
                // raster hinting, not the source fonts' old padded em squares.
                Check(result.InkPixels > specimen.Size * specimen.Size * .2 && result.Envelope is >= .76 and <= 1.01,
                    $"{family} {specimen.Prompt} {specimen.Size} DIP has correctly sized native ink (envelope {result.Envelope:F3})");
                Check(result.CenterErrorX <= 2 && result.CenterErrorY <= 2,
                    $"{family} {specimen.Prompt} {specimen.Size} DIP ink is optically centered");
            }
            foreach (var prompt in Enum.GetValues<ControllerPrompt>())
            {
                var icon = nativeIcons.Single(value => AutomationProperties.GetAutomationId(value) == "Widget.prompt." + prompt);
                var bitmap = new RenderTargetBitmap();
                await bitmap.RenderAsync(icon).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
                var bytes = (await bitmap.GetPixelsAsync()).ToArray();
                Check(Enumerable.Range(0, bytes.Length / 4).Any(index => bytes[index * 4 + 3] > 32),
                    $"{family} mapped {prompt} reaches native raster output");
            }
        }
        Check(retained.SequenceEqual(specimens.Select(value => value.Icon)), "family changes retain every native specimen control");
        foreach (var prompt in new[] { ControllerPrompt.A, ControllerPrompt.LeftBumper, ControllerPrompt.LeftTrigger, ControllerPrompt.Guide })
        foreach (var size in new[] { 18, 24, 32 })
        {
            var values = inkSamples.Where(value => value.Prompt == prompt.ToString() && value.Size == size).ToArray();
            Check(values.Length == 2 && values[0].Sha256 != values[1].Sha256, $"{prompt} {size} DIP family change replaces actual ink");
        }
        presenter.SetControllerFamily(ControllerFamily.Unknown);
        await NativeFrameAsync();
        var retainedGuide = await SampleAsync(specimens.Single(value => value.Prompt == ControllerPrompt.Guide && value.Size == 24), ControllerFamily.PlayStation);
        Check(retainedGuide.Sha256 == inkSamples.Single(value => value.Family == "PlayStation" && value.Prompt == "Guide" && value.Size == 24).Sha256,
            "unknown controller observation retains the last family's rendered Guide ink");
    }

    private static async Task NativeFrameAsync()
    {
        var next = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Rendering(object? sender, object args) => next.TrySetResult();
        CompositionTarget.Rendering += Rendering;
        try { await next.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
        finally { CompositionTarget.Rendering -= Rendering; }
    }

    private static async Task<InkSample> SampleAsync(Specimen specimen, ControllerFamily family)
    {
        // Oversample the actual native FontIcon, with transparent padding, so
        // tiny glyphs cannot pass by owning a large XAML allocation rectangle.
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(specimen.Stage, 288, 144).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
        var bytes = (await bitmap.GetPixelsAsync()).ToArray();
        var width = bitmap.PixelWidth; var height = bitmap.PixelHeight;
        var left = width; var top = height; var right = -1; var bottom = -1; var ink = 0;
        for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x)
        {
            var offset = (y * width + x) * 4;
            if (bytes[offset + 3] < 32 || Math.Max(bytes[offset], Math.Max(bytes[offset + 1], bytes[offset + 2])) < 32) continue;
            ++ink; left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
        if (ink == 0 || left == 0 || top == 0 || right == width - 1 || bottom == height - 1)
            throw new InvalidOperationException($"{family} {specimen.Prompt} {specimen.Size}: empty or clipped glyph raster.");
        var scaleX = width / specimen.Stage.ActualWidth; var scaleY = height / specimen.Stage.ActualHeight;
        var inkWidth = (right - left + 1) / scaleX; var inkHeight = (bottom - top + 1) / scaleY;
        var advance = specimen.Prompt is ControllerPrompt.LeftBumper or ControllerPrompt.LeftTrigger ? 1.35 : 1;
        return new(family.ToString(), specimen.Prompt.ToString(), specimen.Size, specimen.Icon.FontFamily.Source, width, height, ink,
            inkWidth, inkHeight, Math.Abs((left + right + 1) / 2d - width / 2d) / scaleX,
            Math.Abs((top + bottom + 1) / 2d - height / 2d) / scaleY,
            Math.Max(inkWidth / (specimen.Size * advance), inkHeight / specimen.Size), Convert.ToHexString(SHA256.HashData(bytes)));
    }

    private void WriteRasterResult(bool passed, string? error = null)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetRail", "WinUI", "diagnostics");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "glyph-result.json"), JsonSerializer.Serialize(new
        {
            pid = Environment.ProcessId, passed, checks, error, samples = inkSamples, rasterizationScale = XamlRoot?.RasterizationScale,
            scope = "Native FontIcon ink, optical bounds, centering, mapping coverage and family replacement. Not physical legibility acceptance.",
        }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}
