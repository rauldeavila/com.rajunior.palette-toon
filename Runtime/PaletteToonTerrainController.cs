using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class PaletteToonTerrainController : MonoBehaviour
{
    private static readonly int BaseColorId      = Shader.PropertyToID("_BaseColor");
    private static readonly int Threshold1Id     = Shader.PropertyToID("_Threshold1");
    private static readonly int Threshold2Id     = Shader.PropertyToID("_Threshold2");
    private static readonly int IntensityAffectsBandsId = Shader.PropertyToID("_IntensityAffectsBands");
    private static readonly int BandAccumulationId      = Shader.PropertyToID("_BandAccumulation");
    private static readonly int ApplyFogId              = Shader.PropertyToID("_ApplyFog");

    private static readonly int[] ColorShadowIds =
    {
        Shader.PropertyToID("_ColorShadow_L0"),
        Shader.PropertyToID("_ColorShadow_L1"),
        Shader.PropertyToID("_ColorShadow_L2"),
        Shader.PropertyToID("_ColorShadow_L3"),
    };

    private static readonly int[] ColorBaseIds =
    {
        Shader.PropertyToID("_ColorBase_L0"),
        Shader.PropertyToID("_ColorBase_L1"),
        Shader.PropertyToID("_ColorBase_L2"),
        Shader.PropertyToID("_ColorBase_L3"),
    };

    private static readonly int[] ColorHighlightIds =
    {
        Shader.PropertyToID("_ColorHighlight_L0"),
        Shader.PropertyToID("_ColorHighlight_L1"),
        Shader.PropertyToID("_ColorHighlight_L2"),
        Shader.PropertyToID("_ColorHighlight_L3"),
    };

    public const int MaxLayers = 4;

    [System.Serializable]
    public class LayerColors
    {
        [Min(0)] public int shadowColorIndex    = 0;
        [Min(0)] public int baseColorIndex      = 1;
        [Min(0)] public int highlightColorIndex = 2;
    }

    public enum BandAccumulationMode
    {
        Add = 0,
        Max = 1
    }

    [Header("Setup")]
    public Terrain targetTerrain;
    public Texture2D paletteTexture;

    [Header("Layer Colors")]
    public LayerColors[] layers = new LayerColors[MaxLayers]
    {
        new LayerColors(),
        new LayerColors(),
        new LayerColors(),
        new LayerColors()
    };

    [Header("Band Balance")]
    [Range(0f, 1f)] public float darkBandPercentage = 0.3f;
    [Range(0f, 1f)] public float baseBandPercentage = 0.75f;
    [Range(0f, 1f)] public float highlightBandPercentage = 1f;

    [HideInInspector]
    [Range(0f, 1f)] public float shadowThreshold    = 0.35f;
    [HideInInspector]
    [Range(0f, 1f)] public float highlightThreshold = 0.75f;

    [Header("Tint")]
    public Color baseTint = Color.white;

    [Header("Advanced")]
    [Tooltip("Converts palette colors from sRGB to project color space.")]
    public bool convertPaletteToProjectColorSpace = false;
    [Range(0f, 1f)] public float intensityAffectsBands = 1f;
    public BandAccumulationMode bandAccumulation = BandAccumulationMode.Max;
    public bool applyFog = false;

    private Material _materialInstance;
    private Material _originalMaterialTemplate;
    private Terrain _instancedTerrain;

    private Texture2D _cachedPalette;
    private Color[] _cachedColors;
    private bool _cachedConvertToProjectColorSpace;

    private void Reset()
    {
        targetTerrain = GetComponent<Terrain>();
    }

    private void OnEnable()
    {
        Apply();
    }

    private void OnDisable()
    {
        ReleaseMaterialInstance();
    }

    private void OnDestroy()
    {
        ReleaseMaterialInstance();
    }

    private void OnValidate()
    {
        NormalizeBandPercentages(out shadowThreshold, out highlightThreshold);
        intensityAffectsBands = Mathf.Clamp01(intensityAffectsBands);
        Apply();
    }

    public void Apply()
    {
        if (targetTerrain == null)
        {
            targetTerrain = GetComponent<Terrain>();
            if (targetTerrain == null) return;
        }

        EnsureMaterialInstance();
        if (_materialInstance == null) return;

        try { RefreshPaletteCache(); }
        catch (System.Exception e)
        {
            Debug.LogWarning("PaletteToonTerrainController: failed to read palette — " + e.Message, this);
        }

        int maxIndex = GetMaxPaletteIndex();

        NormalizeBandPercentages(out shadowThreshold, out highlightThreshold);

        for (int i = 0; i < MaxLayers; i++)
        {
            LayerColors lc = layers[i];
            lc.shadowColorIndex    = Mathf.Clamp(lc.shadowColorIndex, 0, maxIndex);
            lc.baseColorIndex      = Mathf.Clamp(lc.baseColorIndex, 0, maxIndex);
            lc.highlightColorIndex = Mathf.Clamp(lc.highlightColorIndex, 0, maxIndex);

            _materialInstance.SetColor(ColorShadowIds[i],    GetCachedColor(lc.shadowColorIndex));
            _materialInstance.SetColor(ColorBaseIds[i],       GetCachedColor(lc.baseColorIndex));
            _materialInstance.SetColor(ColorHighlightIds[i],  GetCachedColor(lc.highlightColorIndex));
        }

        _materialInstance.SetColor(BaseColorId,       baseTint);
        _materialInstance.SetFloat(Threshold1Id,      shadowThreshold);
        _materialInstance.SetFloat(Threshold2Id,      highlightThreshold);
        _materialInstance.SetFloat(IntensityAffectsBandsId, intensityAffectsBands);
        _materialInstance.SetFloat(BandAccumulationId, (float)bandAccumulation);
        _materialInstance.SetFloat(ApplyFogId, applyFog ? 1f : 0f);
    }

    // ── material instance lifecycle ──

    private void EnsureMaterialInstance()
    {
        if (_instancedTerrain != null && _instancedTerrain != targetTerrain)
            ReleaseMaterialInstance();

        if (_materialInstance != null) return;

        Material shared = targetTerrain.materialTemplate;
        if (shared == null) return;

        _originalMaterialTemplate = shared;
        _instancedTerrain = targetTerrain;
        _materialInstance = new Material(shared);
        _materialInstance.name = shared.name + " (Instance)";
        targetTerrain.materialTemplate = _materialInstance;
    }

    private void ReleaseMaterialInstance()
    {
        if (_instancedTerrain != null && _originalMaterialTemplate != null)
            _instancedTerrain.materialTemplate = _originalMaterialTemplate;

        if (_materialInstance != null)
        {
            if (Application.isPlaying)
                Destroy(_materialInstance);
            else
                DestroyImmediate(_materialInstance);
        }

        _materialInstance = null;
        _originalMaterialTemplate = null;
        _instancedTerrain = null;
    }

    // ── palette cache ──

    private void RefreshPaletteCache()
    {
        if (paletteTexture == _cachedPalette &&
            _cachedColors != null &&
            _cachedConvertToProjectColorSpace == convertPaletteToProjectColorSpace)
        {
            return;
        }

        _cachedPalette = paletteTexture;
        _cachedColors  = null;
        _cachedConvertToProjectColorSpace = convertPaletteToProjectColorSpace;

        if (paletteTexture == null) return;

#if UNITY_EDITOR
        string path = AssetDatabase.GetAssetPath(paletteTexture);
        if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
        {
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            Texture2D tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (tmp.LoadImage(bytes, false))
                _cachedColors = ConvertPaletteToProjectSpace(tmp.GetPixels32());
            DestroyImmediate(tmp);
            if (_cachedColors != null) return;
        }
#endif
        _cachedColors = ConvertPaletteToProjectSpace(paletteTexture.GetPixels32());
    }

    private Color GetCachedColor(int index)
    {
        if (_cachedColors == null || _cachedColors.Length == 0)
            return Color.magenta;

        return _cachedColors[Mathf.Clamp(index, 0, _cachedColors.Length - 1)];
    }

    private int GetMaxPaletteIndex()
    {
        if (_cachedColors != null && _cachedColors.Length > 0)
            return _cachedColors.Length - 1;
        if (paletteTexture != null)
            return Mathf.Max(0, paletteTexture.width * paletteTexture.height - 1);
        return 255;
    }

    private Color[] ConvertPaletteToProjectSpace(Color32[] source)
    {
        if (source == null || source.Length == 0)
            return null;

        Color[] converted = new Color[source.Length];
        bool linearProject = convertPaletteToProjectColorSpace &&
                             QualitySettings.activeColorSpace == ColorSpace.Linear;

        for (int i = 0; i < source.Length; i++)
        {
            Color c = source[i];
            converted[i] = linearProject ? c.linear : c;
        }

        return converted;
    }

    private void NormalizeBandPercentages(out float thresholdShadow, out float thresholdHighlight)
    {
        darkBandPercentage = Mathf.Clamp01(darkBandPercentage);
        baseBandPercentage = Mathf.Clamp01(baseBandPercentage);
        highlightBandPercentage = Mathf.Clamp01(highlightBandPercentage);

        float total = darkBandPercentage + baseBandPercentage + highlightBandPercentage;
        if (total <= 0.0001f)
        {
            darkBandPercentage = 0.3f;
            baseBandPercentage = 0.75f;
            highlightBandPercentage = 1f;
            total = 1f;
        }

        float dark = darkBandPercentage / total;
        float baseBand = baseBandPercentage / total;

        thresholdShadow = Mathf.Clamp01(dark);
        thresholdHighlight = Mathf.Clamp01(dark + baseBand);
    }
}
