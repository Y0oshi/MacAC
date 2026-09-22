# Building and running

## You need

- .NET 10 SDK in the band pinned by `global.json`.
- Your own Asheron's Call data files: `client_portal.dat`, `client_cell_1.dat`,
  `client_highres.dat`, `client_local_English.dat`. We don't ship them; the
  launcher can download them for you.
- A server. The examples use a local ACEmulator at `127.0.0.1:9000`.
- For the graphical client, a Vulkan 1.3 GPU. On macOS that is MoltenVK; the
  packaged engine bundles its own loader and MoltenVK. When running from
  source, `tools/bundle-vulkan.sh <bin dir>` copies them next to the build,
  or install `molten-vk` and `vulkan-loader` from Homebrew and point
  `VK_ICD_FILENAMES` at the MoltenVK ICD manifest.

The engine also builds on Windows and Linux (x64). The launcher is macOS only.

## Build

```bash
dotnet restore MacAC.slnx
dotnet build MacAC.slnx -c Release
```

## The content package is optional

World meshes and collision are built from the DATs as the world asks for them,
so there is nothing to prepare. `macac-bake` still writes the same content to a
package if you want one on disk; point the engine at it with `MACAC_PAK_PATH`:

```bash
dotnet run --project src/MacAC.Forge/MacAC.Forge.csproj -c Release -- \
  --dat-dir "$HOME/ac" --out "$HOME/ac/macac.pak"
```

About 570 MiB for the standard data set. Machine-local, never committed.
`MACAC_PAK_PATH` overrides the default `<DAT dir>/macac.pak`. The launcher does
not do this and does not need to: leave it alone unless you specifically want a
package on disk.

## Run the client

```bash
export MACAC_DAT_DIR="$HOME/ac" MACAC_PAK_PATH="$HOME/ac/macac.pak"
export MACAC_LIVE=1 MACAC_TEST_HOST=127.0.0.1 MACAC_TEST_PORT=9000
export MACAC_TEST_USER=youraccount MACAC_TEST_PASS=yourpassword
dotnet run --project src/MacAC.Client/MacAC.Client.csproj -c Release
```

The DAT directory can also be the first positional argument. Leave
`MACAC_LIVE` unset to load the world offline with no server.

The packaged engine app (`MacAC Engine.app`) runs the same assembly through
`Contents/MacOS/MacAC` with its bundled Vulkan libraries; it needs none of the
loader variables.

## Startup variables

| Variable | Effect |
|---|---|
| `MACAC_DAT_DIR` | Data-file directory |
| `MACAC_PAK_PATH` | Prepared package; defaults to `<DAT dir>/macac.pak` |
| `MACAC_LIVE=1` | Connect to a server instead of loading offline |
| `MACAC_TEST_HOST` / `MACAC_TEST_PORT` | Server endpoint |
| `MACAC_TEST_USER` / `MACAC_TEST_PASS` | Account |
| `MACAC_NO_AUDIO=1` | Skip audio |
| `MACAC_UNCAPPED_RENDER=1` | No frame pacing (measurement only) |
| `MACAC_DISPLAY_PROTOCOL=auto\|x11\|wayland` | Linux window backend |
| `MACAC_DEVTOOLS=1` | Vulkan validation and debug-utils layers |

`MACAC_RETAIL_CHASE`, `MACAC_CAMERA_COLLIDE`, `MACAC_CAMERA_ALIGN_SLOPE` and
`MACAC_RETAIL_CLOSE_DEGRADES` switch original-client behaviors that are on by
default; set one to `0` to compare. Everything else prefixed `MACAC_` is a
diagnostic probe, off by default, documented next to where it is read.

## The launcher

```bash
cd Launcher && ./scripts/build-app.sh
open build/MacAC.app
```

See `launcher.md`.

## Shaders

GLSL sources are in `src/MacAC.Client/Graphics/Shaders/`; the committed SPIR-V
next to them is what the client loads. After editing a shader
recompile with the bundled compiler, which injects the Vulkan bindings and
assigns varying locations before handing the source to shaderc:

```bash
dotnet run --project tools/ShaderCompiler -c Release -- src/MacAC.Client/Graphics/Shaders src/MacAC.Client/Graphics/Shaders/spv --force
```
