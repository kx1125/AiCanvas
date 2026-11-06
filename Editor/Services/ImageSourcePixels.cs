using System;
using System.IO;
using System.Reflection;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace Tsukuyomi.AiCanvas.Editor
{
    internal readonly struct ImageSourcePixels
    {
        // The Unity 6 editor decoder reads the same source formats as the texture importer.
        // Bind its version-specific entry point here so the rest of the graph uses public APIs.
        private delegate NativeArray<byte> ReadFile(string path, ref int width, ref int height,
            ref int rowBytes, ref GraphicsFormat format);

        private static readonly ReadFile DecodeFile = BindDecoder();
        private readonly Texture2D asset;
        private readonly string filePath;

        internal Vector2Int Size { get; }
        internal bool NeedsCopy => filePath != null || HasReducedMip(asset);

        internal ImageSourcePixels(Texture2D texture)
        {
            asset = texture;
            filePath = null;
            Size = new Vector2Int(texture.width, texture.height);

            string path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path))
                return;

            var importer = AssetImporter.GetAtPath(path);
            if (importer is TextureImporter sourceImporter &&
                (AssetDatabase.IsMainAsset(texture) || AssetDatabase.LoadAssetAtPath<Texture2D>(path) == texture))
            {
                sourceImporter.GetSourceTextureWidthAndHeight(out int width, out int height);
                Size = new Vector2Int(width, height);
                filePath = FileUtil.GetPhysicalPath(path);
            }
            else if (importer is IHVImageFormatImporter && AssetDatabase.IsMainAsset(texture) &&
                HasReducedMip(texture) && !texture.isReadable)
            {
                // GPU-native containers already contain the authored platform texels. Use those
                // directly when mip 0 is resident or readable, even for formats outside the file decoder.
                filePath = FileUtil.GetPhysicalPath(path);
            }
        }

        private static bool HasReducedMip(Texture2D texture)
        {
            return texture.activeMipmapLimit > 0 || (texture.streamingMipmaps && texture.loadedMipmapLevel > 0);
        }

        private static ReadFile BindDecoder()
        {
            var method = typeof(ImageConversion).GetMethod("LoadImageDataAtPath",
                BindingFlags.Static | BindingFlags.NonPublic, null, new[]
                {
                    typeof(string), typeof(int).MakeByRefType(), typeof(int).MakeByRefType(),
                    typeof(int).MakeByRefType(), typeof(GraphicsFormat).MakeByRefType()
                }, null);
            return method != null && method.ReturnType == typeof(NativeArray<byte>)
                ? (ReadFile)method.CreateDelegate(typeof(ReadFile)) : null;
        }

        // Returns an owned, single-mip texture only when the asset itself cannot supply the source pixels.
        internal Texture2D CreateCopy()
        {
            if (filePath != null)
                return DecodeSourceFile();

            if (!NeedsCopy)
                return null;

            if (!asset.isReadable)
                throw new InvalidOperationException($"无法取得 {asset.name} 的原始像素。请使用源图片文件，或提供包含完整 mip 0 的可读纹理。");

            var copy = AllocateTexture(Size.x, Size.y, asset.graphicsFormat);
            try
            {
                var pixels = asset.GetPixelData<byte>(0);
                if (pixels.Length != copy.GetPixelData<byte>(0).Length)
                    throw new InvalidDataException($"{asset.name} does not contain a complete mip 0.");

                copy.SetPixelData(pixels, 0);
                copy.Apply(false, true);
                return copy;
            }
            catch
            {
                Object.DestroyImmediate(copy);
                throw;
            }
        }

        private Texture2D DecodeSourceFile()
        {
            string path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), filePath));
            if (DecodeFile == null)
                return DecodePortableFormat(path);

            int width = 0;
            int height = 0;
            int rowBytes = 0;
            var format = GraphicsFormat.None;
            using var pixels = DecodeFile(path, ref width, ref height, ref rowBytes, ref format);
            if (!pixels.IsCreated || width != Size.x || height != Size.y || format == GraphicsFormat.None)
                throw new InvalidDataException($"无法按原始尺寸读取图片 {filePath}（{Size.x} × {Size.y}）。");

            // Keep the decoder's channel order and precision. Color is decoded exactly once by the transfer shader.
            var copy = AllocateTexture(width, height, GraphicsFormatUtility.GetLinearFormat(format));
            try
            {
                var target = copy.GetPixelData<byte>(0);
                int packedRow = target.Length / height;
                if (target.Length % height != 0 || rowBytes < packedRow || pixels.Length != (long)rowBytes * height)
                    throw new InvalidDataException($"Unexpected source pixel layout in {filePath}.");

                if (rowBytes == packedRow)
                    target.CopyFrom(pixels);
                else
                {
                    for (int y = 0; y < height; y++)
                        NativeArray<byte>.Copy(pixels, y * rowBytes, target, y * packedRow, packedRow);
                }

                copy.Apply(false, true);
                return copy;
            }
            catch
            {
                Object.DestroyImmediate(copy);
                throw;
            }
        }

        private Texture2D DecodePortableFormat(string path)
        {
            var copy = AllocateTexture(2, 2, GraphicsFormat.R8G8B8A8_UNorm);
            try
            {
                if (!copy.LoadImage(File.ReadAllBytes(path), true) || copy.width != Size.x || copy.height != Size.y)
                    throw new NotSupportedException($"此 Unity 版本无法按原始尺寸解码 {filePath}。请提供 PNG、JPEG 或 EXR 源文件。");

                return copy;
            }
            catch
            {
                Object.DestroyImmediate(copy);
                throw;
            }
        }

        private static Texture2D AllocateTexture(int width, int height, GraphicsFormat format)
        {
            return new Texture2D(width, height, format, 1, TextureCreationFlags.None)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
        }
    }
}
