using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class PaletteToonQuickSetup
{
    private const string PackageMaterialPath = "Packages/com.rajunior.palette-toon/Runtime/Materials/PaletteToonRamp.mat";
    private const string PackageTerrainMaterialPath = "Packages/com.rajunior.palette-toon/Runtime/Materials/PaletteToonRamp_Terrain.mat";
    private const string PackageGrassMaterialPath = "Packages/com.rajunior.palette-toon/Runtime/Materials/PaletteToonGrass.mat";
    private const string PackagePalettePath = "Packages/com.rajunior.palette-toon/Runtime/Palettes/ENDESGA-64-1x.png";
    private const string DefaultLocalMaterialPath = "Assets/Materials/PaletteToonRamp.mat";
    private const string DefaultLocalTerrainMaterialPath = "Assets/Materials/PaletteToonRamp_Terrain.mat";
    private const string DefaultLocalGrassMaterialPath = "Assets/Materials/PaletteToonGrass.mat";

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

            // Assign toon material to every slot
            Material[] mats = renderer.sharedMaterials;
            int slotCount = mats.Length;
            for (int m = 0; m < slotCount; m++)
                mats[m] = material;
            renderer.sharedMaterials = mats;
            EditorUtility.SetDirty(renderer);

            // Create one controller per material slot, reusing existing ones
            PaletteToonController[] existing = renderer.GetComponents<PaletteToonController>();

            for (int slotIdx = 0; slotIdx < slotCount; slotIdx++)
            {
                PaletteToonController controller = null;

                foreach (PaletteToonController ex in existing)
                {
                    if (ex.targetRenderer == renderer && ex.materialIndex == slotIdx)
                    {
                        controller = ex;
                        break;
                    }
                }

                if (controller == null)
                {
                    controller = Undo.AddComponent<PaletteToonController>(renderer.gameObject);
                }

                Undo.RecordObject(controller, "Palette Toon Configure Controller");
                controller.targetRenderer = renderer;
                controller.materialIndex = slotIdx;
                if (controller.paletteTexture == null)
                {
                    controller.paletteTexture = palette;
                }

                controller.Apply();
                EditorUtility.SetDirty(controller);
            }

            configured += slotCount;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"Palette Toon: configured {configured} renderer(s).\nMaterial: {AssetDatabase.GetAssetPath(material)}");
    }

    [MenuItem("Tools/Palette Toon/Apply To Selected Renderers", true)]
    private static bool ValidateApplyToSelectedRenderers()
    {
        return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
    }

    internal static Material GetPreferredMaterial()
    {
        Material local = AssetDatabase.LoadAssetAtPath<Material>(DefaultLocalMaterialPath);
        if (local != null && local.shader != null && local.shader.name == "Custom/PaletteToonRamp")
        {
            return local;
        }

        return AssetDatabase.LoadAssetAtPath<Material>(PackageMaterialPath);
    }

    internal static Renderer[] CollectSelectedRenderers()
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

    // ── Terrain ──

    [MenuItem("Tools/Palette Toon/Apply To Selected Terrains", priority = 2003)]
    private static void ApplyToSelectedTerrains()
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

        Material terrainMaterial = GetPreferredTerrainMaterial();
        if (terrainMaterial == null)
        {
            Debug.LogError("Palette Toon: no terrain material found. Reimport the package.");
            return;
        }

        Texture2D palette = AssetDatabase.LoadAssetAtPath<Texture2D>(PackagePalettePath);
        if (palette == null)
        {
            Debug.LogError("Palette Toon: package palette not found. Reimport the package.");
            return;
        }

        Terrain[] terrains = CollectSelectedTerrains();
        if (terrains.Length == 0)
        {
            Debug.LogWarning("Palette Toon: no Terrain found in current selection.");
            return;
        }

        int configured = 0;
        foreach (Terrain terrain in terrains)
        {
            if (terrain == null) continue;

            Undo.RecordObject(terrain, "Palette Toon Apply Terrain Material");
            terrain.materialTemplate = terrainMaterial;
            EditorUtility.SetDirty(terrain);

            PaletteToonTerrainController controller =
                terrain.GetComponent<PaletteToonTerrainController>();

            if (controller == null)
                controller = Undo.AddComponent<PaletteToonTerrainController>(terrain.gameObject);

            Undo.RecordObject(controller, "Palette Toon Configure Terrain Controller");
            controller.targetTerrain = terrain;
            if (controller.paletteTexture == null)
                controller.paletteTexture = palette;

            controller.Apply();
            EditorUtility.SetDirty(controller);
            configured++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"Palette Toon: configured {configured} terrain(s).\nMaterial: {AssetDatabase.GetAssetPath(terrainMaterial)}");
    }

    [MenuItem("Tools/Palette Toon/Apply To Selected Terrains", true)]
    private static bool ValidateApplyToSelectedTerrains()
    {
        return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
    }

    [MenuItem("Tools/Palette Toon/Create Local Terrain Material Preset", priority = 2005)]
    private static void CreateLocalTerrainMaterialPreset()
    {
        Material packageMaterial = AssetDatabase.LoadAssetAtPath<Material>(PackageTerrainMaterialPath);
        if (packageMaterial == null)
        {
            Debug.LogError("Palette Toon: package terrain material not found. Reimport the package.");
            return;
        }

        EnsureFolder("Assets/Materials");

        Material localMaterial = AssetDatabase.LoadAssetAtPath<Material>(DefaultLocalTerrainMaterialPath);
        if (localMaterial == null)
        {
            localMaterial = new Material(packageMaterial)
            {
                name = "PaletteToonRamp_Terrain"
            };
            AssetDatabase.CreateAsset(localMaterial, DefaultLocalTerrainMaterialPath);
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
        Debug.Log("Palette Toon: local terrain material is ready at Assets/Materials/PaletteToonRamp_Terrain.mat");
    }

    internal static Material GetPreferredTerrainMaterial()
    {
        Material local = AssetDatabase.LoadAssetAtPath<Material>(DefaultLocalTerrainMaterialPath);
        if (local != null && local.shader != null && local.shader.name == "Custom/PaletteToonRamp_Terrain")
        {
            return local;
        }

        return AssetDatabase.LoadAssetAtPath<Material>(PackageTerrainMaterialPath);
    }

    internal static Terrain[] CollectSelectedTerrains()
    {
        List<Terrain> result = new List<Terrain>();
        HashSet<Terrain> dedupe = new HashSet<Terrain>();

        foreach (GameObject selected in Selection.gameObjects)
        {
            if (selected == null) continue;

            Terrain[] terrains = selected.GetComponentsInChildren<Terrain>(true);
            for (int i = 0; i < terrains.Length; i++)
            {
                Terrain t = terrains[i];
                if (t != null && dedupe.Add(t))
                    result.Add(t);
            }
        }

        return result.ToArray();
    }

    internal static bool IsUrpActive()
    {
        RenderPipelineAsset current = GraphicsSettings.currentRenderPipeline;
        if (current == null)
        {
            return false;
        }

        return current.GetType().Name.Contains("UniversalRenderPipelineAsset");
    }

    // ── Grass ──

    [MenuItem("Tools/Palette Toon/Apply Grass To Selected Terrains", priority = 2004)]
    private static void ApplyGrassToSelectedTerrains()
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

        Material grassMaterial = GetPreferredGrassMaterial();
        if (grassMaterial == null)
        {
            Debug.LogError("Palette Toon: no grass material found. Create one with Tools > Palette Toon > Create Local Grass Material Preset.");
            return;
        }

        Texture2D palette = AssetDatabase.LoadAssetAtPath<Texture2D>(PackagePalettePath);
        if (palette == null)
        {
            Debug.LogError("Palette Toon: package palette not found. Reimport the package.");
            return;
        }

        Terrain[] terrains = CollectSelectedTerrains();
        if (terrains.Length == 0)
        {
            Debug.LogWarning("Palette Toon: no Terrain found in current selection.");
            return;
        }

        int configured = 0;
        foreach (Terrain terrain in terrains)
        {
            if (terrain == null) continue;

            PaletteToonGrassController controller =
                terrain.GetComponent<PaletteToonGrassController>();

            if (controller == null)
                controller = Undo.AddComponent<PaletteToonGrassController>(terrain.gameObject);

            Undo.RecordObject(controller, "Palette Toon Configure Grass Controller");
            controller.grassMaterial = grassMaterial;
            if (controller.paletteTexture == null)
                controller.paletteTexture = palette;

            controller.Apply();
            EditorUtility.SetDirty(controller);
            configured++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"Palette Toon: configured grass on {configured} terrain(s).\nMaterial: {AssetDatabase.GetAssetPath(grassMaterial)}");
    }

    [MenuItem("Tools/Palette Toon/Apply Grass To Selected Terrains", true)]
    private static bool ValidateApplyGrassToSelectedTerrains()
    {
        return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
    }

    [MenuItem("Tools/Palette Toon/Create Local Grass Material Preset", priority = 2006)]
    private static void CreateLocalGrassMaterialPreset()
    {
        Material packageMaterial = AssetDatabase.LoadAssetAtPath<Material>(PackageGrassMaterialPath);
        if (packageMaterial == null)
        {
            Debug.LogError("Palette Toon: package grass material not found. Reimport the package.");
            return;
        }

        EnsureFolder("Assets/Materials");

        Material localMaterial = AssetDatabase.LoadAssetAtPath<Material>(DefaultLocalGrassMaterialPath);
        if (localMaterial == null)
        {
            localMaterial = new Material(packageMaterial)
            {
                name = "PaletteToonGrass"
            };
            AssetDatabase.CreateAsset(localMaterial, DefaultLocalGrassMaterialPath);
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
        Debug.Log("Palette Toon: local grass material is ready at Assets/Materials/PaletteToonGrass.mat");
    }

    internal static Material GetPreferredGrassMaterial()
    {
        Material local = AssetDatabase.LoadAssetAtPath<Material>(DefaultLocalGrassMaterialPath);
        if (local != null && local.shader != null && local.shader.name == "Custom/PaletteToonGrass")
        {
            return local;
        }

        return AssetDatabase.LoadAssetAtPath<Material>(PackageGrassMaterialPath);
    }
}
