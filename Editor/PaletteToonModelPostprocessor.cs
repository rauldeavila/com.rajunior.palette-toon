using UnityEditor;
using UnityEngine;

public class PaletteToonModelPostprocessor : AssetPostprocessor
{
    private void OnPostprocessModel(GameObject root)
    {
        string ext = System.IO.Path.GetExtension(assetPath).ToLowerInvariant();
        if (ext != ".fbx" && ext != ".blend") return;

        // Cannot create assets during import — only look for existing settings
        var settings = PaletteToonAutoSetupSettings.FindExistingSettings();
        if (settings == null || !settings.autoMatchOnImport) return;

        if (settings.paletteTexture == null)
        {
            Debug.LogWarning("Palette Toon Auto Import: enabled but no palette texture assigned. " +
                "Configure in Project Settings > Palette Toon > Auto Setup.");
            return;
        }

        if (settings.paletteColumns < 3) return;

        Material toonMaterial = GetToonMaterial();
        if (toonMaterial == null)
        {
            Debug.LogWarning("Palette Toon Auto Import: no toon material found. " +
                "Run Tools > Palette Toon > Create Local Material Preset first.");
            return;
        }

        Color[] paletteColors = PaletteToonAutoSetup.ReadPaletteColors(settings.paletteTexture);
        if (paletteColors == null || paletteColors.Length == 0) return;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        int totalConfigured = 0;
        int totalWarnings = 0;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;

            var result = PaletteToonAutoSetup.SetupRenderer(
                renderer, toonMaterial, settings.paletteTexture,
                paletteColors, settings, useUndo: false);

            totalConfigured += result.configured;
            totalWarnings += result.warnings;
        }

        if (totalConfigured > 0)
        {
            Debug.Log($"Palette Toon Auto Import: configured {totalConfigured} material slot(s) " +
                $"on '{assetPath}'" +
                (totalWarnings > 0 ? $" ({totalWarnings} used fallback row)." : "."));
        }
    }

    private static Material GetToonMaterial()
    {
        const string localPath = "Assets/Materials/PaletteToonRamp.mat";
        const string packagePath = "Packages/com.rajunior.palette-toon/Runtime/Materials/PaletteToonRamp.mat";

        Material local = AssetDatabase.LoadAssetAtPath<Material>(localPath);
        if (local != null && local.shader != null && local.shader.name == "Custom/PaletteToonRamp")
            return local;

        return AssetDatabase.LoadAssetAtPath<Material>(packagePath);
    }
}
