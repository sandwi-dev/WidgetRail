[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$AppPid,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/winui-shell')
)
# This is the frontend foundation fixture: its transparent margin contains the
# three sample points below. It does not prove media or arbitrary widget alpha.
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class AlphaWindowProbe {
 [StructLayout(LayoutKind.Sequential)] public struct Rect {public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll",SetLastError=true)] public static extern bool GetWindowRect(IntPtr hwnd,out Rect r);
 [DllImport("user32.dll",SetLastError=true)] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
}
"@
$env:Path=[Environment]::GetEnvironmentVariable('Path','Machine')+';'+[Environment]::GetEnvironmentVariable('Path','User')
$windows=winapp ui list-windows -a $AppPid --json | ConvertFrom-Json
$hwnd=[IntPtr]($windows | Where-Object title -Like 'WidgetRail*' | Select-Object -First 1).hwnd
$r=[AlphaWindowProbe+Rect]::new()
if(-not [AlphaWindowProbe]::GetWindowRect($hwnd,[ref]$r)){throw 'Window rect unavailable'}
$form=[System.Windows.Forms.Form]::new()
$form.FormBorderStyle='None'; $form.ShowInTaskbar=$false
$form.StartPosition='Manual'; $form.Bounds=[System.Drawing.Rectangle]::FromLTRB($r.Left,$r.Top,$r.Right,$r.Bottom)
$form.BackColor=[System.Drawing.Color]::Red
$form.Show()
if(-not [AlphaWindowProbe]::SetWindowPos($form.Handle,$hwnd,$r.Left,$r.Top,$r.Right-$r.Left,$r.Bottom-$r.Top,0x10)){throw 'Background placement failed'}
try {
 foreach($phase in @('red','lime')){
  $form.BackColor=[System.Drawing.Color]::FromName($phase); $form.Refresh()
  $until=[DateTime]::UtcNow.AddMilliseconds(400)
  while([DateTime]::UtcNow -lt $until){[System.Windows.Forms.Application]::DoEvents();Start-Sleep -Milliseconds 10}
  winapp ui screenshot -a $AppPid --capture-screen -o "$OutputDirectory/alpha-$phase.png" --json | Out-File "$OutputDirectory/alpha-$phase.json"
  if($LASTEXITCODE -ne 0){throw 'Capture failed'}
 }
} finally {$form.Close();$form.Dispose()}
$red=[System.Drawing.Bitmap]::new((Join-Path $OutputDirectory 'alpha-red.png'))
$green=[System.Drawing.Bitmap]::new((Join-Path $OutputDirectory 'alpha-lime.png'))
try {
 $samples=@(); foreach($point in @(@(20,120),@(20,240),@(20,360))){
  $a=$red.GetPixel($point[0],$point[1]);$b=$green.GetPixel($point[0],$point[1]);
  $pass=$a.R -gt 240 -and $a.G -lt 15 -and $b.G -gt 240 -and $b.R -lt 15
  $samples+=@{x=$point[0];y=$point[1];red=@($a.R,$a.G,$a.B);green=@($b.R,$b.G,$b.B);pass=$pass}
 }
 $samples | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $OutputDirectory 'alpha-results.json')
 if(@($samples | Where-Object {-not $_.pass}).Count){throw 'Desktop alpha samples failed'}
 'Desktop alpha: 3/3 sample locations passed across red/green background changes.'
} finally {$red.Dispose();$green.Dispose()}
