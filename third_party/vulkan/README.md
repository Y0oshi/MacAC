# Vulkan runtime (universal)

Prebuilt universal (arm64 + x86_64) libraries bundled into the engine so
players need nothing installed.

| File | Source | Version |
|---|---|---|
| `libMoltenVK.dylib` | KhronosGroup/MoltenVK official release (`MoltenVK-macos.tar`) | 1.4.2 |
| `libvulkan.1.dylib` | KhronosGroup/Vulkan-Loader, built from tag `v1.4.357` with `CMAKE_OSX_ARCHITECTURES=arm64;x86_64` | 1.4.357 |

Both are Apache-2.0; see the LICENSE files beside them.
