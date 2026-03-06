using UnityEditor;
using UnityEngine;

public class PaletteTexturePostprocessor : AssetPostprocessor
{
    private static bool IsPaletteAsset(string path)
    {
        return path.Contains("/Palettes/");
    }

    private void OnPreprocessTexture()
    {
        if (!IsPaletteAsset(assetPath))
        {
            return;
        }

        TextureImporter importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Default;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.mipmapEnabled = false;
        importer.sRGBTexture = true;
        importer.ignorePngGamma = true;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.isReadable = true;
        importer.maxTextureSize = 8192;
    }
}
