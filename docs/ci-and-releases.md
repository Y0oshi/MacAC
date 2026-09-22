# CI and releases

Two workflows under `.github/workflows/`.

`ci.yml` runs on every pull request and push to `main`: locked restore and a
Release build on hosted `macos-14` (Apple Silicon).

`release.yml` runs on a `v*` tag (or by hand). It takes the version from the
tag, calls `tools/build-release.sh`, signs and notarizes when the
`MACOS_CERT_P12` / `APPLE_ID` secrets are set, and publishes a GitHub Release
with:

```
MacAC-<version>.dmg              universal launcher
MacAC-Engine-osx-arm64.zip       engine for Apple Silicon
MacAC-Engine-osx-x64.zip         engine for Intel
engine-manifest.json             what the launcher reads to install/update
SHA256SUMS.txt
```

## Cutting a release

1. Set `<Version>` in `Directory.Build.props`. It is the one version for
   every assembly, the launcher, the manifest and the label on the character
   selection screen.
2. Build, run the client against your DATs, log into a server.
3. Tag and push:

   ```bash
   git tag v1.0.0
   git push origin v1.0.0
   ```

Versions must sort above the previous one under SemVer or the launcher won't
offer the update. Releases are never marked pre-release; the launcher reads
`releases/latest/download/engine-manifest.json` and GitHub's `latest` skips
pre-releases.

## Signing

`build-release.sh` signs ad-hoc unless `CODESIGN_IDENTITY` names a Developer
ID. Ad-hoc builds open after the Privacy & Security "Open Anyway" step the
README describes. With the signing and notarization secrets configured,
`release.yml` produces a notarized DMG that opens without it.

## Building locally

```bash
./tools/build-release.sh
```

needs the .NET SDK from `global.json` and the Xcode Command Line Tools. It
publishes both engines with `tools/package-engine-app.sh`, builds the launcher
with `Launcher/scripts/build-app.sh`, and writes the DMG. It works from a
folder inside iCloud Drive; the signing steps copy out to `/tmp` first because
File Provider extended attributes upset `codesign`.
