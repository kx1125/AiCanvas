using System;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Tsukuyomi.AiCanvas.Editor
{
    /// <summary>Owned PNG bytes and submission metadata; safe to retain across awaits/reimports.</summary>
    public sealed class ImageInputSnapshot
    {
        private readonly byte[] png;
        public int Width { get; }
        public int Height { get; }
        public bool HasAlpha { get; }
        public string AssetPath { get; }
        public string AssetVersion { get; }
        public string ContentHash { get; }

        private ImageInputSnapshot(byte[] png, int width, int height, bool hasAlpha, string assetPath, string assetVersion)
        {
            this.png = png;
            Width = width;
            Height = height;
            HasAlpha = hasAlpha;
            AssetPath = assetPath;
            AssetVersion = assetVersion;
            using var hash = SHA256.Create();
            ContentHash = BitConverter.ToString(hash.ComputeHash(png)).Replace("-", "").ToLowerInvariant();
        }

        // Providers own the returned buffer and cannot mutate the submitted snapshot.
        public byte[] CopyPngBytes() => (byte[])png.Clone();

        internal static ImageInputSnapshot FromPng(Texture2D imported, byte[] png)
        {
            var size = new ImageSourcePixels(imported).Size;
            return new ImageInputSnapshot((byte[])png.Clone(), size.x, size.y, HasSourceAlpha(imported),
                AssetDatabase.GetAssetPath(imported), GetVersion(imported));
        }

        internal static ImageInputSnapshot FromGpu(byte[] png, int width, int height, bool hasAlpha, string path, string version)
            => new ImageInputSnapshot(png, width, height, hasAlpha, path, version);

        /// <summary>Capture on the Unity main thread. Supports non-readable imported textures.</summary>
        [Obsolete("Legacy synchronous capture. AiCanvas graph execution uses GPU snapshots; retained for existing integrations.")]
        public static ImageInputSnapshot Capture(Texture2D source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var original = new ImageSourcePixels(source);
            ImageComputeDevice.ValidateDimensions(original.Size.x, original.Size.y);
            var previous = RenderTexture.active;
            var previousSrgb = GL.sRGBWrite;
            RenderTexture temporary = null;
            Texture2D readable = null;
            Texture2D copy = null;
            try
            {
                copy = original.CreateCopy();
                var pixels = copy != null ? copy : source;
                bool encodeSrgb = GraphicsFormatUtility.IsSRGBFormat(pixels.graphicsFormat) &&
                    QualitySettings.activeColorSpace == ColorSpace.Linear;
                temporary = RenderTexture.GetTemporary(pixels.width, pixels.height, 0, RenderTextureFormat.ARGB32,
                    encodeSrgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
                GL.sRGBWrite = encodeSrgb;
                Graphics.Blit(pixels, temporary);
                RenderTexture.active = temporary;
                readable = new Texture2D(pixels.width, pixels.height, TextureFormat.RGBA32, false, true);
                readable.ReadPixels(new Rect(0, 0, pixels.width, pixels.height), 0, 0, false);
                readable.Apply(false, false);
                return FromGpu(readable.EncodeToPNG(), pixels.width, pixels.height, HasSourceAlpha(source),
                    AssetDatabase.GetAssetPath(source), GetVersion(source));
            }
            finally
            {
                GL.sRGBWrite = previousSrgb;
                RenderTexture.active = previous;
                if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
                if (readable != null) UnityEngine.Object.DestroyImmediate(readable);
                if (copy != null)
                    UnityEngine.Object.DestroyImmediate(copy);
            }
        }

        internal static bool HasSourceAlpha(Texture2D texture)
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
            return importer != null ? importer.DoesSourceTextureHaveAlpha()
                : GraphicsFormatUtility.HasAlphaChannel(texture.graphicsFormat);
        }

        internal static string GetVersion(Texture2D texture)
            => ImageAssetQueryScope.TextureVersion(texture);
    }
}
