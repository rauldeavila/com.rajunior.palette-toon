# Palette Toon URP

Toon shader + tooling for palette-driven rendering in URP.

## Included

- `Runtime/Shaders/PaletteToonRamp.shader`
- `Runtime/Materials/PaletteToonRamp.mat` (ready-to-use)
- `Runtime/Palettes/ENDESGA-64-1x.png` (bundled starter palette)
- `Runtime/PaletteToonController.cs`
- `Editor/PaletteToonControllerEditor.cs`
- `Editor/PaletteTexturePostprocessor.cs`
- `Editor/PaletteToonQuickSetup.cs` (one-click menu setup)

## Requirements

- Unity 6 (6000.x)
- URP 17.3.0+

## Quick Start

1. Select one or more objects with `Renderer`.
2. Run `Tools > Palette Toon > Apply To Selected Renderers`.
3. Pick `Shadow/Base/Highlight` indexes in the inspector.

Optional:

- Run `Tools > Palette Toon > Create Local Material Preset` to create `Assets/Materials/PaletteToonRamp.mat` in your project (instead of using the package material directly).

## Notes

- Point/spot/directional shadows are supported.
- For palette color fidelity, keep tonemapping disabled in your scene volume.
- The palette postprocessor enforces point filtering and uncompressed import under `/Palettes/`.
- `Palette Toon Controller` exposes range-percentage band controls:
  - `Dark Band %`, `Base Band %`, `Highlight Band %` (auto-normalized)
  - `Use Range % For Point/Spot` (default on)
  - `Intensity Affects Bands` (default off)
  - `Band Accumulation` (default `Max`)
  - `Apply Fog` (default off)
- For color-space matching in Linear projects, keep `Convert Palette To Project Space` enabled (default on).
