# Game Help

Install from **Catalogue**. Game Help is a community widget and is not bundled in either installer. It requires your own Gemini API key. The widget owns Gemini requests and protected key storage; the host provides generic capture, media, document and intent services.

Capture access is requested when taking a screenshot or clip. Opening saved captures requests library read access. Sending a question with live web search enabled requests provider-document access for the required attribution. Denial or cancellation does not perform the protected action.

Run `./Build-CommunityPackage.ps1` to create a `.wrwidget` archive and SHA-256 sidecar. This does not install, enable or publish it. See [Game Help and capture](../../docs/reference/widget-capture-and-game-help.md) for details.

## Results and preferences

An answer completing while Game Help is not visible can create an Answers ready
notification. Open returns to that answer through the widget's declared result
intent. Cleared/trimmed answers return quietly to the conversation; opening a
notice never resubmits a question. The host accounts for main and pinned surfaces
and owns delivery preferences. No answer text or captured image is copied into the
notification. The existing answer operation lifetime is unchanged.

Spoiler and web-search choices use the shared PrivateState service and its
reviewed-update continuity. This version starts those legacy file-based choices
at defaults without importing the old file. API keys stay in their existing
secret store. Notification preferences live in the host, separately from both.
