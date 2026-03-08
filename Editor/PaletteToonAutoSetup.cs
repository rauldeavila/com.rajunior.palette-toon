using System.IO;
using UnityEditor;
using UnityEngine;

public static class PaletteToonAutoSetup
{
    // ── Menu Item ──

    [MenuItem("Tools/Palette Toon/Auto Setup From Material Colors", priority = 2002)]
    private static void AutoSetupFromSelection()
    {
        if (!PaletteToonQuickSetup.IsUrpActive())
        {
            EditorUtility.DisplayDialog("Palette Toon - URP Required",
                "Palette Toon requires URP as the active render pipeline.", "OK");
            return;
        }

        var settings = PaletteToonAutoSetupSettings.GetOrCreateSettings();
        if (settings.paletteTexture == null)
        {
            EditorUtility.DisplayDialog("Palette Toon - No Palette",
                "Assign a palette texture in Project Settings > Palette Toon > Auto Setup.", "OK");
            return;
        }

        Material toonMaterial = PaletteToonQuickSetup.GetPreferredMaterial();
        if (toonMaterial == null)
        {
            Debug.LogError("Palette Toon: no material found. " +
                "Create one with Tools > Palette Toon > Create Local Material Preset.");
            return;
        }

        Renderer[] renderers = PaletteToonQuickSetup.CollectSelectedRenderers();
        if (renderers.Length == 0)
        {
            Debug.LogWarning("Palette Toon: no Renderer found in current selection.");
            return;
        }

        Color[] paletteColors = ReadPaletteColors(settings.paletteTexture);
        if (paletteColors == null || paletteColors.Length == 0)
        {
            Debug.LogError("Palette Toon: failed to read palette texture.");
            return;
        }

        if (!ValidatePaletteLayout(paletteColors.Length, settings.paletteColumns,
                settings.paletteTexture))
            return;

        int configured = 0;
        int warnings = 0;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;

            SetupResult result = SetupRenderer(renderer, toonMaterial,
                settings.paletteTexture, paletteColors, settings, useUndo: true);
            configured += result.configured;
            warnings += result.warnings;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"Palette Toon Auto Setup: configured {configured} material slot(s)" +
            (warnings > 0 ? $" ({warnings} used fallback row)." : "."));
    }

    [MenuItem("Tools/Palette Toon/Auto Setup From Material Colors", true)]
    private static bool ValidateAutoSetup()
    {
        return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
    }

    // ── Core Setup ──

    public struct SetupResult
    {
        public int configured;
        public int warnings;
    }

    public static SetupResult SetupRenderer(
        Renderer renderer,
        Material toonMaterial,
        Texture2D paletteTexture,
        Color[] paletteColors,
        PaletteToonAutoSetupSettings settings,
        bool useUndo)
    {
        var result = new SetupResult();
        Material[] originalMats = renderer.sharedMaterials;
        int slotCount = originalMats.Length;

        // Read original colors BEFORE replacing materials
        Color[] slotColors = new Color[slotCount];
        for (int i = 0; i < slotCount; i++)
            slotColors[i] = originalMats[i] != null ? GetMaterialBaseColor(originalMats[i]) : Color.white;

        // Replace all materials with toon material
        if (useUndo) Undo.RecordObject(renderer, "Palette Toon Auto Setup");
        Material[] newMats = new Material[slotCount];
        for (int m = 0; m < slotCount; m++)
            newMats[m] = toonMaterial;
        renderer.sharedMaterials = newMats;
        EditorUtility.SetDirty(renderer);

        // Create/configure controllers per slot
        PaletteToonController[] existing = renderer.GetComponents<PaletteToonController>();

        for (int slotIdx = 0; slotIdx < slotCount; slotIdx++)
        {
            PaletteToonController controller = FindControllerForSlot(existing, renderer, slotIdx);

            if (controller == null)
            {
                controller = useUndo
                    ? Undo.AddComponent<PaletteToonController>(renderer.gameObject)
                    : renderer.gameObject.AddComponent<PaletteToonController>();
            }

            if (useUndo) Undo.RecordObject(controller, "Palette Toon Auto Setup");

            controller.targetRenderer = renderer;
            controller.materialIndex = slotIdx;
            controller.paletteTexture = paletteTexture;

            // Match color to palette row
            int row = FindClosestPaletteRow(slotColors[slotIdx], paletteColors,
                settings.paletteColumns, settings.maxMatchDistance, out float distance);

            if (row < 0)
            {
                row = settings.fallbackRow;
                result.warnings++;
                Debug.LogWarning(
                    $"Palette Toon: no close match for slot {slotIdx} on " +
                    $"'{renderer.gameObject.name}' (color: {slotColors[slotIdx]}, " +
                    $"best distance: {distance:F1}). Using fallback row {row}.",
                    renderer);
            }

            int cols = settings.paletteColumns;
            controller.shadowColorIndex    = row * cols;
            controller.baseColorIndex      = row * cols + 1;
            controller.highlightColorIndex = row * cols + 2;

            controller.darkBandPercentage      = 0.3f;
            controller.baseBandPercentage      = 0.75f;
            controller.highlightBandPercentage = 1f;

            if (useUndo) controller.Apply();
            EditorUtility.SetDirty(controller);
            result.configured++;
        }

        return result;
    }

    // ── Color Matching (CIELAB Delta-E CIE76) ──

    public static int FindClosestPaletteRow(
        Color targetColor,
        Color[] paletteColors,
        int paletteColumns,
        float maxDistance,
        out float bestDistance)
    {
        Lab targetLab = RGBToLab(targetColor);
        int rowCount = paletteColors.Length / paletteColumns;
        int bestRow = -1;
        bestDistance = float.MaxValue;

        for (int row = 0; row < rowCount; row++)
        {
            int baseIndex = row * paletteColumns + 1; // column 1 = base
            if (baseIndex >= paletteColors.Length) break;

            Lab paletteLab = RGBToLab(paletteColors[baseIndex]);
            float dist = DeltaE(targetLab, paletteLab);

            if (dist < bestDistance)
            {
                bestDistance = dist;
                bestRow = row;
            }
        }

        if (bestDistance > maxDistance)
            return -1;

        return bestRow;
    }

    private struct Lab
    {
        public float L, a, b;
    }

    private static Lab RGBToLab(Color color)
    {
        // sRGB to linear
        float r = GammaToLinear(color.r);
        float g = GammaToLinear(color.g);
        float b = GammaToLinear(color.b);

        // Linear RGB to XYZ (D65 illuminant)
        float x = r * 0.4124564f + g * 0.3575761f + b * 0.1804375f;
        float y = r * 0.2126729f + g * 0.7151522f + b * 0.0721750f;
        float z = r * 0.0193339f + g * 0.1191920f + b * 0.9503041f;

        // Normalize to D65 white point
        x /= 0.95047f;
        z /= 1.08883f;

        x = LabF(x);
        y = LabF(y);
        z = LabF(z);

        return new Lab
        {
            L = 116f * y - 16f,
            a = 500f * (x - y),
            b = 200f * (y - z)
        };
    }

    private static float GammaToLinear(float c)
    {
        return c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
    }

    private static float LabF(float t)
    {
        const float delta = 6f / 29f;
        return t > delta * delta * delta
            ? Mathf.Pow(t, 1f / 3f)
            : t / (3f * delta * delta) + 4f / 29f;
    }

    private static float DeltaE(Lab a, Lab b)
    {
        float dL = a.L - b.L;
        float da = a.a - b.a;
        float db = a.b - b.b;
        return Mathf.Sqrt(dL * dL + da * da + db * db);
    }

    // ── Palette Reading ──

    public static Color[] ReadPaletteColors(Texture2D paletteTexture)
    {
        string path = AssetDatabase.GetAssetPath(paletteTexture);
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;

        byte[] bytes = File.ReadAllBytes(path);
        Texture2D tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        if (!tmp.LoadImage(bytes, false))
        {
            Object.DestroyImmediate(tmp);
            return null;
        }

        Color32[] raw = tmp.GetPixels32();
        Color[] colors = new Color[raw.Length];
        for (int i = 0; i < raw.Length; i++)
            colors[i] = (Color)raw[i];

        Object.DestroyImmediate(tmp);
        return colors;
    }

    // ── Helpers ──

    private static Color GetMaterialBaseColor(Material mat)
    {
        if (mat.HasProperty("_BaseColor"))
            return mat.GetColor("_BaseColor");
        if (mat.HasProperty("_Color"))
            return mat.GetColor("_Color");
        return mat.color;
    }

    private static PaletteToonController FindControllerForSlot(
        PaletteToonController[] controllers, Renderer renderer, int slotIdx)
    {
        foreach (PaletteToonController c in controllers)
        {
            if (c.targetRenderer == renderer && c.materialIndex == slotIdx)
                return c;
        }
        return null;
    }

    private static bool ValidatePaletteLayout(int totalColors, int columns, Texture2D texture)
    {
        if (columns < 3)
        {
            Debug.LogError(
                $"Palette Toon Auto Setup: palette columns must be >= 3, got {columns}. " +
                "Check Project Settings > Palette Toon > Auto Setup.");
            return false;
        }

        if (texture.width != columns)
        {
            Debug.LogWarning(
                $"Palette Toon Auto Setup: palette texture width ({texture.width}) " +
                $"does not match configured columns ({columns}).");
        }

        if (totalColors % columns != 0)
        {
            Debug.LogWarning(
                $"Palette Toon Auto Setup: palette has {totalColors} pixels " +
                $"not evenly divisible by {columns} columns. Last row may be incomplete.");
        }

        return true;
    }
}
