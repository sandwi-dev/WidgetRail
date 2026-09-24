# YT Music companion regression fixture

This preserves the former sandboxed YTMDesktop2 client for existing worker,
controller-action failure, package recovery and responsive-layout tests.
It is not the distributed YouTube Music application.

`YtMusicCompanionFixture.csproj` builds the original fixture assembly identity.
`Build-CommunityPackage.ps1` creates its deterministic companion package for the
explicit legacy conformance routes. Ordinary builds and releases use the
[standalone widget](../../samples/YtMusicWidget/README.md).

The fixture also illustrates the host's
[exact-port companion and private-secret contract](../../docs/reference/community-companion-services.md).
It requires the host loopback capability; it does not grant arbitrary networking
to sandboxed widgets.
