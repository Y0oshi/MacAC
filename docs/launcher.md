# The launcher

`Launcher/` is a SwiftUI app (macOS 14+, Apple Silicon and Intel). It has
five pages.

**Setup** is one-time. Two steps, each with its own button:

1. Download game data. Fetches the four DAT files from the data release feed
   (`Ac1container/ac_container` on GitHub, about 580 MB) into the folder shown,
   skipping files whose SHA-256 already matches. You can point it at DATs you
   already have with Choose…
2. Download engine. Reads `engine-manifest.json` from the latest MacAC
   release, picks the build for your CPU and installs it under
   `~/Library/Application Support/MacAC/Engine/`. Check for update re-reads
   the manifest.
There is also a developer field for a source-built `MacAC.Client.dll`. When
set, Play runs that through `dotnet` instead of the installed engine.

**Server Browser** lists the public servers with live player counts from
TreeStats, sorted by who's online. Click Add to put one in My Servers.

**My Servers** is your list. Private servers go in here by hand as
`name` and `host:port`.

**Accounts** belong to a server. Pick a username and password; most servers
create the account the first time you log in. Passwords are stored in
`launcher.json` (mode 0600) under `~/Library/Application Support/MacAC/`.

**Play** picks a server and an account and starts the engine with
`MACAC_LIVE=1`, `MACAC_TEST_HOST/PORT/USER/PASS` and the data paths from
Setup. The engine's log streams into the page; it is also written to
`~/Library/Application Support/MacAC/engine.log`. Stop closes the session.

Set `MACAC_RELEASE_BASE=http://host/path` to make the launcher fetch
manifests and assets from a local server while testing a release.
