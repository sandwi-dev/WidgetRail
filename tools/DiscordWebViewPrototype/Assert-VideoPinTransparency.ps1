param([Parameter(Mandatory)][string]$Screenshot,[Parameter(Mandatory)][string]$Output)
$ErrorActionPreference='Stop'
# This assertion is for the direct-video fixture's opaque 16:9 source in a taller pin.
# Sample away from rounded corners and text; a dark pixel is not proof of alpha.
Add-Type -AssemblyName System.Drawing
$bitmap=[Drawing.Bitmap]::new([IO.Path]::GetFullPath($Screenshot))
try {
    $padding=($bitmap.Height-$bitmap.Width*9/16)/2
    if($padding -lt 4){throw 'Fixture capture has no measurable top/bottom padding.'}
    $x=[int]($bitmap.Width/2);$y=[int][Math]::Floor($padding/2)
    $top=$bitmap.GetPixel($x,$y);$bottom=$bitmap.GetPixel($x,$bitmap.Height-1-$y)
    $video=$bitmap.GetPixel($x,[int]($bitmap.Height/2))
    $passed=$top.A -eq 0 -and $bottom.A -eq 0 -and $video.A -eq 255
    [ordered]@{passed=$passed;topAlpha=$top.A;bottomAlpha=$bottom.A;videoAlpha=$video.A;
        width=$bitmap.Width;height=$bitmap.Height;sourceAspectRatio='16:9'}|
        ConvertTo-Json|Set-Content -LiteralPath $Output
    if(!$passed){throw "Video/padding alpha mismatch: top=$($top.A), bottom=$($bottom.A), video=$($video.A)"}
    'Native video is opaque; top and bottom padding are transparent.'
} finally {$bitmap.Dispose()}
