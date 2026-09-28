using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Tsukuyomi.AiCanvas.Editor
{
    [InitializeOnLoad]
    internal static class ImageComputeDevice
    {
        internal const string TransferPath = "Packages/com.tsukuyomi.ai-canvas/Editor/Shaders/ImageTransfer.compute";
        internal static readonly GpuImageCache Cache = new();
        private static ComputeShader transfer;
        internal static int DispatchCount { get; private set; }
        internal static int ReadbackCount { get; private set; }

        static ImageComputeDevice()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            EditorApplication.quitting += Shutdown;
        }

        internal static ComputeShader Transfer => transfer != null ? transfer :
            transfer = AssetDatabase.LoadAssetAtPath<ComputeShader>(TransferPath)
                ?? throw new InvalidOperationException("AiCanvas image transfer ComputeShader is missing.");

        private static void Shutdown()
        {
            ImageGraphRunner.Cancel();
            // A requested readback owns its temporary target until the GPU is finished.
            if (SystemInfo.supportsAsyncGPUReadback) AsyncGPUReadback.WaitAllRequests();
            ImagePreviewStore.ClearAll();
            Cache.Dispose();
            GpuImage.ReleaseAll();
        }

        internal static RenderTexture Allocate(int width, int height, GraphicsFormat format = GraphicsFormat.R16G16B16A16_SFloat)
        {
            if (!SystemInfo.supportsComputeShaders)
                throw new NotSupportedException("AiCanvas local image processing requires ComputeShader support.");
            int maxSize = Math.Min(16384, SystemInfo.maxTextureSize);
            if (width < 1 || height < 1 || width > maxSize || height > maxSize)
                throw new ArgumentOutOfRangeException(nameof(width), $"Unsupported image dimensions: {width} x {height}.");
            if (!SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.LoadStore) ||
                !SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Sample))
                throw new NotSupportedException("GPU does not support the required image format: " + format);
            var descriptor = new RenderTextureDescriptor(width, height)
            {
                graphicsFormat = format, depthBufferBits = 0, msaaSamples = 1,
                enableRandomWrite = true, useMipMap = false, autoGenerateMips = false,
                dimension = TextureDimension.Tex2D, volumeDepth = 1
            };
            var texture = new RenderTexture(descriptor)
            {
                name = "AiCanvas GPU image", hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            if (!texture.Create())
            {
                UnityEngine.Object.DestroyImmediate(texture);
                throw new InvalidOperationException("Could not allocate an AiCanvas GPU image.");
            }
            return texture;
        }

        internal static GpuImage Import(Texture2D texture, string version)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            string key = Hash128.Compute("import-v1|" + version + "|" + texture.width + "|" + texture.height + "|" + texture.isDataSRGB +
                "|" + ImageAssetQueryScope.DependencyHash(TransferPath)).ToString();
            if (Cache.TryGet(key, out var cached)) return cached;
            var kind = texture.isDataSRGB ? ImageValueKind.Color : ImageValueKind.Data;
            var target = Allocate(texture.width, texture.height);
            try
            {
                var shader = Transfer;
                int kernel = shader.FindKernel("ImportImage");
                shader.SetTexture(kernel, "_Input", texture);
                // An sRGB SRV already decodes on texture Load. Gamma projects may expose a UNorm SRV.
                shader.SetInt("_DecodeSrgb", texture.isDataSRGB && !GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat) ? 1 : 0);
                Dispatch(shader, kernel, target);
                var image = new GpuImage(target, key, kind, ImageInputSnapshot.HasSourceAlpha(texture));
                Cache.Add(key, image);
                return image;
            }
            catch { target.Release(); UnityEngine.Object.DestroyImmediate(target); throw; }
        }

        internal static GpuImage Execute(ComputeImageOperation operation, IReadOnlyList<GpuImage> inputs)
        {
            if (inputs.Count != operation.Inputs.Count) throw new ArgumentException("Compute input count does not match its operation.");
            int width = operation.Width == 0 ? inputs[0].Width : operation.Width;
            int height = operation.Height == 0 ? inputs[0].Height : operation.Height;
            var kind = operation.ValueKind == ImageValueKind.Inherit ? inputs[0].Kind : operation.ValueKind;
            string key = ComputeKey(operation, inputs);
            if (Cache.TryGet(key, out var cached)) return cached;
            var target = Allocate(width, height);
            RenderTexture intermediate = null;
            try
            {
                var shader = operation.Shader;
                int kernel = shader.FindKernel(operation.Kernel);
                for (int index = 0; index < inputs.Count; index++)
                {
                    shader.SetTexture(kernel, operation.Inputs[index].ShaderName, inputs[index].Texture);
                    shader.SetInt(operation.Inputs[index].ShaderName + "IsColor", inputs[index].Kind == ImageValueKind.Color ? 1 : 0);
                }
                foreach (var parameter in operation.Parameters) parameter.Apply(shader);
                if (operation.FollowUpKernel == null) Dispatch(shader, kernel, target);
                else
                {
                    // Resolve both kernels before dispatching. The temporary is never published or cached.
                    int followUp = shader.FindKernel(operation.FollowUpKernel);
                    intermediate = Allocate(width, height);
                    Dispatch(shader, kernel, intermediate);
                    shader.SetTexture(followUp, operation.Inputs[0].ShaderName, intermediate);
                    shader.SetInt(operation.Inputs[0].ShaderName + "IsColor", kind == ImageValueKind.Color ? 1 : 0);
                    Dispatch(shader, followUp, target);
                }
                var result = new GpuImage(target, key, kind);
                Cache.Add(key, result);
                return result;
            }
            catch { target.Release(); UnityEngine.Object.DestroyImmediate(target); throw; }
            finally
            {
                if (intermediate != null) { intermediate.Release(); UnityEngine.Object.DestroyImmediate(intermediate); }
            }
        }

        private static string ComputeKey(ComputeImageOperation operation, IReadOnlyList<GpuImage> inputs)
            => Hash128.Compute("compute-v1|" + operation.Fingerprint + "|" +
                string.Join("|", inputs.Select(input => input.Version))).ToString();

        internal static GpuImage[] ExecuteChannelSplit(ComputeImageOperation[] operations, GpuImage input)
        {
            if (operations.Length != 4 || operations.Where((op, index) => op.SplitChannel != index ||
                op.Shader != operations[0].Shader).Any()) throw new ArgumentException("Expected the four ordered RGBA split outputs.");
            var inputs = new[] { input };
            var keys = operations.Select(op => ComputeKey(op, inputs)).ToArray();
            var results = new GpuImage[4];
            var targets = new RenderTexture[4];
            try
            {
                int hits = 0;
                for (int index = 0; index < 4; index++) if (Cache.TryGet(keys[index], out results[index])) hits++;
                if (hits > 0 || SystemInfo.supportedRandomWriteTargetCount < 4)
                {
                    // Never write into a cached immutable result. Partial hits, or devices
                    // with fewer UAV bindings, keep the original independent-output path.
                    for (int index = 0; index < 4; index++)
                        if (results[index] == null) results[index] = Execute(operations[index], inputs);
                    return results;
                }
                var shader = operations[0].Shader;
                int kernel = shader.FindKernel("RgbaSplitAll");
                shader.SetTexture(kernel, "_Input", input.Texture);
                for (int index = 0; index < 4; index++)
                {
                    targets[index] = Allocate(input.Width, input.Height);
                    shader.SetTexture(kernel, "_Split" + "RGBA"[index], targets[index]);
                }
                DispatchSize(shader, kernel, input.Width, input.Height);
                for (int index = 0; index < 4; index++)
                {
                    results[index] = new GpuImage(targets[index], keys[index], ImageValueKind.Data);
                    targets[index] = null;
                    Cache.Add(keys[index], results[index]);
                }
                return results;
            }
            catch
            {
                foreach (var image in results) image?.Dispose();
                throw;
            }
            finally
            {
                foreach (var target in targets)
                    if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            }
        }

        private static void Dispatch(ComputeShader shader, int kernel, RenderTexture target)
        {
            shader.SetTexture(kernel, "_Output", target);
            DispatchSize(shader, kernel, target.width, target.height);
        }

        private static void DispatchSize(ComputeShader shader, int kernel, int width, int height)
        {
            shader.GetKernelThreadGroupSizes(kernel, out uint x, out uint y, out uint z);
            if (x == 0 || y == 0 || z != 1) throw new InvalidOperationException("Image kernels must use two-dimensional thread groups (z = 1).");
            shader.SetInt("_Width", width); shader.SetInt("_Height", height);
            shader.Dispatch(kernel, (width + (int)x - 1) / (int)x, (height + (int)y - 1) / (int)y, 1);
            DispatchCount++;
        }

        internal static void PreviewChannel(Texture source, int channel, RenderTexture target)
        {
            if (channel < 0 || channel > 3) throw new ArgumentOutOfRangeException(nameof(channel));
            var shader = Transfer;
            int kernel = shader.FindKernel("PreviewChannel");
            shader.SetTexture(kernel, "_Input", source);
            shader.SetInt("_PreviewChannel", channel);
            shader.SetInt("_DecodeSrgb", source is Texture2D texture && texture.isDataSRGB &&
                !GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat) ? 1 : 0);
            Dispatch(shader, kernel, target);
        }

        internal static async Task<byte[]> ReadPngAsync(GpuImage source, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var lease = source.Retain();
            var target = Allocate(source.Width, source.Height, GraphicsFormat.R8G8B8A8_UNorm);
            target.name = "AiCanvas PNG staging";
            Texture2D readable = null;
            try
            {
                var shader = Transfer;
                int kernel = shader.FindKernel("EncodeImage");
                shader.SetTexture(kernel, "_Input", source.Texture);
                shader.SetInt("_EncodeSrgb", source.Kind == ImageValueKind.Color ? 1 : 0);
                Dispatch(shader, kernel, target);
                ReadbackCount++;
                readable = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true)
                    { hideFlags = HideFlags.HideAndDontSave };
                if (SystemInfo.supportsAsyncGPUReadback &&
                    SystemInfo.IsFormatSupported(target.graphicsFormat, GraphicsFormatUsage.ReadPixels))
                {
                    // Cancellation is observed AFTER the request finishes; never release an in-flight target.
                    var request = await AsyncGPUReadback.RequestAsync(target, 0, GraphicsFormat.R8G8B8A8_UNorm);
                    if (request.hasError) throw new InvalidOperationException("GPU image readback failed.");
                    token.ThrowIfCancellationRequested();
                    readable.LoadRawTextureData(request.GetData<byte>());
                    readable.Apply(false, false);
                }
                else
                {
                    var previous = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = target;
                        readable.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
                        readable.Apply(false, false);
                    }
                    finally { RenderTexture.active = previous; }
                }
                token.ThrowIfCancellationRequested();
                return readable.EncodeToPNG();
            }
            finally
            {
                if (readable != null) UnityEngine.Object.DestroyImmediate(readable);
                target.Release(); UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
