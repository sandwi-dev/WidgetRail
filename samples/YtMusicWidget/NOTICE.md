# Third-party components

The standalone implementation was informed by the architecture of
[Now Playing](https://github.com/LoZazaMastro/Now-Playing), by **LoZazaMastro**
(MIT). Its React/Decky UI and application code are not included in this package.

The package includes pinned, checksum-verified dependencies:

- CPython embedded runtime — Python Software Foundation license; `python/LICENSE.txt`.
- ytmusicapi — MIT; license inside its `.dist-info` directory.
- yt-dlp — Unlicense; license and notices inside `python/yt_dlp.zip`.
- yt-dlp-ejs — Unlicense; license inside its `.dist-info` directory.
- QuickJS-NG — MIT and included third-party notices; `python/quickjs-LICENSE.txt`.
- Requests, charset-normalizer, idna, urllib3, certifi and websocket-client —
  license files included in their `.dist-info` directories (including MPL 2.0
  for certifi and Apache 2.0 for websocket-client).
- Microsoft WebView2 SDK — distributed under its NuGet package license.

`Service/runtime-lock.json` records exact download URLs and SHA-256 hashes.
The YouTube Music service and brand belong to Google. This is an unofficial client.
