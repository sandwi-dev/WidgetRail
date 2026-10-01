# Playnite process workflow fixture

This executable hosts the production `PlayniteLibraryWidget` and
`PlayniteLibraryApplicationService` through `WidgetApplicationBootstrap`.
Only the Playnite provider is fake. It has three games, returns no artwork,
and records a launch into a temporary journal instead of starting a process.
Connection probing is fake too; credentials, networking, and system actions
are never used.

`WidgetBridge.Tests` packages and installs it into an isolated catalog. Its
workflow opens Library, searches, obtains an indexed lease, rejects a stale
query, selects the current poster, opens production details, leaves/returns
through lifecycle transitions, and verifies the exact launched game. The
fixture is complementary to the separate shipped-executable admission test;
it does not qualify an actual Playnite Bridge installation or real game launch.

The fixture-only environment variable `WRAIL_PLAYNITE_PROCESS_FIXTURE_ROOT`
must identify an absolute temporary directory containing `fixture-owned`.
Production entry points do not inspect this variable. The test restores the
previous environment and drains the process before removing the temporary
catalog.
