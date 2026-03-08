using System.IO;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PaletteToonController))]
public class PaletteToonControllerEditor : Editor
{
    private enum ActiveSlot
    {
        Shadow,
        Base,
        Highlight
    }

    private SerializedProperty _targetRenderer;
    private SerializedProperty _materialIndex;
    private SerializedProperty _paletteTexture;
    private SerializedProperty _shadowColorIndex;
    private SerializedProperty _baseColorIndex;
    private SerializedProperty _highlightColorIndex;
    private SerializedProperty _darkBandPercentage;
    private SerializedProperty _baseBandPercentage;
    private SerializedProperty _highlightBandPercentage;
    private SerializedProperty _shadowThreshold;
    private SerializedProperty _highlightThreshold;
    private SerializedProperty _baseTint;
    private SerializedProperty _convertPaletteToProjectColorSpace;
    private SerializedProperty _intensityAffectsBands;
    private SerializedProperty _enableOutline;
    private SerializedProperty _outlineWidth;
    private SerializedProperty _outlineColor;
    private SerializedProperty _bandAccumulation;
    private SerializedProperty _applyFog;

    private ActiveSlot? _activeSlot = null;
    private bool _autoAdvancing = false;
    private bool _showAdvanced = false;

    private Texture2D _cachedTexture;
    private Color[] _cachedColors;
    private int _cachedWidth;
    private int _cachedHeight;
    private bool _cachedConvertToProjectColorSpace;

    private const string AdvancedFoldoutKey = "PaletteToon_ShowAdvanced";

    private void OnEnable()
    {
        _targetRenderer = serializedObject.FindProperty("targetRenderer");
        _materialIndex = serializedObject.FindProperty("materialIndex");
        _paletteTexture = serializedObject.FindProperty("paletteTexture");
        _shadowColorIndex = serializedObject.FindProperty("shadowColorIndex");
        _baseColorIndex = serializedObject.FindProperty("baseColorIndex");
        _highlightColorIndex = serializedObject.FindProperty("highlightColorIndex");
        _darkBandPercentage = serializedObject.FindProperty("darkBandPercentage");
        _baseBandPercentage = serializedObject.FindProperty("baseBandPercentage");
        _highlightBandPercentage = serializedObject.FindProperty("highlightBandPercentage");
        _shadowThreshold = serializedObject.FindProperty("shadowThreshold");
        _highlightThreshold = serializedObject.FindProperty("highlightThreshold");
        _baseTint = serializedObject.FindProperty("baseTint");
        _convertPaletteToProjectColorSpace = serializedObject.FindProperty("convertPaletteToProjectColorSpace");
        _intensityAffectsBands = serializedObject.FindProperty("intensityAffectsBands");
        _enableOutline = serializedObject.FindProperty("enableOutline");
        _outlineWidth = serializedObject.FindProperty("outlineWidth");
        _outlineColor = serializedObject.FindProperty("outlineColor");
        _bandAccumulation = serializedObject.FindProperty("bandAccumulation");
        _applyFog = serializedObject.FindProperty("applyFog");

        _showAdvanced = SessionState.GetBool(AdvancedFoldoutKey, false);
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // ── Section A: Setup ──
        EditorGUILayout.PropertyField(_targetRenderer);

        PaletteToonController ctrl = (PaletteToonController)target;

        // Material Slot with name hint
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(_materialIndex, new GUIContent("Material Slot"));
        if (ctrl.targetRenderer != null)
        {
            Material[] mats = ctrl.targetRenderer.sharedMaterials;
            int idx = _materialIndex.intValue;
            if (idx >= 0 && idx < mats.Length && mats[idx] != null)
            {
                GUIStyle hintStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    fontStyle = FontStyle.Italic
                };
                EditorGUILayout.LabelField(mats[idx].name, hintStyle, GUILayout.Width(120f));
            }
            else if (idx >= mats.Length)
            {
                EditorGUILayout.LabelField("(out of range)", EditorStyles.miniLabel, GUILayout.Width(120f));
            }

            // Clamp to valid range
            _materialIndex.intValue = Mathf.Clamp(_materialIndex.intValue, 0, Mathf.Max(0, mats.Length - 1));
        }
        EditorGUILayout.EndHorizontal();

        // Warn if another controller targets the same slot
        if (ctrl.targetRenderer != null)
        {
            PaletteToonController[] siblings = ctrl.GetComponents<PaletteToonController>();
            foreach (PaletteToonController sibling in siblings)
            {
                if (sibling != ctrl &&
                    sibling.targetRenderer == ctrl.targetRenderer &&
                    sibling.materialIndex == _materialIndex.intValue)
                {
                    EditorGUILayout.HelpBox(
                        $"Another PaletteToonController is also targeting material slot {_materialIndex.intValue}.",
                        MessageType.Warning);
                    break;
                }
            }
        }

        EditorGUILayout.PropertyField(_paletteTexture);

        // ── Section B: Palette Grid (primary interaction) ──
        DrawPickingStateLabel();
        DrawPaletteGrid();

        // ── Section C: Toon Colors + Preview ──
        ClampIndexesByPaletteSize();

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Toon Colors", EditorStyles.boldLabel);
        DrawSlotRow("Shadow", _shadowColorIndex, ActiveSlot.Shadow);
        DrawSlotRow("Base", _baseColorIndex, ActiveSlot.Base);
        DrawSlotRow("Highlight", _highlightColorIndex, ActiveSlot.Highlight);
        DrawBandPreviewBar();

