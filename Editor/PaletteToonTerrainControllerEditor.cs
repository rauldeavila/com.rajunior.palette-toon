using System.IO;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PaletteToonTerrainController))]
public class PaletteToonTerrainControllerEditor : Editor
{
    private enum ActiveBand
    {
        Shadow,
        Base,
        Highlight
    }

    private SerializedProperty _targetTerrain;
    private SerializedProperty _paletteTexture;
    private SerializedProperty _usePaletteRemap;
    private SerializedProperty _paletteRampTexture;
    private SerializedProperty _layers;
    private SerializedProperty _darkBandPercentage;
    private SerializedProperty _baseBandPercentage;
    private SerializedProperty _highlightBandPercentage;
    private SerializedProperty _shadowThreshold;
    private SerializedProperty _highlightThreshold;
    private SerializedProperty _baseTint;
    private SerializedProperty _convertPaletteToProjectColorSpace;
    private SerializedProperty _intensityAffectsBands;
    private SerializedProperty _bandAccumulation;
    private SerializedProperty _applyFog;

    private int? _activeLayer = null;
    private ActiveBand? _activeBand = null;
    private bool _autoAdvancing = false;
    private bool _showAdvanced = false;
    private bool[] _layerFoldouts = new bool[PaletteToonTerrainController.MaxLayers];

    private Texture2D _cachedTexture;
    private Color[] _cachedColors;
    private int _cachedWidth;
    private int _cachedHeight;
    private bool _cachedConvertToProjectColorSpace;

    private const string AdvancedFoldoutKey = "PaletteToonTerrain_ShowAdvanced";
    private const string LayerFoldoutKeyPrefix = "PaletteToonTerrain_Layer";

    private void OnEnable()
    {
        _targetTerrain = serializedObject.FindProperty("targetTerrain");
        _paletteTexture = serializedObject.FindProperty("paletteTexture");
        _usePaletteRemap = serializedObject.FindProperty("usePaletteRemap");
        _paletteRampTexture = serializedObject.FindProperty("paletteRampTexture");
        _layers = serializedObject.FindProperty("layers");
        _darkBandPercentage = serializedObject.FindProperty("darkBandPercentage");
        _baseBandPercentage = serializedObject.FindProperty("baseBandPercentage");
        _highlightBandPercentage = serializedObject.FindProperty("highlightBandPercentage");
        _shadowThreshold = serializedObject.FindProperty("shadowThreshold");
        _highlightThreshold = serializedObject.FindProperty("highlightThreshold");
        _baseTint = serializedObject.FindProperty("baseTint");
        _convertPaletteToProjectColorSpace = serializedObject.FindProperty("convertPaletteToProjectColorSpace");
        _intensityAffectsBands = serializedObject.FindProperty("intensityAffectsBands");
        _bandAccumulation = serializedObject.FindProperty("bandAccumulation");
        _applyFog = serializedObject.FindProperty("applyFog");

        _showAdvanced = SessionState.GetBool(AdvancedFoldoutKey, false);
        for (int i = 0; i < PaletteToonTerrainController.MaxLayers; i++)
            _layerFoldouts[i] = SessionState.GetBool(LayerFoldoutKeyPrefix + i, i == 0);
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        PaletteToonTerrainController ctrl = (PaletteToonTerrainController)target;

        // ── Setup ──
        EditorGUILayout.PropertyField(_targetTerrain);
        EditorGUILayout.PropertyField(_paletteTexture);

        // Warn if terrain has more than 4 layers
        if (ctrl.targetTerrain != null && ctrl.targetTerrain.terrainData != null)
        {
            int alphamapCount = ctrl.targetTerrain.terrainData.alphamapTextureCount;
            if (alphamapCount > 1)
            {
                EditorGUILayout.HelpBox(
                    $"This terrain has {ctrl.targetTerrain.terrainData.terrainLayers.Length} layers. " +
                    "Only the first 4 layers are supported by the toon terrain shader.",
                    MessageType.Warning);
            }
        }

        // ── Palette Remap ──
        EditorGUILayout.Space(6f);
        EditorGUILayout.PropertyField(_usePaletteRemap,
            new GUIContent("Use Palette Remap",
                "Sample terrain layer textures and automatically remap each pixel to its " +
                "shadow/base/highlight palette color based on lighting."));

        bool isRemapMode = _usePaletteRemap.boolValue;

        if (isRemapMode)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_paletteRampTexture,
                new GUIContent("Palette Ramp (3×N)",
                    "The 3-column palette (shadow/base/highlight per row). " +
                    "If empty, uses the main Palette Texture."));

