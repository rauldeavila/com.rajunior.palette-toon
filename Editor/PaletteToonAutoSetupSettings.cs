using UnityEditor;
using UnityEngine;

public class PaletteToonAutoSetupSettings : ScriptableObject
{
    private const string DefaultAssetPath = "Assets/Settings/PaletteToonAutoSetupSettings.asset";
    private const string PackagePalette3xNPath = "Packages/com.rajunior.palette-toon/Runtime/Palettes/ENDESGA-64-3xN.png";

    [Tooltip("Palette texture (must be 3 columns wide: shadow, base, highlight per row).")]
    public Texture2D paletteTexture;

    [Tooltip("Number of columns in the palette (shadow / base / highlight).")]
    [Min(3)] public int paletteColumns = 3;

    [Tooltip("Automatically configure PaletteToonControllers when .fbx models are imported.")]
    public bool autoMatchOnImport = true;

    [Tooltip("Maximum perceptual color distance (CIELAB Delta-E, 0-100) to accept a match. " +
             "Typical noticeable difference is around 2-5. Set higher for lenient matching.")]
    [Range(0f, 50f)] public float maxMatchDistance = 15f;

    [Tooltip("Palette row to use when no match is found within the distance threshold.")]
    [Min(0)] public int fallbackRow;

    private static PaletteToonAutoSetupSettings _instance;

    public static PaletteToonAutoSetupSettings GetOrCreateSettings()
    {
        if (_instance != null) return _instance;

        string[] guids = AssetDatabase.FindAssets("t:PaletteToonAutoSetupSettings");
        if (guids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            _instance = AssetDatabase.LoadAssetAtPath<PaletteToonAutoSetupSettings>(path);
            if (_instance != null) return _instance;
        }

        _instance = CreateInstance<PaletteToonAutoSetupSettings>();
        _instance.paletteTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(PackagePalette3xNPath);
        EnsureFolder("Assets/Settings");
        AssetDatabase.CreateAsset(_instance, DefaultAssetPath);
        AssetDatabase.SaveAssets();
        return _instance;
    }

    public static PaletteToonAutoSetupSettings FindExistingSettings()
    {
        if (_instance != null) return _instance;

        string[] guids = AssetDatabase.FindAssets("t:PaletteToonAutoSetupSettings");
        if (guids.Length == 0) return null;

        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        _instance = AssetDatabase.LoadAssetAtPath<PaletteToonAutoSetupSettings>(path);
        return _instance;
    }

    [SettingsProvider]
    public static SettingsProvider CreateSettingsProvider()
    {
        return new SettingsProvider("Project/Palette Toon/Auto Setup", SettingsScope.Project)
        {
            guiHandler = _ =>
            {
                var settings = GetOrCreateSettings();
                var so = new SerializedObject(settings);

                EditorGUILayout.PropertyField(so.FindProperty("paletteTexture"));
                EditorGUILayout.PropertyField(so.FindProperty("paletteColumns"));

                EditorGUILayout.Space();
                EditorGUILayout.PropertyField(so.FindProperty("autoMatchOnImport"),
                    new GUIContent("Auto Match On FBX Import"));

                EditorGUILayout.Space();
                EditorGUILayout.PropertyField(so.FindProperty("maxMatchDistance"));
                EditorGUILayout.PropertyField(so.FindProperty("fallbackRow"));

                so.ApplyModifiedProperties();
            },
            keywords = new[] { "Palette", "Toon", "Auto", "Setup", "FBX", "Import", "Match" }
        };
    }

    private static void EnsureFolder(string targetFolder)
    {
        string[] parts = targetFolder.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
