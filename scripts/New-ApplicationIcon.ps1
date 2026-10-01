[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$branding = Join-Path $PSScriptRoot '../assets/branding'
$document = [xml](Get-Content -LiteralPath (Join-Path $branding 'widgetrail.svg') -Raw)
Add-Type -AssemblyName System.Drawing
function New-BrandingPng([int]$Width, [int]$Height, [int]$LogoSize) {
    $large = [Drawing.Bitmap]::new($Width * 4, $Height * 4, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($large)
    $bitmap = [Drawing.Bitmap]::new($Width, $Height, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $scaled = [Drawing.Graphics]::FromImage($bitmap)
    $stream = [IO.MemoryStream]::new()
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.TranslateTransform(($Width - $LogoSize) * 2, ($Height - $LogoSize) * 2)
        $graphics.ScaleTransform($LogoSize * 4 / 256.0, $LogoSize * 4 / 256.0)
        foreach ($rect in $document.DocumentElement.ChildNodes | Where-Object LocalName -EQ 'rect') {
            $x = [single]::Parse($rect.x, [Globalization.CultureInfo]::InvariantCulture)
            $y = [single]::Parse($rect.y, [Globalization.CultureInfo]::InvariantCulture)
            $w = [single]::Parse($rect.width, [Globalization.CultureInfo]::InvariantCulture)
            $h = [single]::Parse($rect.height, [Globalization.CultureInfo]::InvariantCulture)
            $d = 2 * [single]::Parse($rect.rx, [Globalization.CultureInfo]::InvariantCulture)
            $path = [Drawing.Drawing2D.GraphicsPath]::new()
            $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($rect.fill))
            try {
                $path.AddArc($x, $y, $d, $d, 180, 90)
                $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
                $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
                $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
                $path.CloseFigure()
                $graphics.FillPath($brush, $path)
            } finally { $brush.Dispose(); $path.Dispose() }
        }
        $scaled.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
        $scaled.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $scaled.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $scaled.DrawImage($large, [Drawing.Rectangle]::new(0,0,$Width,$Height))
        $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
        return ,$stream.ToArray()
    } finally { $stream.Dispose(); $scaled.Dispose(); $bitmap.Dispose(); $graphics.Dispose(); $large.Dispose() }
}
$sizes = @(16,20,24,32,40,48,64,96,128,256)
$images = [Collections.Generic.List[byte[]]]::new()
foreach ($size in $sizes) {
    $bytes = New-BrandingPng $size $size $size
    $images.Add($bytes)
    if ($size -eq 256) { [IO.File]::WriteAllBytes((Join-Path $branding 'widgetrail.png'), $bytes) }
}
$output = [IO.File]::Create((Join-Path $branding 'widgetrail.ico'))
$writer = [IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index=0; $index -lt $sizes.Count; $index++) {
        $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$index].Length); $writer.Write([uint32]$offset)
        $offset += $images[$index].Length
    }
    foreach ($bytes in $images) { $writer.Write($bytes) }
} finally { $writer.Dispose() }
Write-Output "Generated WidgetRail PNG and ICO ($($sizes -join ', ') px) from widgetrail.svg."

# Package surfaces use the same vector source as the executable/window icon.
# Wide tiles and splash images center the mark on a transparent canvas.
$assets = Join-Path $PSScriptRoot '../src/OverlayFrontend.WinUI/Assets'
foreach ($asset in @(
    @('StoreLogo.png', 50, 50, 50),
    @('LockScreenLogo.scale-200.png', 48, 48, 48),
    @('Square44x44Logo.scale-200.png', 88, 88, 88),
    @('Square44x44Logo.targetsize-24_altform-unplated.png', 24, 24, 24),
    @('Square44x44Logo.targetsize-48_altform-lightunplated.png', 48, 48, 48),
    @('Square150x150Logo.scale-200.png', 300, 300, 240),
    @('Wide310x150Logo.scale-200.png', 620, 300, 240),
    @('SplashScreen.scale-200.png', 1240, 600, 256)
)) {
    [IO.File]::WriteAllBytes((Join-Path $assets $asset[0]), (New-BrandingPng $asset[1] $asset[2] $asset[3]))
}
Write-Output 'Generated WinUI package logos from widgetrail.svg.'