            Texture2D ramp = (_paletteRampTexture.objectReferenceValue as Texture2D)
                          ?? (_paletteTexture.objectReferenceValue as Texture2D);
            if (ramp != null && ramp.width != 3)
            {
                EditorGUILayout.HelpBox(
                    $"Palette ramp must be 3 columns wide (shadow/base/highlight). " +
                    $"Current texture is {ramp.width}×{ramp.height}.",
                    MessageType.Warning);
            }

            // Validate palette ramp import settings
            if (ramp != null)
            {
                string rampPath = AssetDatabase.GetAssetPath(ramp);
                if (!string.IsNullOrEmpty(rampPath))
                {
                    TextureImporter importer = AssetImporter.GetAtPath(rampPath) as TextureImporter;
                    if (importer != null)
                    {
                        System.Collections.Generic.List<string> issues = new();
                        if (!importer.sRGBTexture)
                            issues.Add("sRGB must be ON (palette colors are sRGB)");
                        if (importer.filterMode != FilterMode.Point)
                            issues.Add("Filter Mode should be Point (no blending between palette cells)");
                        if (importer.textureCompression != TextureImporterCompression.Uncompressed)
                            issues.Add("Compression should be None (avoid color shifting)");
                        if (issues.Count > 0)
                        {
                            EditorGUILayout.HelpBox(
                                "Palette ramp import settings:\n• " + string.Join("\n• ", issues),
                                MessageType.Warning);
                        }
                    }
                }
            }

