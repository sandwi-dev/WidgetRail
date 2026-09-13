# Licensing

WidgetRail keeps the platform open while letting widget authors choose how to
license their own work.

| Project-owned material | License |
|---|---|
| Platform, native host, broker, runtime, developer tools and other files unless overridden below | [GPL-3.0-only](LICENSE) |
| `src/WidgetSdk/`, `src/WidgetProtocol/`, `src/WidgetApplicationRuntime/`, `src/Shared/` | [MPL-2.0](LICENSES/MPL-2.0.txt) |
| `src/FirstPartyWidgets/`, `samples/`, `templates/` | [MIT](LICENSES/MIT.txt) |
| Original example code in the documentation and original starter source/tests/project files emitted by `wrail` scaffolding commands | [MIT](LICENSES/MIT.txt) |
| Third-party code, data, artwork and dependencies | Their existing licenses and terms |

The three MPL-noticed files in `src/WidgetRuntime/` - `RuntimeProtocol.cs`,
`WidgetWorkerServer.cs` and `WidgetWorkerNotificationLane.cs` - are also MPL-covered.
They are compiled into the application bootstrap included with the SDK. The
remaining host runtime stays GPL-covered.

The license files and explicit file notices establish the exceptions to the
repository's default GPL license. Existing third-party notices take precedence
over the project-owned defaults. Copyright remains with the respective authors. These declarations apply to
this revision and do not revoke rights already granted for earlier versions.

## Write your own widget

An independently authored widget that uses the public SDK can use your chosen
license, including a proprietary license. You can copy or adapt first-party
widget code, sample code and starter templates under MIT; keep its copyright
and permission notice with substantial copied portions.

The SDK and its protocol dependency are MPL-covered. When distributing those
libraries, comply with MPL's source availability and notice requirements,
including for any modifications you make to covered files. This does not
require independently written widget files to adopt MPL.

The `wrail` tool itself is GPL-covered. Its **original generated starter
boilerplate is separately licensed under MIT**; running the scaffolder does not
make your generated widget GPL. Copied SDK libraries, third-party assets and
other dependencies retain their own licenses.

## Modify the platform

If you distribute the GPL-covered platform or a modified version, provide its
Corresponding Source and comply with GPLv3. Commercial use and paid distribution
are allowed. Private modifications that you do not distribute do not require
publication solely because of GPL.

MPL-covered SDK files remain MPL-covered when distributed separately. Combining
them with the GPL platform must follow the applicable license terms, including
MPL's secondary-license provisions.

## Reuse a complete first-party widget carefully

The widget's own code is MIT, but that does not relicense its dependencies.
Some first-party widgets, particularly Settings, reference GPL-covered platform
libraries. Copying their MIT UI or logic into your own independent widget is
permitted; distributing a program that incorporates those GPL dependencies
still carries the dependencies' obligations. Prefer the public SDK boundary
when building a community widget.

## Names, logos and service integrations

These software licenses do not grant rights to third-party trademarks or logos,
or waive provider API terms. Review [icon provenance](docs/reference/widget-icon-provenance.md)
and [third-party notices](THIRD_PARTY_NOTICES.md) before redistributing assets.
Contributor changes use the license applicable to the files they modify.
