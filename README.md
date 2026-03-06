# Palette Toon URP

Toon shader + tooling for palette-driven rendering in URP.

## Included

- `Runtime/Shaders/PaletteToonRamp.shader`
- `Runtime/PaletteToonController.cs`
- `Editor/PaletteToonControllerEditor.cs`
- `Editor/PaletteTexturePostprocessor.cs`

## Requirements

- Unity 6 (6000.x)
- URP 17.3.0+

## Quick Start

1. Create a material using `Custom/PaletteToonRamp`.
2. Apply the material to a MeshRenderer.
3. Add `PaletteToonController` to the same GameObject.
4. Assign a 1px palette PNG in `Palette Texture`.
5. Pick `Shadow/Base/Highlight` indexes in the inspector.

## Notes

- Point/spot/directional shadows are supported.
- For palette color fidelity, keep tonemapping disabled in your scene volume.
- The palette postprocessor enforces point filtering and uncompressed import under `/Palettes/`.
