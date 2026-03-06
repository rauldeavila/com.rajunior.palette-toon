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
    private SerializedProperty _paletteTexture;
    private SerializedProperty _shadowColorIndex;
    private SerializedProperty _baseColorIndex;
    private SerializedProperty _highlightColorIndex;
    private SerializedProperty _shadowThreshold;
    private SerializedProperty _highlightThreshold;
    private SerializedProperty _baseTint;

    private ActiveSlot? _activeSlot = null;
    private Texture2D _cachedTexture;
    private Color[] _cachedColors;
    private int _cachedWidth;
    private int _cachedHeight;

    private void OnEnable()
    {
        _targetRenderer = serializedObject.FindProperty("targetRenderer");
        _paletteTexture = serializedObject.FindProperty("paletteTexture");
        _shadowColorIndex = serializedObject.FindProperty("shadowColorIndex");
        _baseColorIndex = serializedObject.FindProperty("baseColorIndex");
        _highlightColorIndex = serializedObject.FindProperty("highlightColorIndex");
        _shadowThreshold = serializedObject.FindProperty("shadowThreshold");
        _highlightThreshold = serializedObject.FindProperty("highlightThreshold");
        _baseTint = serializedObject.FindProperty("baseTint");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(_targetRenderer);
        EditorGUILayout.PropertyField(_paletteTexture);
        EditorGUILayout.PropertyField(_baseTint);

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Ramp", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_shadowThreshold, new GUIContent("Shadow Threshold"));
        EditorGUILayout.PropertyField(_highlightThreshold, new GUIContent("Highlight Threshold"));

        ClampThresholds();
        ClampIndexesByPaletteSize();

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("3 Colors", EditorStyles.boldLabel);
        DrawSlotRow("Shadow", _shadowColorIndex, ActiveSlot.Shadow);
        DrawSlotRow("Base", _baseColorIndex, ActiveSlot.Base);
        DrawSlotRow("Highlight", _highlightColorIndex, ActiveSlot.Highlight);

        DrawPaletteGrid();

        serializedObject.ApplyModifiedProperties();

        PaletteToonController controller = (PaletteToonController)target;
        controller.Apply();
    }

    private void DrawSlotRow(string label, SerializedProperty indexProp, ActiveSlot slot)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(60f));

        Color color = GetPaletteColor(indexProp.intValue);
        Rect swatchRect = GUILayoutUtility.GetRect(24f, 16f, GUILayout.Width(24f), GUILayout.Height(16f));
        EditorGUI.DrawRect(swatchRect, color);
        DrawOutline(swatchRect, Color.black);

        EditorGUI.BeginChangeCheck();
        int newIndex = EditorGUILayout.DelayedIntField(indexProp.intValue, GUILayout.Width(64f));
        if (EditorGUI.EndChangeCheck())
        {
            indexProp.intValue = Mathf.Max(0, newIndex);
        }

        bool isActive = _activeSlot == slot;
        bool clicked = GUILayout.Toggle(isActive, isActive ? "Picking" : "Pick", "Button", GUILayout.Width(64f));
        if (clicked && !isActive)
        {
            _activeSlot = slot;
        }
        else if (!clicked && isActive)
        {
            _activeSlot = null;
        }

        EditorGUILayout.EndHorizontal();
    }

    private void DrawPaletteGrid()
    {
        Texture2D palette = _paletteTexture.objectReferenceValue as Texture2D;
        RefreshPaletteCache(palette);

        if (_cachedColors == null || _cachedColors.Length == 0)
        {
            EditorGUILayout.HelpBox("Selecione uma paleta .png para escolher as cores visualmente.", MessageType.Info);
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

                if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                {
                    AssignToActiveSlot(index);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.HelpBox("Clique na paleta para aplicar ao slot ativo (Shadow, Base ou Highlight).", MessageType.None);
    }

    private void AssignToActiveSlot(int index)
    {
        if (_activeSlot == null)
        {
            return;
        }

        switch (_activeSlot.Value)
        {
            case ActiveSlot.Shadow:
                _shadowColorIndex.intValue = index;
                break;
            case ActiveSlot.Base:
                _baseColorIndex.intValue = index;
                break;
            default:
                _highlightColorIndex.intValue = index;
                break;
        }

        _activeSlot = null;
    }

    private void ClampThresholds()
    {
        _shadowThreshold.floatValue = Mathf.Clamp01(_shadowThreshold.floatValue);
        _highlightThreshold.floatValue = Mathf.Clamp(_highlightThreshold.floatValue, _shadowThreshold.floatValue, 1f);
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
        if (texture == _cachedTexture && _cachedColors != null)
        {
            return;
        }

        _cachedTexture = texture;
        _cachedColors = null;
        _cachedWidth = 0;
        _cachedHeight = 0;

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
        _cachedColors = temp.GetPixels();
        Object.DestroyImmediate(temp);
    }

    private static void DrawOutline(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - 1f, rect.width, 1f), color);
        EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, 1f, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.yMin, 1f, rect.height), color);
    }
}
