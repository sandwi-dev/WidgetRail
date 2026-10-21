[CmdletBinding()]
param([Parameter(Mandatory)][string]$DllPath)
$ErrorActionPreference = 'Stop'
$nativePath = (Resolve-Path -LiteralPath $DllPath).Path
$repository = Split-Path $PSScriptRoot
# Derive requirements from the managed callers, so adding an import also makes
# an older native payload fail qualification. Do not execute code from the DLL.
$required = @(foreach ($source in @('src/OverlayFrontend.WinUI/Capture/NativeWindowCapture.cs', 'src/OverlayFrontend.WinUI/Previews/PreviewNative.cs')) {
    $text = [IO.File]::ReadAllText((Join-Path $repository $source))
    foreach ($match in [regex]::Matches($text, 'EntryPoint\s*=\s*"(Wrail[^"]+)"')) { $match.Groups[1].Value }
}) | Sort-Object -Unique
if ($required.Count -eq 0) { throw 'No managed capture/preview entry points were found.' }
if (-not ('WidgetRailNativeExportCheck' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class WidgetRailNativeExportCheck {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll", CharSet=CharSet.Ansi, ExactSpelling=true)]
    public static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FreeLibrary(IntPtr module);
}
'@
}
# DONT_RESOLVE_DLL_REFERENCES maps the export table without DllMain or loading
# dependencies. Addresses are inspected only and must never be invoked.
$module = [WidgetRailNativeExportCheck]::LoadLibraryExW($nativePath, [IntPtr]::Zero, 1)
if ($module -eq [IntPtr]::Zero) { throw "Cannot inspect native DLL: $nativePath (error $([Runtime.InteropServices.Marshal]::GetLastWin32Error()))." }
try {
    $missing = @($required | Where-Object { [WidgetRailNativeExportCheck]::GetProcAddress($module, $_) -eq [IntPtr]::Zero })
    if ($missing.Count) { throw "Native capture/preview DLL is incompatible with this frontend: missing $($missing -join ', ') in $nativePath. Rebuild with scripts/Build-WinUiWindowPreview.ps1 before publishing." }
} finally { [void][WidgetRailNativeExportCheck]::FreeLibrary($module) }
"Verified $($required.Count) managed capture/preview imports in $nativePath."
