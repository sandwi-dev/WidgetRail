# Game Help

Install from **Marketplace**. Game Help is a community widget and is not bundled in either installer. It requires your own Gemini API key. The widget owns Gemini requests and protected key storage; the host provides generic capture, media, document and intent services.

Capture access is requested when taking a screenshot or clip. Opening saved captures requests library read access. Sending a question with live web search enabled requests provider-document access for the required attribution. Denial or cancellation does not perform the protected action.

Run `./Build-CommunityPackage.ps1` to create a `.wrwidget` archive and SHA-256 sidecar. This does not install, enable or publish it. See [Game Help and capture](../../docs/reference/widget-capture-and-game-help.md) for details.
