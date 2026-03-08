using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class PaletteToonController : MonoBehaviour
{
    private static readonly int BaseColorId      = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorShadowId    = Shader.PropertyToID("_ColorShadow");
    private static readonly int ColorBaseId      = Shader.PropertyToID("_ColorBase");
    private static readonly int ColorHighlightId = Shader.PropertyToID("_ColorHighlight");
    private static readonly int Threshold1Id     = Shader.PropertyToID("_Threshold1");
    private static readonly int Threshold2Id     = Shader.PropertyToID("_Threshold2");
    private static readonly int IntensityAffectsBandsId = Shader.PropertyToID("_IntensityAffectsBands");
    private static readonly int BandAccumulationId      = Shader.PropertyToID("_BandAccumulation");
    private static readonly int ApplyFogId              = Shader.PropertyToID("_ApplyFog");
    private static readonly int OutlineEnabledId       = Shader.PropertyToID("_OutlineEnabled");
    private static readonly int OutlineWidthId         = Shader.PropertyToID("_OutlineWidth");
    private static readonly int OutlineColorId         = Shader.PropertyToID("_OutlineColor");

    public enum BandAccumulationMode
    {
        Add = 0,
        Max = 1
    }

    [Header("Setup")]
    public Renderer targetRenderer;
    [Tooltip("Which material slot on the renderer to control (0 = first material).")]
    [Min(0)] public int materialIndex = 0;
    public Texture2D paletteTexture;

    [Header("Toon Colors")]
    [Min(0)] public int shadowColorIndex    = 0;
    [Min(0)] public int baseColorIndex      = 1;
    [Min(0)] public int highlightColorIndex = 2;

    [Header("Band Balance")]
    [Range(0f, 1f)] public float darkBandPercentage = 0.35f;
    [Range(0f, 1f)] public float baseBandPercentage = 0.40f;
    [Range(0f, 1f)] public float highlightBandPercentage = 0.25f;

    [HideInInspector]
    [Range(0f, 1f)] public float shadowThreshold    = 0.35f;
    [HideInInspector]
    [Range(0f, 1f)] public float highlightThreshold = 0.75f;

    [Header("Tint")]
    public Color baseTint = Color.white;

    [Header("Outline")]
    public bool enableOutline = false;
    [Range(0f, 0.1f)] public float outlineWidth = 0.02f;
    public Color outlineColor = Color.black;

    [Header("Advanced")]
    [Tooltip("Converts palette colors from sRGB to project color space.")]
    public bool convertPaletteToProjectColorSpace = false;
    [Range(0f, 1f)] public float intensityAffectsBands = 1f;
    public BandAccumulationMode bandAccumulation = BandAccumulationMode.Max;
    public bool applyFog = false;

    private Material _materialInstance;
    private Material _originalSharedMaterial;
    private Renderer _instancedRenderer;
    private int _instancedMaterialIndex = -1;

    // palette cache (already converted for current project color space)
    private Texture2D _cachedPalette;
    private Color[] _cachedColors;
    private bool _cachedConvertToProjectColorSpace;

    private void Reset()
    {
        targetRenderer = GetComponent<Renderer>();
        AutoAssignMaterialIndex();
    }

    private void AutoAssignMaterialIndex()
    {
        if (targetRenderer == null) return;

        int slotCount = targetRenderer.sharedMaterials.Length;
        if (slotCount <= 1)
        {
            materialIndex = 0;
            return;
        }

        PaletteToonController[] siblings = GetComponents<PaletteToonController>();
        bool[] claimed = new bool[slotCount];
        foreach (PaletteToonController sibling in siblings)
        {
            if (sibling == this) continue;
            if (sibling.materialIndex >= 0 && sibling.materialIndex < slotCount)
                claimed[sibling.materialIndex] = true;
        }

        for (int i = 0; i < slotCount; i++)
        {
            if (!claimed[i])
            {
                materialIndex = i;
                return;
            }
        }

        materialIndex = 0;
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

    // ── apply all properties via material instance (SRP Batcher compatible) ──

    public void Apply()
    {
        if (targetRenderer == null)
        {
            targetRenderer = GetComponent<Renderer>();
            if (targetRenderer == null) return;
        }

        EnsureMaterialInstance();
        if (_materialInstance == null) return;

        EnsureOutlineSmoother();

        try { RefreshPaletteCache(); }
        catch (System.Exception e)
        {
            Debug.LogWarning("PaletteToonController: failed to read palette — " + e.Message, this);
        }

        int maxIndex = GetMaxPaletteIndex();
        shadowColorIndex    = Mathf.Clamp(shadowColorIndex, 0, maxIndex);
        baseColorIndex      = Mathf.Clamp(baseColorIndex, 0, maxIndex);
        highlightColorIndex = Mathf.Clamp(highlightColorIndex, 0, maxIndex);

        NormalizeBandPercentages(out shadowThreshold, out highlightThreshold);

        _materialInstance.SetColor(ColorShadowId,     GetCachedColor(shadowColorIndex));
        _materialInstance.SetColor(ColorBaseId,       GetCachedColor(baseColorIndex));
        _materialInstance.SetColor(ColorHighlightId,  GetCachedColor(highlightColorIndex));
        _materialInstance.SetColor(BaseColorId,       baseTint);
        _materialInstance.SetFloat(Threshold1Id,      shadowThreshold);
        _materialInstance.SetFloat(Threshold2Id,      highlightThreshold);
        _materialInstance.SetFloat(IntensityAffectsBandsId, intensityAffectsBands);
        _materialInstance.SetFloat(BandAccumulationId, (float)bandAccumulation);
        _materialInstance.SetFloat(ApplyFogId, applyFog ? 1f : 0f);
        _materialInstance.SetFloat(OutlineEnabledId, enableOutline ? 1f : 0f);
        _materialInstance.SetFloat(OutlineWidthId, outlineWidth);
        _materialInstance.SetColor(OutlineColorId, outlineColor);
    }

    // ── outline smoother lifecycle ──

    private void EnsureOutlineSmoother()
    {
        PaletteToonOutlineSmoother smoother = GetComponent<PaletteToonOutlineSmoother>();

        if (enableOutline)
        {
            if (smoother == null)
                smoother = gameObject.AddComponent<PaletteToonOutlineSmoother>();
            else
                smoother.Bake();
        }
    }

    // ── material instance lifecycle ──

    private void EnsureMaterialInstance()
    {
        // If renderer or material index changed, release old instance first
        if (_instancedRenderer != null && _instancedRenderer != targetRenderer)
            ReleaseMaterialInstance();

        if (_materialInstance != null && _instancedMaterialIndex != materialIndex)
            ReleaseMaterialInstance();

        if (_materialInstance != null) return;

        Material[] sharedMats = targetRenderer.sharedMaterials;
        if (sharedMats == null || sharedMats.Length == 0) return;

        if (materialIndex >= sharedMats.Length)
        {
            Debug.LogWarning(
                $"PaletteToonController: materialIndex {materialIndex} is out of range " +
                $"(renderer has {sharedMats.Length} material(s)). Clamping to last slot.",
                this);
            materialIndex = sharedMats.Length - 1;
        }

        Material shared = sharedMats[materialIndex];
        if (shared == null) return;

        _originalSharedMaterial = shared;
        _instancedRenderer = targetRenderer;
        _instancedMaterialIndex = materialIndex;
        _materialInstance = new Material(shared);
        _materialInstance.name = shared.name + " (Instance)";

        Material[] mats = targetRenderer.sharedMaterials;
        mats[materialIndex] = _materialInstance;
        targetRenderer.sharedMaterials = mats;
    }

    private void ReleaseMaterialInstance()
    {
        if (_instancedRenderer != null && _originalSharedMaterial != null)
        {
            Material[] mats = _instancedRenderer.sharedMaterials;
            if (_instancedMaterialIndex >= 0 && _instancedMaterialIndex < mats.Length)
            {
                mats[_instancedMaterialIndex] = _originalSharedMaterial;
                _instancedRenderer.sharedMaterials = mats;
            }
        }

        if (_materialInstance != null)
        {
            if (Application.isPlaying)
                Destroy(_materialInstance);
            else
                DestroyImmediate(_materialInstance);
        }

        _materialInstance = null;
        _originalSharedMaterial = null;
        _instancedRenderer = null;
        _instancedMaterialIndex = -1;
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
