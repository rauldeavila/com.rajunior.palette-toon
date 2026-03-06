using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class PaletteToonQuickSetup
{
    private const string PackageMaterialPath = "Packages/com.rajunior.palette-toon/Runtime/Materials/PaletteToonRamp.mat";
    private const string PackagePalettePath = "Packages/com.rajunior.palette-toon/Runtime/Palettes/ENDESGA-64-1x.png";
    private const string DefaultLocalMaterialPath = "Assets/Materials/PaletteToonRamp.mat";

    [MenuItem("Tools/Palette Toon/Create Local Material Preset", priority = 2000)]
    private static void CreateLocalMaterialPreset()
    {
        Material packageMaterial = AssetDatabase.LoadAssetAtPath<Material>(PackageMaterialPath);
        if (packageMaterial == null)
        {
            Debug.LogError("Palette Toon: package material not found. Reimport the package.");
            return;
        }

        EnsureFolder("Assets/Materials");

        Material localMaterial = AssetDatabase.LoadAssetAtPath<Material>(DefaultLocalMaterialPath);
        if (localMaterial == null)
        {
            localMaterial = new Material(packageMaterial)
            {
                name = "PaletteToonRamp"
            };
            AssetDatabase.CreateAsset(localMaterial, DefaultLocalMaterialPath);
        }
        else
        {
            localMaterial.shader = packageMaterial.shader;
            localMaterial.CopyPropertiesFromMaterial(packageMaterial);
            EditorUtility.SetDirty(localMaterial);
        }

        AssetDatabase.SaveAssets();

        Selection.activeObject = localMaterial;
        EditorGUIUtility.PingObject(localMaterial);
        Debug.Log("Palette Toon: local material is ready at Assets/Materials/PaletteToonRamp.mat");
    }

    [MenuItem("Tools/Palette Toon/Apply To Selected Renderers", priority = 2001)]
    private static void ApplyToSelectedRenderers()
    {
        if (!IsUrpActive())
        {
            const string message = "Palette Toon requires URP as the active render pipeline.\n\n" +
                                   "Fix:\n" +
                                   "1) Install URP package\n" +
                                   "2) Create a URP Pipeline Asset\n" +
                                   "3) Assign it in Project Settings > Graphics and Project Settings > Quality";
            EditorUtility.DisplayDialog("Palette Toon - URP Required", message, "OK");
            Debug.LogError("Palette Toon: URP is not active. Assign a Universal Render Pipeline Asset in Graphics/Quality settings.");
            return;
        }

        Material material = GetPreferredMaterial();
        if (material == null)
        {
            Debug.LogError("Palette Toon: no material found. Create one with Tools/Palette Toon/Create Local Material Preset.");
            return;
        }

        Texture2D palette = AssetDatabase.LoadAssetAtPath<Texture2D>(PackagePalettePath);
        if (palette == null)
        {
            Debug.LogError("Palette Toon: package palette not found. Reimport the package.");
            return;
        }

        Renderer[] renderers = CollectSelectedRenderers();
        if (renderers.Length == 0)
        {
            Debug.LogWarning("Palette Toon: no Renderer found in current selection.");
            return;
        }

        int configured = 0;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            Undo.RecordObject(renderer, "Palette Toon Apply Material");
            renderer.sharedMaterial = material;
            EditorUtility.SetDirty(renderer);

            PaletteToonController controller = renderer.GetComponent<PaletteToonController>();
            if (controller == null)
            {
                controller = Undo.AddComponent<PaletteToonController>(renderer.gameObject);
            }

            Undo.RecordObject(controller, "Palette Toon Configure Controller");
            controller.targetRenderer = renderer;
            if (controller.paletteTexture == null)
            {
                controller.paletteTexture = palette;
            }

            controller.Apply();
            EditorUtility.SetDirty(controller);
            configured++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"Palette Toon: configured {configured} renderer(s).\nMaterial: {AssetDatabase.GetAssetPath(material)}");
    }

    [MenuItem("Tools/Palette Toon/Apply To Selected Renderers", true)]
    private static bool ValidateApplyToSelectedRenderers()
    {
        return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
    }

    private static Material GetPreferredMaterial()
    {
        Material local = AssetDatabase.LoadAssetAtPath<Material>(DefaultLocalMaterialPath);
        if (local != null && local.shader != null && local.shader.name == "Custom/PaletteToonRamp")
        {
            return local;
        }

        return AssetDatabase.LoadAssetAtPath<Material>(PackageMaterialPath);
    }

    private static Renderer[] CollectSelectedRenderers()
    {
        List<Renderer> result = new List<Renderer>();
        HashSet<Renderer> dedupe = new HashSet<Renderer>();

        foreach (GameObject selected in Selection.gameObjects)
        {
            if (selected == null)
            {
                continue;
            }

            Renderer[] renderers = selected.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !dedupe.Add(renderer))
                {
                    continue;
                }

                result.Add(renderer);
            }
        }

        return result.ToArray();
    }

    private static void EnsureFolder(string targetFolder)
    {
        string[] parts = targetFolder.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }

            current = next;
        }
    }

    private static bool IsUrpActive()
    {
        RenderPipelineAsset current = GraphicsSettings.currentRenderPipeline;
        if (current == null)
        {
            return false;
        }

        return current.GetType().Name.Contains("UniversalRenderPipelineAsset");
    }
}
