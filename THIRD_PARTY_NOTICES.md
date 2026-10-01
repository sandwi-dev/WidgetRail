# Third-party notices

WidgetRail includes or uses the third-party components below. These retain
their own licenses; the project license does not replace these terms.

## WinUIEx 2.9.3 (WinUI frontend)

Project: https://github.com/dotMorten/WinUIEx

Copyright (c) 2021 Morten Nielsen. License: MIT.
The complete license is retained in `third_party/WinUIEx/LICENSE` and copied into
the WinUI frontend output. The pinned NuGet library supplies transparent window
backdrop integration; its source is not copied into WidgetRail.

## Microsoft Windows App SDK 2.5.1

Project: https://github.com/microsoft/WindowsAppSDK

The WinUI frontend uses Microsoft's Windows App SDK and its signed Windows App
Runtime Framework. The distribution retains the SDK license and notices under
`licenses/Microsoft.WindowsAppSDK-license.txt` and
`licenses/Microsoft.WindowsAppSDK-NOTICE.txt`, together with the runtime's embedded
package licenses. These components retain their respective redistribution terms.

## Kenney Input Prompts 1.5A

Created by Kenney, available at https://kenney.nl/assets/input-prompts

License: Creative Commons Zero (CC0 1.0). The original Xbox Series and
PlayStation Series fonts, character maps, license, and attribution are bundled
in `assets/fonts/kenney`. WinUI loads private subsets from `assets/fonts/winui-controller`;
they are not installed as system fonts.
Subset transformations and source hashes are documented in
`assets/fonts/winui-controller/NOTICE.txt` and `inventory.json`.

## PromptFont

PromptFont by Shinmera (Yukari Hafner), available at https://shinmera.com/promptfont

License: SIL Open Font License 1.1. The unmodified font and its complete license
are retained in `assets/fonts/promptfont`. WinUI loads a privately renamed subset for
the PS-logo button missing from the Kenney set; it is not installed as a system font.

## Microsoft .NET 10 frontend and .NET 8 service runtimes

Projects: https://github.com/dotnet/runtime and https://github.com/dotnet/windowsdesktop

Copyright (c) .NET Foundation and Contributors. License: MIT.
The self-contained WinUI publication retains its .NET runtime notices. The private
.NET 8 runtime distribution retains Microsoft's `dotnet/LICENSE.txt` and
`dotnet/ThirdPartyNotices.txt`. Runtime versions and archive checksums are pinned
in `eng/runtime-dependencies.json`.

## ViGEmClient 1.16.18.0

Project: https://github.com/nefarius/ViGEmClient

Commit: 9e91a124d179bf26a878a952153042ac871da243

Copyright (c) 2017-2023 Nefarius Software Solutions e.U. and Contributors.

License: MIT. The complete unmodified license and pinned native source closure
are in `third_party/ViGEmClient`.

## Public Suffix List snapshot 2026-08-19

Project: https://publicsuffix.org/list/

Snapshot commit: e8c9a2b2b2856b6449999dd0ec0d118f364ed0cd

Licensed under the Mozilla Public License 2.0. The complete license and the
unaltered checked-in snapshot are in `third_party/public_suffix_list`.

## Microsoft Edge WebView2 SDK 1.0.4078.44

Project: https://developer.microsoft.com/microsoft-edge/webview2/

Copyright (C) Microsoft Corporation. All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.
* Redistributions in binary form must reproduce the above copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.
* The name of Microsoft Corporation, or the names of its contributors may not
  be used to endorse or promote products derived from this software without
  specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
