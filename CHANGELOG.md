# Changelog

## Unreleased

### Fixed

- The engine would not start on a freshly set-up Mac. A download carries macOS's
  quarantine flag, `ditto` hands it to every file it unpacks, and Gatekeeper then
  kills the engine the moment the launcher spawns it — with no dialog, no error
  and nothing in the log. The launcher now clears the flag as it installs, which
  it is entitled to do: it has already checked the download against the SHA-256
  in the release manifest.

- Logging in no longer fails at random. The handshake opens with a single
  sign-in datagram over UDP, and every later step was protected — the connect
  response is resent until the server proves it heard, and the character roster
  and data check ride the reliable layer — but that first datagram had nothing
  behind it. Losing it once meant waiting out the whole ten-second deadline and
  then being told the server had not answered. It is now resent until something
  comes back, and a timeout says which step stalled, how long it waited and how
  many sign-ins it sent.
- A part whose texture upload has not landed is no longer drawn. It carried the
  no-texture marker into the shader, which sampled an unbound slot and returned
  whatever the driver had there.

### Changed

- Game content is built from the DAT files as the world asks for it. There is no
  preparation step before first play and nothing on disk that can go stale. The
  launcher no longer has a content step; `macac-bake` still exists for anyone who
  wants a package on disk, and the engine will use one if pointed at it.
- Own DAT layer. `MacAC.Dat` reads the game's four data files directly:
  memory-mapped archives, a sorted directory decoded once, and a decoder per
  record type. Verified against every object in the retail data (114,565
  portal/local/high-res records and all 805,347 cell records decode to the same
  values; the 15 blocking-particle scripts now decode the payload the game
  actually wrote). The third-party data packages are gone.
- The world frame draws from a declared order rather than from nested control
  flow, and the batching layer states what it previously only implied: what makes
  two draws shareable, which way round the opaque and transparent passes go, and
  who owns the per-instance data a frame stages for the GPU.
- The engine's own input actions — flying, the debug panel, cycling the weather,
  the orbit-hold mouse button — were renamed. Bindings are stored by action name,
  so if you had rebound any of those twelve they go back to their defaults (F1–F3,
  F7–F10, Ctrl+M, Tab, Ctrl, right mouse); retail actions are untouched.

## 1.0.0

First release.

- Native macOS launcher (Apple Silicon and Intel): downloads the game data,
  installs the engine, keeps servers and accounts.
- Vulkan 1.3 client over MoltenVK, 60 fps on Apple Silicon.
- Plugin hooks for panels and render packs.