            EditorGUILayout.HelpBox(
                "Terrain layer textures will be sampled. Each pixel color is matched to the " +
                "nearest palette color and remapped to shadow/base/highlight automatically.",
                MessageType.Info);
            EditorGUI.indentLevel--;
        }
        else
        {
            // ── Flat Color Mode: Palette Grid + Layer Sections ──
            DrawPickingStateLabel();
            DrawPaletteGrid();

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Layer Colors", EditorStyles.boldLabel);

            int layerCount = PaletteToonTerrainController.MaxLayers;
            for (int i = 0; i < layerCount; i++)
            {
                string layerName = GetTerrainLayerName(ctrl.targetTerrain, i);
                string label = string.IsNullOrEmpty(layerName)
                    ? $"Layer {i}"
                    : $"Layer {i} ({layerName})";

                bool newFoldout = EditorGUILayout.Foldout(_layerFoldouts[i], label, true);
                if (newFoldout != _layerFoldouts[i])
                {
                    _layerFoldouts[i] = newFoldout;
                    SessionState.SetBool(LayerFoldoutKeyPrefix + i, newFoldout);
                }

                if (_layerFoldouts[i])
                {
                    EditorGUI.indentLevel++;
                    SerializedProperty layer = _layers.GetArrayElementAtIndex(i);
                    DrawSlotRow("Shadow",    layer.FindPropertyRelative("shadowColorIndex"),    i, ActiveBand.Shadow);
                    DrawSlotRow("Base",      layer.FindPropertyRelative("baseColorIndex"),      i, ActiveBand.Base);
                    DrawSlotRow("Highlight", layer.FindPropertyRelative("highlightColorIndex"), i, ActiveBand.Highlight);
                    EditorGUI.indentLevel--;
                }
            }

            // Band Preview Bar only in flat color mode
            DrawBandPreviewBar();
        }

        // ── Band Balance ──
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Band Balance", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_darkBandPercentage, new GUIContent("Shadow Width"));
        EditorGUILayout.PropertyField(_baseBandPercentage, new GUIContent("Base Width"));
        EditorGUILayout.PropertyField(_highlightBandPercentage, new GUIContent("Highlight Width"));
        ClampBandPercentages();

        // ── Advanced Settings ──
        EditorGUILayout.Space(6f);
        bool newAdvanced = EditorGUILayout.Foldout(_showAdvanced, "Advanced Settings", true);
        if (newAdvanced != _showAdvanced)
        {
            _showAdvanced = newAdvanced;
            SessionState.SetBool(AdvancedFoldoutKey, _showAdvanced);
        }

        if (_showAdvanced)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_baseTint, new GUIContent("Base Tint"));
            EditorGUILayout.PropertyField(_convertPaletteToProjectColorSpace,
                new GUIContent("Convert to Project Color Space",
                    "Converts sRGB palette colors to Linear when project uses Linear rendering."));
            EditorGUILayout.PropertyField(_intensityAffectsBands,
                new GUIContent("Intensity Affects Bands",
                    "Blend light intensity into band calculation."));
            EditorGUILayout.PropertyField(_bandAccumulation,
                new GUIContent("Band Accumulation",
                    "How multiple lights combine: Add sums contributions, Max takes the brightest."));
            EditorGUILayout.PropertyField(_applyFog, new GUIContent("Apply Fog"));
            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();

        ctrl.Apply();
    }

    // ── picking state ──

    private void DrawPickingStateLabel()
    {
        if (_cachedColors == null || _cachedColors.Length == 0)
            return;

        if (_activeLayer == null)
        {
            EditorGUILayout.HelpBox(
                "Click any color to assign: Shadow \u2192 Base \u2192 Highlight for the active layer",
                MessageType.None);
        }
        else
        {
            EditorGUILayout.HelpBox(
                $"Picking: Layer {_activeLayer.Value} - {_activeBand.Value}",
                MessageType.Info);
        }
    }

    // ── slot row (per-layer) ──

    private void DrawSlotRow(string label, SerializedProperty indexProp, int layerIndex, ActiveBand band)
    {
        bool isActive = _activeLayer == layerIndex && _activeBand == band;

        Rect rowRect = EditorGUILayout.BeginHorizontal();
        if (isActive && Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(rowRect, new Color(0.3f, 0.5f, 0.8f, 0.15f));
        }

        GUILayout.Label(label, GUILayout.Width(60f));

        Color color = GetPaletteColor(indexProp.intValue);
        Rect swatchRect = GUILayoutUtility.GetRect(24f, 16f, GUILayout.Width(24f), GUILayout.Height(16f));
        if (Event.current.type == EventType.Repaint)
        {
            EditorGUI.DrawRect(swatchRect, color);
            DrawOutline(swatchRect, Color.black);
        }

        EditorGUI.BeginChangeCheck();
        int newIndex = EditorGUILayout.DelayedIntField(indexProp.intValue, GUILayout.Width(36f));
        if (EditorGUI.EndChangeCheck())
        {
            indexProp.intValue = Mathf.Max(0, newIndex);
        }

        string buttonLabel = isActive ? ">> Picking" : "Pick";
        bool clicked = GUILayout.Toggle(isActive, buttonLabel, "Button", GUILayout.Width(74f));
        if (clicked && !isActive)
        {
            _activeLayer = layerIndex;
            _activeBand = band;
            _autoAdvancing = false;
        }
        else if (!clicked && isActive)
        {
            _activeLayer = null;
            _activeBand = null;
            _autoAdvancing = false;
        }

        EditorGUILayout.EndHorizontal();
    }

    // ── palette grid ──

    private void DrawPaletteGrid()
    {
        Texture2D palette = _paletteTexture.objectReferenceValue as Texture2D;
        RefreshPaletteCache(palette);

        if (_cachedColors == null || _cachedColors.Length == 0)
        {
            EditorGUILayout.HelpBox("Assign a palette .png to pick colors visually.", MessageType.Info);
            return;
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Palette", EditorStyles.boldLabel);

        int columns = Mathf.Clamp(_cachedWidth, 1, 32);
        int rows = Mathf.CeilToInt((float)_cachedColors.Length / columns);

        // Gather currently selected indices for visual feedback
        int[] shadowIndices = new int[PaletteToonTerrainController.MaxLayers];
        int[] baseIndices = new int[PaletteToonTerrainController.MaxLayers];
        int[] highlightIndices = new int[PaletteToonTerrainController.MaxLayers];
        for (int i = 0; i < PaletteToonTerrainController.MaxLayers; i++)
        {
            SerializedProperty layer = _layers.GetArrayElementAtIndex(i);
            shadowIndices[i] = layer.FindPropertyRelative("shadowColorIndex").intValue;
            baseIndices[i] = layer.FindPropertyRelative("baseColorIndex").intValue;
            highlightIndices[i] = layer.FindPropertyRelative("highlightColorIndex").intValue;
        }

        for (int row = 0; row < rows; row++)
        {
            EditorGUILayout.BeginHorizontal();
            for (int col = 0; col < columns; col++)
            {
                int index = (row * columns) + col;
                if (index >= _cachedColors.Length)
                    break;

                Rect rect = GUILayoutUtility.GetRect(18f, 18f, GUILayout.Width(18f), GUILayout.Height(18f));

                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(rect, _cachedColors[index]);

                    // Highlight if used by the active layer (or layer 0 if none active)
                    int displayLayer = _activeLayer ?? 0;
                    if (index == shadowIndices[displayLayer])
                        DrawOutline(rect, new Color(0.25f, 0.25f, 0.25f));
                    if (index == baseIndices[displayLayer])
                        DrawOutline(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f), Color.white);
                    if (index == highlightIndices[displayLayer])
                        DrawOutline(new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f), Color.yellow);
                }

                if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                {
                    AssignToActiveSlot(index);
                }
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    private void AssignToActiveSlot(int colorIndex)
    {
        if (_activeLayer == null)
        {
            // Find the first expanded layer, or default to 0
            int startLayer = 0;
            for (int i = 0; i < PaletteToonTerrainController.MaxLayers; i++)
            {
                if (_layerFoldouts[i]) { startLayer = i; break; }
            }
            _activeLayer = startLayer;
            _activeBand = ActiveBand.Shadow;
            _autoAdvancing = true;
        }

        SerializedProperty layer = _layers.GetArrayElementAtIndex(_activeLayer.Value);

        switch (_activeBand.Value)
        {
            case ActiveBand.Shadow:
                layer.FindPropertyRelative("shadowColorIndex").intValue = colorIndex;
                _activeBand = _autoAdvancing ? ActiveBand.Base : (ActiveBand?)null;
                if (_activeBand == null) _activeLayer = null;
                break;
            case ActiveBand.Base:
                layer.FindPropertyRelative("baseColorIndex").intValue = colorIndex;
                _activeBand = _autoAdvancing ? ActiveBand.Highlight : (ActiveBand?)null;
                if (_activeBand == null) _activeLayer = null;
                break;
            case ActiveBand.Highlight:
                layer.FindPropertyRelative("highlightColorIndex").intValue = colorIndex;
                _activeBand = null;
                _activeLayer = null;
                _autoAdvancing = false;
                break;
        }
    }

    // ── band preview bar ──

    private void DrawBandPreviewBar()
    {
        EditorGUILayout.Space(4f);

        Rect barRect = GUILayoutUtility.GetRect(0f, 24f, GUILayout.ExpandWidth(true), GUILayout.Height(24f));

        if (Event.current.type != EventType.Repaint)
            return;

        float dark = _darkBandPercentage.floatValue;
        float mid = _baseBandPercentage.floatValue;
        float hi = _highlightBandPercentage.floatValue;
        float total = dark + mid + hi;
        if (total < 0.001f) total = 1f;

        float darkW = (dark / total) * barRect.width;
        float midW = (mid / total) * barRect.width;
        float hiW = (hi / total) * barRect.width;

        // Use layer 0 colors for preview
        SerializedProperty layer0 = _layers.GetArrayElementAtIndex(0);
        Color shadowColor = GetPaletteColor(layer0.FindPropertyRelative("shadowColorIndex").intValue);
        Color baseColor = GetPaletteColor(layer0.FindPropertyRelative("baseColorIndex").intValue);
        Color hlColor = GetPaletteColor(layer0.FindPropertyRelative("highlightColorIndex").intValue);

        EditorGUI.DrawRect(new Rect(barRect.x, barRect.y, darkW, barRect.height), shadowColor);
        EditorGUI.DrawRect(new Rect(barRect.x + darkW, barRect.y, midW, barRect.height), baseColor);
        EditorGUI.DrawRect(new Rect(barRect.x + darkW + midW, barRect.y, hiW, barRect.height), hlColor);

        EditorGUI.DrawRect(new Rect(barRect.x + darkW - 0.5f, barRect.y, 1f, barRect.height), Color.black);
        EditorGUI.DrawRect(new Rect(barRect.x + darkW + midW - 0.5f, barRect.y, 1f, barRect.height), Color.black);

        DrawOutline(barRect, new Color(0.2f, 0.2f, 0.2f));

        GUIStyle centeredLabel = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter
        };

        if (darkW > 40f)
        {
            centeredLabel.normal.textColor = GetContrastTextColor(shadowColor);
            GUI.Label(new Rect(barRect.x, barRect.y, darkW, barRect.height), "Shadow", centeredLabel);
        }
        if (midW > 30f)
        {
            centeredLabel.normal.textColor = GetContrastTextColor(baseColor);
            GUI.Label(new Rect(barRect.x + darkW, barRect.y, midW, barRect.height), "Base", centeredLabel);
        }
        if (hiW > 30f)
        {
            centeredLabel.normal.textColor = GetContrastTextColor(hlColor);
            GUI.Label(new Rect(barRect.x + darkW + midW, barRect.y, hiW, barRect.height), "Highlight", centeredLabel);
        }
    }

    // ── band normalization ──

    private void ClampBandPercentages()
    {
        _darkBandPercentage.floatValue = Mathf.Clamp01(_darkBandPercentage.floatValue);
        _baseBandPercentage.floatValue = Mathf.Clamp01(_baseBandPercentage.floatValue);
        _highlightBandPercentage.floatValue = Mathf.Clamp01(_highlightBandPercentage.floatValue);

        float total = _darkBandPercentage.floatValue + _baseBandPercentage.floatValue + _highlightBandPercentage.floatValue;
        if (total <= 0.0001f)
        {
            _darkBandPercentage.floatValue = 0.35f;
            _baseBandPercentage.floatValue = 0.40f;
            _highlightBandPercentage.floatValue = 0.25f;
            total = 1f;
        }

        float darkNorm = _darkBandPercentage.floatValue / total;
        float baseNorm = _baseBandPercentage.floatValue / total;
        _shadowThreshold.floatValue = Mathf.Clamp01(darkNorm);
        _highlightThreshold.floatValue = Mathf.Clamp01(darkNorm + baseNorm);
    }

    // ── helpers ──

    private static string GetTerrainLayerName(Terrain terrain, int layerIndex)
    {
        if (terrain == null || terrain.terrainData == null)
            return null;

        TerrainLayer[] terrainLayers = terrain.terrainData.terrainLayers;
        if (terrainLayers == null || layerIndex >= terrainLayers.Length || terrainLayers[layerIndex] == null)
            return null;

        return terrainLayers[layerIndex].name;
    }

    private Color GetPaletteColor(int index)
    {
        if (_cachedColors == null || _cachedColors.Length == 0)
            return Color.black;

        int safeIndex = Mathf.Clamp(index, 0, _cachedColors.Length - 1);
        return _cachedColors[safeIndex];
    }

    private void RefreshPaletteCache(Texture2D texture)
    {
        bool convertToProjectSpace = _convertPaletteToProjectColorSpace != null &&
                                     _convertPaletteToProjectColorSpace.boolValue;

        if (texture == _cachedTexture &&
            _cachedColors != null &&
            _cachedConvertToProjectColorSpace == convertToProjectSpace)
        {
            return;
        }

        _cachedTexture = texture;
        _cachedColors = null;
        _cachedWidth = 0;
        _cachedHeight = 0;
        _cachedConvertToProjectColorSpace = convertToProjectSpace;

        if (texture == null)
            return;

        string path = AssetDatabase.GetAssetPath(texture);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        byte[] bytes = File.ReadAllBytes(path);
        Texture2D temp = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        if (!temp.LoadImage(bytes, false))
        {
            Object.DestroyImmediate(temp);
            return;
        }

        _cachedWidth = temp.width;
        _cachedHeight = temp.height;
        _cachedColors = ConvertPaletteToProjectSpace(temp.GetPixels32(), convertToProjectSpace);
        Object.DestroyImmediate(temp);
    }

    private static Color[] ConvertPaletteToProjectSpace(Color32[] source, bool convertToProjectSpace)
    {
        if (source == null || source.Length == 0)
            return null;

        Color[] converted = new Color[source.Length];
        bool linearProject = convertToProjectSpace && QualitySettings.activeColorSpace == ColorSpace.Linear;

        for (int i = 0; i < source.Length; i++)
        {
            Color c = source[i];
            converted[i] = linearProject ? c.linear : c;
        }

        return converted;
    }

    private static Color GetContrastTextColor(Color bg)
    {
        float lum = 0.299f * bg.r + 0.587f * bg.g + 0.114f * bg.b;
        return lum > 0.5f ? Color.black : Color.white;
    }

    private static void DrawOutline(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.yMin, 1f, rect.height), color);
    }
}
