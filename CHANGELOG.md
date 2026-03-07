# Changelog

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
