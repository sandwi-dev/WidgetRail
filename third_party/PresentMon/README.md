# PresentMon console dependency

WidgetRail distributes the unmodified Intel PresentMon **2.6.0 x64 console**
executable under its MIT license for FPS collection. The binary is downloaded
during publication using `scripts/Get-PresentMon.ps1`; it is not committed here.

- Upstream: https://github.com/GameTechDev/PresentMon
- Release: https://github.com/GameTechDev/PresentMon/releases/tag/v2.6.0
- SHA256: `b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af`
- License source: https://raw.githubusercontent.com/GameTechDev/PresentMon/v2.6.0/LICENSE.txt

Version and hash are locked in `eng/runtime-dependencies.json`. The runtime hash
in `PresentMonRunner.ExpectedSha256` must change in the same reviewed update.
Ship `LICENSE.txt` as `runtime/Performance/PresentMon-LICENSE.txt` alongside the
executable. No PresentMon service, driver, overlay, or MSI is installed.
