# Changelog

## 1.13.1

- **Editor**: Fixed auto-setup color matching selecting wrong palette rows in Linear projects — `GetMaterialBaseColor` now uses `GetVector` instead of `GetColor` to read raw sRGB values, preventing double gamma-to-linear conversion during Delta-E comparison.

## 1.13.0

- **Shader**: Added Texture Variation mode — assign shadow and highlight texture variants per terrain layer. The shader picks which texture to display based on the toon lighting band, using the terrain layer's own texture as the base/mid band.
- **Controller**: Replaced `usePaletteRemap` toggle with `TerrainToonMode` enum (FlatColor / PaletteRemap / TextureVariation). Existing components auto-migrate via `OnValidate`.
- **Editor**: Mode selector dropdown with dedicated UI per mode — texture slots for TextureVariation, palette ramp for PaletteRemap, color picker for FlatColor.

## 1.12.1

- **Shader**: Fixed palette remap inconsistency — LUT is now built in the correct color space (linear for Linear projects, sRGB for Gamma) so terrain texture lookups match palette colors reliably.
- **Shader**: Changed `_PaletteRowLUT` 3D texture from `RGBA32` to `RFloat` — stored row indices are no longer corrupted by implicit sRGB gamma conversion on read.
- **Shader**: Palette ramp and row LUT now use explicit `sampler_point_clamp` — eliminates color bleeding from bilinear interpolation between palette cells.
- **Editor**: Added import-settings validation for palette ramp texture (sRGB, Point filter, no compression) with actionable warnings.

## 1.12.0

- **Shader**: Added palette remap mode for terrain — terrain layer textures are sampled and each pixel is automatically remapped to its shadow/base/highlight palette equivalent via a 3D color lookup table.
- **Runtime**: `PaletteToonTerrainController` gains `usePaletteRemap` toggle and `paletteRampTexture` field. When enabled, the controller generates a 32³ 3D LUT mapping any color to its palette row.
- **Editor**: Terrain inspector shows remap settings when enabled, hides per-layer color pickers (colors are driven automatically by the textures).

## 1.11.0

- **Runtime**: Added terrain support — new `PaletteToonRamp_Terrain` shader with splatmap blending for up to 4 terrain layers, each with independent shadow/base/highlight palette colors.
- **Runtime**: Added `PaletteToonTerrainController` — manages material instances on `Terrain` components with per-layer palette color configuration.
- **Editor**: Added `PaletteToonTerrainControllerEditor` — custom inspector with per-layer foldouts, palette grid picker with layer-aware auto-advance, and terrain layer name display.
- **Editor**: Added `Tools > Palette Toon > Apply To Selected Terrains` for one-click terrain setup.
- **Editor**: Added `Tools > Palette Toon > Create Local Terrain Material Preset`.
- **Runtime**: Bundled `PaletteToonRamp_Terrain.mat` terrain material.

## 1.9.1

- **Editor**: Auto-match on FBX import now enabled by default — new settings assets ship with `autoMatchOnImport = true` and the bundled ENDESGA-64 3xN palette pre-assigned.
- **Editor**: Auto setup now applies band percentages (shadow 0.3 / base 0.75 / highlight 1.0) to configured controllers.
- **Runtime**: Changed default band balance to shadow 0.3 / base 0.75 / highlight 1.0 (was 0.35 / 0.40 / 0.25).

## 1.9.0

- **Editor**: Added `Tools > Palette Toon > Auto Setup From Material Colors` — automatically reads FBX material colors, matches them to a 3-column palette (shadow/base/highlight per row), and configures `PaletteToonController` indices per material slot.
- **Editor**: Added `PaletteToonAutoSetupSettings` (Project Settings > Palette Toon > Auto Setup) — configure palette texture, match tolerance, fallback row, and optional auto-match on `.fbx` import.
- **Editor**: Added `PaletteToonModelPostprocessor` — optional `AssetPostprocessor` that automatically configures controllers when `.fbx` models are imported (toggle in settings).
- **Editor**: Color matching uses perceptual CIELAB Delta-E (CIE76) for accurate palette lookups.

## 1.5.0

