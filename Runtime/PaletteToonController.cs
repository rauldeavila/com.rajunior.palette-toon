using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public class PaletteToonController : MonoBehaviour
{
    private static readonly int BaseColorId      = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorShadowId    = Shader.PropertyToID("_ColorShadow");
    private static readonly int ColorBaseId      = Shader.PropertyToID("_ColorBase");
    private static readonly int ColorHighlightId = Shader.PropertyToID("_ColorHighlight");
    private static readonly int Threshold1Id     = Shader.PropertyToID("_Threshold1");
    private static readonly int Threshold2Id     = Shader.PropertyToID("_Threshold2");
    private static readonly int UseRangePercentForLocalLightsId = Shader.PropertyToID("_UseRangePercentForLocalLights");
    private static readonly int IntensityAffectsBandsId = Shader.PropertyToID("_IntensityAffectsBands");
    private static readonly int BandAccumulationId      = Shader.PropertyToID("_BandAccumulation");
    private static readonly int ApplyFogId              = Shader.PropertyToID("_ApplyFog");

    public enum BandAccumulationMode
    {
        Add = 0,
        Max = 1
    }

    [Header("Target")]
    public Renderer targetRenderer;

    [Header("Palette PNG (1px colors)")]
    public Texture2D paletteTexture;

    [Header("Toon Colors (3 colors per object)")]
    [Min(0)] public int shadowColorIndex    = 0;
    [Min(0)] public int baseColorIndex      = 1;
    [Min(0)] public int highlightColorIndex = 2;

    [Header("Band Percentages (sum auto-normalized)")]
    [Range(0f, 1f)] public float darkBandPercentage = 0.35f;
    [Range(0f, 1f)] public float baseBandPercentage = 0.40f;
    [Range(0f, 1f)] public float highlightBandPercentage = 0.25f;

    [Header("Legacy Ramp Thresholds (auto from percentages)")]
    [Range(0f, 1f)] public float shadowThreshold    = 0.35f;
    [Range(0f, 1f)] public float highlightThreshold = 0.75f;

    [Header("Tint")]
    public Color baseTint = Color.white;

    [Header("Color Fidelity")]
    [Tooltip("Converts palette colors from sRGB to project color space (recommended for Linear projects).")]
    public bool convertPaletteToProjectColorSpace = true;

    [Header("Lighting Behavior")]
    public bool useRangePercentForLocalLights = true;
    [Range(0f, 1f)] public float intensityAffectsBands = 0f;
    public BandAccumulationMode bandAccumulation = BandAccumulationMode.Max;
    public bool applyFog = false;

    private MaterialPropertyBlock _mpb;

    // palette cache (already converted for current project color space)
    private Texture2D _cachedPalette;
    private Color[] _cachedColors;
    private bool _cachedConvertToProjectColorSpace;

    private void Reset()
    {
        targetRenderer = GetComponent<Renderer>();
    }

    private void OnEnable()
    {
        Apply();
    }

    private void OnDisable()
    {
        // clear overrides so the renderer goes back to material defaults
        if (targetRenderer != null)
        {
            targetRenderer.SetPropertyBlock(null);
        }
    }

    private void OnValidate()
    {
        NormalizeBandPercentages(out shadowThreshold, out highlightThreshold);
        intensityAffectsBands = Mathf.Clamp01(intensityAffectsBands);
        Apply();
    }

    // ── apply all properties via MaterialPropertyBlock ──

    public void Apply()
    {
        if (targetRenderer == null)
        {
            targetRenderer = GetComponent<Renderer>();
            if (targetRenderer == null) return;
        }

        if (_mpb == null)
            _mpb = new MaterialPropertyBlock();

        try { RefreshPaletteCache(); }
        catch (System.Exception e)
        {
            Debug.LogWarning("PaletteToonController: failed to read palette — " + e.Message, this);
        }

        int maxIndex = GetMaxPaletteIndex();
        shadowColorIndex    = Mathf.Clamp(shadowColorIndex, 0, maxIndex);
        baseColorIndex      = Mathf.Clamp(baseColorIndex, 0, maxIndex);
        highlightColorIndex = Mathf.Clamp(highlightColorIndex, 0, maxIndex);

        targetRenderer.GetPropertyBlock(_mpb);

        NormalizeBandPercentages(out shadowThreshold, out highlightThreshold);

        _mpb.SetColor(ColorShadowId,     GetCachedColor(shadowColorIndex));
        _mpb.SetColor(ColorBaseId,       GetCachedColor(baseColorIndex));
        _mpb.SetColor(ColorHighlightId,  GetCachedColor(highlightColorIndex));
        _mpb.SetColor(BaseColorId,       baseTint);
        _mpb.SetFloat(Threshold1Id,      shadowThreshold);
        _mpb.SetFloat(Threshold2Id,      highlightThreshold);
        _mpb.SetFloat(UseRangePercentForLocalLightsId, useRangePercentForLocalLights ? 1f : 0f);
        _mpb.SetFloat(IntensityAffectsBandsId, intensityAffectsBands);
        _mpb.SetFloat(BandAccumulationId, (float)bandAccumulation);
        _mpb.SetFloat(ApplyFogId, applyFog ? 1f : 0f);

        targetRenderer.SetPropertyBlock(_mpb);
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
        // editor: read raw bytes from disk — works even if texture is not readable
        string path = AssetDatabase.GetAssetPath(paletteTexture);
        if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
        {
            byte[] bytes = System.IO.File.ReadAllBytes(path);
            // PNG palette values are authored in sRGB.
            // We explicitly convert to project color space below.
            Texture2D tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (tmp.LoadImage(bytes, false))
                _cachedColors = ConvertPaletteToProjectSpace(tmp.GetPixels32());
            DestroyImmediate(tmp);
            if (_cachedColors != null) return;
        }
#endif
        // build fallback (requires isReadable)
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
            darkBandPercentage = 0.35f;
            baseBandPercentage = 0.40f;
            highlightBandPercentage = 0.25f;
            total = 1f;
        }

        float dark = darkBandPercentage / total;
        float baseBand = baseBandPercentage / total;

        thresholdShadow = Mathf.Clamp01(dark);
        thresholdHighlight = Mathf.Clamp01(dark + baseBand);
    }
}
