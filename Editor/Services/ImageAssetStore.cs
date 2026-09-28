using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    internal static class ImageAssetStore
    {
        internal static event Action<Texture2D> ImageImported;

        internal static string ValidateFolder(string folder)
        {
            folder = folder?.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrWhiteSpace(folder) ||
                (folder != AiCanvasResourcePaths.Root &&
                 !folder.StartsWith(AiCanvasResourcePaths.Root + "/", StringComparison.Ordinal)) ||
                folder.Split('/').Any(part => string.IsNullOrEmpty(part) || part == ".." || part == "."))
                throw new ArgumentException("AiCanvas resources must be saved under " + AiCanvasResourcePaths.Root + ".", nameof(folder));
            return folder;
        }

        internal static string GeneratedPath(string folder, string batchId, int index)
            => $"{folder}/AiCanvas_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{batchId}_{index + 1}.png";

        internal static Texture2D SavePng(string path, byte[] png, bool sRgb = true)
        {
            if (png == null || png.Length == 0) throw new ArgumentException("PNG data is empty.", nameof(png));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, png);
            // The postprocessor applies our policy during this first import, also in import workers.
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            if (!sRgb && AssetImporter.GetAtPath(path) is TextureImporter importer && importer.sRGBTexture)
            {
                importer.sRGBTexture = false;
                importer.userData = "AiCanvas.Data";
                importer.SaveAndReimport();
            }
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) throw new InvalidOperationException("Could not import image: " + path);
            EditorEvent.Invoke(ImageImported, texture);
            return texture;
        }

        internal static void SaveRecord(string path, object record)
            => File.WriteAllText(Path.ChangeExtension(path, ".json"), JsonUtility.ToJson(record, true));
    }

    // File names identify AiCanvas owned images across resource subfolders.
    // No in-process pending-import table: Unity may run the importer in another process.
    internal sealed class AiCanvasTextureImporter : AssetPostprocessor
    {
        private static readonly Regex Generated = new Regex(@"^AiCanvas_\d{8}_\d{6}_[0-9a-f]{32}_[1-9][0-9]*\.png$", RegexOptions.CultureInvariant);
        private static readonly Regex Capture = new Regex(@"^CameraColor_[0-9a-f]{32}_[0-9a-f]{32}\.png$", RegexOptions.CultureInvariant);

        private void OnPreprocessTexture()
        {
            string filename = Path.GetFileName(assetPath);
            bool capture = Capture.IsMatch(filename);
            if (!capture && !Generated.IsMatch(filename)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            importer.maxTextureSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(width, height)), 32, 16384);
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.sRGBTexture = importer.userData != "AiCanvas.Data";
            // Keep generated assets suitable for material use; captures are editor snapshots.
            importer.mipmapEnabled = !capture;
        }
    }
}