- **Performance**: SRP Batcher compatible — shader properties moved into `CBUFFER_START(UnityPerMaterial)`, per-object colors now use material instances instead of `MaterialPropertyBlock`.
- **Runtime**: Material instance is created automatically on enable and cleaned up on disable/destroy, restoring the original shared material.

## 1.4.3

- **Shader**: Fixed shadow band alignment — shadow drop now uses `Threshold2 - Threshold1` so shadow boundaries align exactly with light band boundaries.

## 1.4.2

- **Shader**: Shadows now step down one band instead of zeroing the signal — highlight in shadow shows base, base in shadow shows shadow color. Creates layered toon shadow depth.

## 1.4.1

- **Shader**: `Intensity Affects Bands` now defaults to `1` — light brightness intuitively scales band reach.
- **Shader**: Smoothed shadow edges with `smoothstep` to eliminate crackly artifacts from low-resolution shadow maps.

## 1.4.0

- **Shader**: Point/spot lights now combine surface orientation (N·L) with linear range falloff — bands match the light gizmo and respond to surface facing direction.
- **Shader**: Removed `Use Range Percent For Local Lights` toggle — the new combined approach replaces both previous modes.
- **Inspector**: Palette grid moved to top — primary interaction is now immediately visible.
- **Inspector**: Auto-advance color picking — click 3 palette colors in sequence to assign Shadow → Base → Highlight (3 clicks instead of 9+).
- **Inspector**: Added 3-band preview bar showing shadow/base/highlight colors proportional to band widths.
- **Inspector**: Advanced settings (Base Tint, Color Space, Intensity, Accumulation, Fog) collapsed into foldout by default.
- **Inspector**: Removed threshold readout fields — thresholds are now internal implementation details.
- **Runtime**: `convertPaletteToProjectColorSpace` now defaults to `false` — palette colors are used as-is (real sRGB colors out of the box).
- **Runtime**: Threshold fields hidden from fallback inspector.

## 1.3.1

- Fixed range-percentage mode fallback for non-punctual additional lights (directional additional lights now use classic band contribution).

## 1.3.0

- Added band distribution controls by percentage:
  - `Dark Band %`
  - `Base Band %`
  - `Highlight Band %`
  - Percentages are auto-normalized and converted to thresholds internally.
- Added range-driven local light mapping for point/spot:
  - `Use Range % For Point/Spot` (default on)
  - Band placement now follows light range volume (gizmo) instead of light intensity by default.
- Kept `Intensity Affects Bands` for optional old-style behavior when desired.

## 1.2.0

- Added explicit lighting-behavior controls on `PaletteToonController`:
  - `Intensity Affects Bands`
  - `Band Accumulation` (`Add` / `Max`)
  - `Apply Fog`
- Defaulted toon band behavior to stable setup for point/spot lights:
  - intensity does not shift bands by default
  - additional lights use `Max` accumulation by default (instead of additive blowout)
- Added `Convert Palette To Project Space` toggle on `PaletteToonController` for color-space control.
- Updated `PaletteToonControllerEditor` palette preview to use the same project-space conversion path as runtime, reducing Inspector-vs-Game color mismatch.
- Updated bundled material preset defaults for the new shader properties.

## 1.1.2

- Added URP active-pipeline validation in `Tools > Palette Toon > Apply To Selected Renderers`.
- Added auto-repair/update behavior for `Assets/Materials/PaletteToonRamp.mat` in `Create Local Material Preset`.
- Prevented invalid local materials from being preferred over valid package material.

## 1.1.1

- Added missing `.meta` files for package files and folders.
- Fixed Unity import errors in immutable `Packages/com.rajunior.palette-toon` installations.

## 1.1.0

- Bundled `Runtime/Materials/PaletteToonRamp.mat` as a ready-to-use material.
- Bundled `Runtime/Palettes/ENDESGA-64-1x.png` as default starter palette.
- Added `Tools/Palette Toon/Create Local Material Preset`.
- Added `Tools/Palette Toon/Apply To Selected Renderers` for one-click setup.

## 1.0.0

- Initial release.
- Palette toon URP shader with main/additional lights.
- Shadow support for directional, point and spot lights.
- Palette controller and custom inspector.
- Palette texture import postprocessor.
