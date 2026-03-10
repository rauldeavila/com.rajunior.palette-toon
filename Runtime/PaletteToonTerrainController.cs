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

    // Palette remap IDs
    private static readonly int PaletteRampId    = Shader.PropertyToID("_PaletteRamp");
    private static readonly int PaletteRowLUTId  = Shader.PropertyToID("_PaletteRowLUT");
    private static readonly int PaletteRowsId    = Shader.PropertyToID("_PaletteRows");

    private const string PaletteRemapKeyword = "_PALETTE_REMAP";
    private const string TextureVariationKeyword = "_TEXTURE_VARIATION";
    private const int LutResolution = 32;

    // Texture variation IDs
    private static readonly int[] ShadowTexIds =
    {
        Shader.PropertyToID("_ShadowTex_L0"),
        Shader.PropertyToID("_ShadowTex_L1"),
        Shader.PropertyToID("_ShadowTex_L2"),
        Shader.PropertyToID("_ShadowTex_L3"),
    };

    private static readonly int[] HighlightTexIds =
    {
        Shader.PropertyToID("_HighlightTex_L0"),
        Shader.PropertyToID("_HighlightTex_L1"),
        Shader.PropertyToID("_HighlightTex_L2"),
        Shader.PropertyToID("_HighlightTex_L3"),
    };

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

        // Texture variation mode
        public Texture2D shadowTexture;
        public Texture2D highlightTexture;
    }

    public enum TerrainToonMode
    {
        FlatColor = 0,
        PaletteRemap = 1,
        TextureVariation = 2
    }

    public enum BandAccumulationMode
    {
        Add = 0,
        Max = 1
    }

    [Header("Setup")]
    public Terrain targetTerrain;
    public Texture2D paletteTexture;

    [Header("Toon Mode")]
    [Tooltip("FlatColor: per-layer colors from palette.\n" +
             "PaletteRemap: auto-remap texture colors via 3D LUT.\n" +
             "TextureVariation: assign shadow/highlight texture variants per layer.")]
    public TerrainToonMode toonMode = TerrainToonMode.FlatColor;
    [Tooltip("The 3-column palette used for remap (shadow/base/highlight per row). " +
             "If not set, falls back to the main palette texture.")]
    public Texture2D paletteRampTexture;

    // Legacy field — migrated to toonMode in OnValidate
    [HideInInspector, SerializeField] private bool usePaletteRemap = false;

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

    // Palette remap LUT cache
    private Texture3D _paletteRowLUT;
    private Texture2D _cachedRampSource;
    private int _cachedRampRows;

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
        ReleasePaletteRowLUT();
    }

    private void OnDestroy()
    {
        ReleaseMaterialInstance();
        ReleasePaletteRowLUT();
    }

    private void OnValidate()
    {
        // Migrate legacy bool → enum
        if (usePaletteRemap)
        {
            toonMode = TerrainToonMode.PaletteRemap;
            usePaletteRemap = false;
        }

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

        NormalizeBandPercentages(out shadowThreshold, out highlightThreshold);

        // ── mode selection ──
        _materialInstance.DisableKeyword(PaletteRemapKeyword);
        _materialInstance.DisableKeyword(TextureVariationKeyword);

        switch (toonMode)
        {
            case TerrainToonMode.PaletteRemap:
            {
                _materialInstance.EnableKeyword(PaletteRemapKeyword);

                Texture2D ramp = paletteRampTexture != null ? paletteRampTexture : paletteTexture;
                if (ramp != null)
                {
                    EnsurePaletteRowLUT(ramp);
                    _materialInstance.SetTexture(PaletteRampId, ramp);
                    _materialInstance.SetFloat(PaletteRowsId, ramp.height);
                    if (_paletteRowLUT != null)
                        _materialInstance.SetTexture(PaletteRowLUTId, _paletteRowLUT);
                }
                break;
            }

            case TerrainToonMode.TextureVariation:
            {
                _materialInstance.EnableKeyword(TextureVariationKeyword);

                for (int i = 0; i < MaxLayers; i++)
                {
                    LayerColors lc = layers[i];
                    if (lc.shadowTexture != null)
                        _materialInstance.SetTexture(ShadowTexIds[i], lc.shadowTexture);
                    if (lc.highlightTexture != null)
                        _materialInstance.SetTexture(HighlightTexIds[i], lc.highlightTexture);
                }
                break;
            }

            default: // FlatColor
            {
                int maxIndex = GetMaxPaletteIndex();
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
                break;
            }
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

    // ── palette remap LUT ──

    private void EnsurePaletteRowLUT(Texture2D ramp)
    {
        if (ramp == _cachedRampSource && _paletteRowLUT != null && _cachedRampRows == ramp.height)
            return;

        ReleasePaletteRowLUT();
        _cachedRampSource = ramp;
        _cachedRampRows = ramp.height;

        Color[] rampColors = ReadPaletteColors(ramp);
        if (rampColors == null || rampColors.Length == 0) return;

        int paletteColumns = ramp.width;
        int paletteRows = ramp.height;

        // Terrain layer textures are imported as sRGB. In a Linear project the
        // GPU auto-converts sampled values to linear, so the LUT is indexed by
        // linear colors. Convert palette colors to linear to match.
        // In a Gamma project no conversion happens — use sRGB values as-is.
        bool isLinear = QualitySettings.activeColorSpace == ColorSpace.Linear;
        Color[] matchColors = new Color[rampColors.Length];
        for (int i = 0; i < rampColors.Length; i++)
        {
            if (isLinear)
            {
                matchColors[i] = new Color(
                    Mathf.GammaToLinearSpace(rampColors[i].r),
                    Mathf.GammaToLinearSpace(rampColors[i].g),
                    Mathf.GammaToLinearSpace(rampColors[i].b),
                    rampColors[i].a);
            }
            else
            {
                matchColors[i] = rampColors[i];
            }
        }

        // Build 3D LUT: for each (R,G,B) cell, find nearest palette color → encode row
        int res = LutResolution;
        Color[] lutPixels = new Color[res * res * res];

        for (int b = 0; b < res; b++)
        for (int g = 0; g < res; g++)
        for (int r = 0; r < res; r++)
        {
            float rf = r / (float)(res - 1);
            float gf = g / (float)(res - 1);
            float bf = b / (float)(res - 1);

            float bestDist = float.MaxValue;
            int bestRow = 0;

            for (int row = 0; row < paletteRows; row++)
            {
                for (int col = 0; col < paletteColumns; col++)
                {
                    int idx = row * paletteColumns + col;
                    if (idx >= matchColors.Length) continue;

                    Color pc = matchColors[idx];
                    float dr = rf - pc.r;
                    float dg = gf - pc.g;
                    float db = bf - pc.b;
                    float dist = dr * dr + dg * dg + db * db;
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestRow = row;
                    }
                }
            }

            // Encode row as normalized value; add 0.5 to center within the texel
            float rowNorm = (bestRow + 0.5f) / paletteRows;
            int lutIdx = r + g * res + b * res * res;
            lutPixels[lutIdx] = new Color(rowNorm, 0f, 0f, 1f);
        }

        // RFloat is a data format — never treated as sRGB, so stored row
        // indices are read back without any gamma conversion.
        _paletteRowLUT = new Texture3D(res, res, res, TextureFormat.RFloat, false);
        _paletteRowLUT.filterMode = FilterMode.Point;
        _paletteRowLUT.wrapMode = TextureWrapMode.Clamp;
        _paletteRowLUT.SetPixels(lutPixels);
        _paletteRowLUT.Apply(false, false);
    }

    private Color[] ReadPaletteColors(Texture2D tex)
    {
        if (tex == null) return null;

#if UNITY_EDITOR
        string path = AssetDatabase.GetAssetPath(tex);
        if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
        {
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            Texture2D tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (tmp.LoadImage(bytes, false))
            {
                Color32[] raw = tmp.GetPixels32();
                DestroyImmediate(tmp);
                // Return raw sRGB colors (shader textures are also sRGB → GPU converts both)
                Color[] result = new Color[raw.Length];
                for (int i = 0; i < raw.Length; i++)
                    result[i] = (Color)raw[i];
                return result;
            }
            DestroyImmediate(tmp);
        }
#endif
        Color32[] fallback = tex.GetPixels32();
        Color[] fallbackColors = new Color[fallback.Length];
        for (int i = 0; i < fallback.Length; i++)
            fallbackColors[i] = (Color)fallback[i];
        return fallbackColors;
    }

    private void ReleasePaletteRowLUT()
    {
        if (_paletteRowLUT != null)
        {
            if (Application.isPlaying)
                Destroy(_paletteRowLUT);
            else
                DestroyImmediate(_paletteRowLUT);
        }
        _paletteRowLUT = null;
        _cachedRampSource = null;
        _cachedRampRows = 0;
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
