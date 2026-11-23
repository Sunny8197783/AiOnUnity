using UnityEditor;
using UnityEngine;

namespace AiOn.EditorTools
{
    /// <summary>
    /// Import rules for the packed surface detail maps (Resources/Surfaces/surf_&lt;set&gt;_n|a.jpg, see LevelSurfaces): data,
    /// not colour (linear), wrapped, trilinear with anisotropy; the normal+roughness map as BC7, the albedo/AO map as one
    /// channel (BC4). Max 1024 (the sets are packed at 1024).
    /// </summary>
    public sealed class SurfaceTextureImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            var path = assetPath.Replace((char)92, '/');
            if (path.Contains("/Resources/Sky/sky_")) { Sky((TextureImporter)assetImporter); return; }
            if (path.Contains("/Resources/Decals/") || path.Contains("/Resources/Fx/fx_")) { Decal((TextureImporter)assetImporter); return; }
            if (!path.Contains("/Resources/Surfaces/surf_")) return;
            var ti = (TextureImporter)assetImporter;
            bool normal = assetPath.EndsWith("_n.jpg");
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = false;
            ti.alphaSource = TextureImporterAlphaSource.None;
            ti.mipmapEnabled = true;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = 8;
            ti.maxTextureSize = 1024;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "Standalone", overridden = true, maxTextureSize = 1024, format = normal ? TextureImporterFormat.BC7 : TextureImporterFormat.BC4, textureCompression = TextureImporterCompression.CompressedHQ });
        }

        /// <summary>Decal pictures (Resources/Decals): colour with alpha, clamped, mipmapped, at most 1024.</summary>
        static void Decal(TextureImporter ti)
        {
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = 4;
            // keep the size as drawn: the notice sheets are 512x724 (A-series), and the default rounding to 512x512
            // squashed their Korean text before the projector stretched it back
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.maxTextureSize = 1024;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
        }

        /// <summary>The sky photos (Resources/Sky, see SkyRig.Photos): colour, no mips (sampled at mip 0, the azimuth wraps
        /// at the seam), repeat across, clamped top and bottom, BC7 at 2048.</summary>
        static void Sky(TextureImporter ti)
        {
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.alphaSource = TextureImporterAlphaSource.None;
            ti.mipmapEnabled = false;
            ti.wrapModeU = TextureWrapMode.Repeat;
            ti.wrapModeV = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Bilinear;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "Standalone", overridden = true, maxTextureSize = 2048, format = TextureImporterFormat.BC7, textureCompression = TextureImporterCompression.CompressedHQ });
        }
    }
}
