# MacAC

Asheron's Call on the Mac. Native launcher, no Windows, no Wine.

Works on Apple Silicon and Intel. Needs macOS 14 or newer.

## Install

1. Grab the latest `MacAC-x.y.z.dmg` from [Releases](../../releases).
2. Open it and drag MacAC into Applications.
3. Open MacAC.

macOS will refuse to open it the first time because the app isn't notarized. That's expected. To get past it:

1. Click **Done** on the warning.
2. Open **System Settings → Privacy & Security**.
3. Scroll down. Next to the message about MacAC, click **Open Anyway**.
4. Confirm, and enter your password if it asks. You only have to do this once.

The engine the launcher downloads afterwards needs none of this — the launcher
checks it against the release manifest and installs it ready to run.

## First run

Go to **Setup**. Two buttons, in order:

- **Download game data** – about 580 MB
- **Download engine** – picks the right build for your Mac

Then:

- **Server Browser** – every public server, live player counts. Click Add.
- **Accounts** – pick a username and password. Most servers create the account on your first login.
- **Play**

## Building it yourself

You need the Xcode Command Line Tools and the .NET 10 SDK. Full Xcode is not required.

```bash
dotnet build MacAC.slnx -c Release
cd Launcher && ./scripts/build-app.sh
```

The launcher ends up in `Launcher/build/MacAC.app`. To build a full release (both engines, the DMG, and the manifest the launcher reads):

```bash
./tools/build-release.sh
```

Tagging `v*` on GitHub does the same thing and publishes it.

## License

MIT. See [LICENSE](LICENSE).
