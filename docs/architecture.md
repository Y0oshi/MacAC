# Architecture

Layered by dependency. Lower layers don't reference higher ones. Game state
lives in one place (`SimCore`) whether or not a window is open.

```
MacAC.Client        the game: Vulkan renderer, UI, audio, input
      |
MacAC.Sim           SimCore (session, objects, inventory, movement, physics,
      |             magic) plus the input/panel layer the UI binds to
MacAC.Core          everything below: gameplay mechanics, DAT + pak access,
      |             the wire protocol, host services, the plugin contract
MacAC.Dat           the game's data files: memory-mapped archives, the record
                    decoders, no dependencies of its own
MacAC.Forge         optional: writes macac.pak from the DATs (ships as macac-bake)
```

## Projects

| Project | What it is | Depends on |
|---|---|---|
| `MacAC.Dat` | The game's four data files: each one memory-mapped, its directory decoded once into a sorted table, and a decoder per record type (meshes, rigs, motion, rooms, terrain, materials, textures, sound, UI layouts, the game tables). Reads only; knows nothing about rendering or the session. | nothing |
| `MacAC.Core` | `Host/` app paths and OS services. `Extensibility/` what plugins compile against. `Mechanics/` physics and collision, movement, animation, terrain, scenery, spells, world rules; no window, no GPU. `Assets/` the DAT files, and the optional machine-local `macac.pak`. `Wire/` UDP transport, ISAAC, fragments, reliable delivery, every message parser and builder. | Dat, packages |
| `MacAC.Sim` | `SimCore`: owns the session, objects, inventory, character state, selection, combat, casting, local movement, physics, projectiles, portal transit; exposes read-only lenses, typed directives and ordered deltas. `Cockpit/` input actions, key bindings, view models and panel contracts. | Core |
| `MacAC.Client` | The game: Vulkan renderer (Silk.NET, MoltenVK on macOS), world streaming, the UI built from the game's own layout data, audio, input. Projects Sim state, owns none of it. | Core, Sim, Dat |
| `MacAC.Forge` | Optional. Writes `macac.pak` from the DAT files; nothing needs it. Ships as `macac-bake`. | Core, Dat |

## Two rules

Behavior comes from the original game. How a slope stops you, when an
animation swaps, what bytes go on the wire: reproduced as observed, even where
the original is odd.

Gameplay truth has one owner. `SimCore` holds the only copy of session,
object, inventory, movement and physics state. The client borrows views of
those objects and never keeps a mirror.

## Content

Everything is read straight from the DAT files. World meshes and collision are
built as the world asks for them, on the streaming workers, and cached in
memory; a landblock costs tens of milliseconds off the frame path. There is
nothing to prepare before first play and nothing on disk that can go stale.

`macac-bake` can still write a `macac.pak` of the same content, and the engine
will use one if `MACAC_PAK_PATH` names it, but an ordinary install has none.

## Rendering

Vulkan 1.3 through Silk.NET, bindless textures, multi-draw indirect. Shaders
are GLSL compiled to committed SPIR-V by `tools/ShaderCompiler`.
The startup capability probe explains what is missing if the GPU can't do it.

## UI

The gameplay UI is the game's own. Layouts are imported from its layout data
and bound to runtime state by small controllers. Plugin panels use the markup
described in `plugin-ui-markup.md` and render in the same look.