        // ── Section D: Band Balance ──
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Band Balance", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_darkBandPercentage, new GUIContent("Shadow Width"));
        EditorGUILayout.PropertyField(_baseBandPercentage, new GUIContent("Base Width"));
        EditorGUILayout.PropertyField(_highlightBandPercentage, new GUIContent("Highlight Width"));
        ClampBandPercentages();

        // ── Section E: Outline ──
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Outline", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_enableOutline, new GUIContent("Enable Outline"));
        if (_enableOutline.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(_outlineWidth, new GUIContent("Width"));
            EditorGUILayout.PropertyField(_outlineColor, new GUIContent("Color"));
            EditorGUI.indentLevel--;
        }

        // ── Section F: Advanced Settings (collapsed by default) ──
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

    private void DrawPickingStateLabel()
    {
        if (_cachedColors == null || _cachedColors.Length == 0)
            return;

        if (_activeSlot == null)
        {
            EditorGUILayout.HelpBox(
                "Click any color to assign: Shadow \u2192 Base \u2192 Highlight",
                MessageType.None);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "Picking: " + _activeSlot.Value,
                MessageType.Info);
        }
    }

    private void DrawSlotRow(string label, SerializedProperty indexProp, ActiveSlot slot)
    {
        bool isActive = _activeSlot == slot;

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
            _activeSlot = slot;
            _autoAdvancing = false;
        }
        else if (!clicked && isActive)
        {
            _activeSlot = null;
            _autoAdvancing = false;
        }

        EditorGUILayout.EndHorizontal();
    }

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

        Color shadowColor = GetPaletteColor(_shadowColorIndex.intValue);
        Color baseColor = GetPaletteColor(_baseColorIndex.intValue);
        Color hlColor = GetPaletteColor(_highlightColorIndex.intValue);

        EditorGUI.DrawRect(new Rect(barRect.x, barRect.y, darkW, barRect.height), shadowColor);
        EditorGUI.DrawRect(new Rect(barRect.x + darkW, barRect.y, midW, barRect.height), baseColor);
        EditorGUI.DrawRect(new Rect(barRect.x + darkW + midW, barRect.y, hiW, barRect.height), hlColor);

        // Separator lines
        EditorGUI.DrawRect(new Rect(barRect.x + darkW - 0.5f, barRect.y, 1f, barRect.height), Color.black);
        EditorGUI.DrawRect(new Rect(barRect.x + darkW + midW - 0.5f, barRect.y, 1f, barRect.height), Color.black);

        DrawOutline(barRect, new Color(0.2f, 0.2f, 0.2f));

        // Labels inside segments
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

        for (int row = 0; row < rows; row++)
        {
            EditorGUILayout.BeginHorizontal();
            for (int col = 0; col < columns; col++)
            {
                int index = (row * columns) + col;
                if (index >= _cachedColors.Length)
                {
                    break;
                }

                Rect rect = GUILayoutUtility.GetRect(18f, 18f, GUILayout.Width(18f), GUILayout.Height(18f));

                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(rect, _cachedColors[index]);

                    if (index == _shadowColorIndex.intValue)
                    {
                        DrawOutline(rect, new Color(0.25f, 0.25f, 0.25f));
                    }

                    if (index == _baseColorIndex.intValue)
                    {
                        DrawOutline(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f), Color.white);
                    }

                    if (index == _highlightColorIndex.intValue)
                    {
                        DrawOutline(new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, rect.height - 4f), Color.yellow);
                    }
                }

                if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                {
                    AssignToActiveSlot(index);
                }
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    private void AssignToActiveSlot(int index)
    {
        if (_activeSlot == null)
        {
            _activeSlot = ActiveSlot.Shadow;
            _autoAdvancing = true;
        }

        switch (_activeSlot.Value)
        {
            case ActiveSlot.Shadow:
                _shadowColorIndex.intValue = index;
                _activeSlot = _autoAdvancing ? ActiveSlot.Base : (ActiveSlot?)null;
                break;
            case ActiveSlot.Base:
                _baseColorIndex.intValue = index;
                _activeSlot = _autoAdvancing ? ActiveSlot.Highlight : (ActiveSlot?)null;
                break;
            case ActiveSlot.Highlight:
                _highlightColorIndex.intValue = index;
                _activeSlot = null;
                _autoAdvancing = false;
                break;
        }
    }

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

    private void ClampIndexesByPaletteSize()
    {
        int maxIndex = Mathf.Max(GetPaletteColorCount() - 1, 0);
        _shadowColorIndex.intValue = Mathf.Clamp(_shadowColorIndex.intValue, 0, maxIndex);
        _baseColorIndex.intValue = Mathf.Clamp(_baseColorIndex.intValue, 0, maxIndex);
        _highlightColorIndex.intValue = Mathf.Clamp(_highlightColorIndex.intValue, 0, maxIndex);
    }

    private int GetPaletteColorCount()
    {
        Texture2D texture = _paletteTexture.objectReferenceValue as Texture2D;
        if (texture == null)
        {
            return 1;
        }

        return Mathf.Max(texture.width * texture.height, 1);
    }

    private Color GetPaletteColor(int index)
    {
        if (_cachedColors == null || _cachedColors.Length == 0)
        {
            return Color.black;
        }

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
        {
            return;
        }

        string path = AssetDatabase.GetAssetPath(texture);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

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
        {
            return null;
        }

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
