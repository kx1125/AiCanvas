using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tsukuyomi.AiCanvas.Editor
{
    internal sealed class ImageHistogramData
    {
        // R, G, B, luminance; 256 bins per channel.
        internal readonly uint[] Bins;
        internal readonly int SampleWidth, SampleHeight;
        internal readonly bool Sampled;
        internal readonly ImageValueKind Kind;
        internal readonly ImageWorkingSpace Space;
        internal ImageHistogramData(uint[] bins, GpuImage input, int width, int height, ImageWorkingSpace space)
        {
            Bins = bins; SampleWidth = width; SampleHeight = height;
            Sampled = width != input.Width || height != input.Height; Kind = input.Kind;
            Space = space;
        }
    }

    [InitializeOnLoad]
    internal static class ImageHistogram
    {
        internal const string ShaderPath = "Packages/com.tsukuyomi.ai-canvas/Editor/Shaders/ImageHistogram.compute";
        private static readonly Dictionary<string, ImageHistogramData> Cache = new();
        private static readonly Queue<string> Order = new();
        private static readonly Dictionary<string, Task<ImageHistogramData>> Pending = new();
        private static ComputeShader shader;
        private static bool stopping;
        internal static int DispatchCount { get; private set; }

        static ImageHistogram()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            EditorApplication.quitting += Shutdown;
        }

        private static void Shutdown()
        {
            stopping = true;
            if (SystemInfo.supportsAsyncGPUReadback) AsyncGPUReadback.WaitAllRequests();
            Cache.Clear(); Order.Clear();
        }

        internal static ImageWorkingSpace EffectiveSpace(GpuImage input, ImageWorkingSpace space)
            => input.Kind == ImageValueKind.Color ? space : ImageWorkingSpace.Linear;

        internal static string Key(GpuImage input, ImageWorkingSpace space)
            => input.Version + "|" + (int)EffectiveSpace(input, space) + "|" + AssetDatabase.GetAssetDependencyHash(ShaderPath);

        internal static Task<ImageHistogramData> GetAsync(GpuImage input, ImageWorkingSpace space = ImageWorkingSpace.Linear)
        {
            if (stopping) return Task.FromException<ImageHistogramData>(new OperationCanceledException());
            if (!SystemInfo.supportsAsyncGPUReadback)
                return Task.FromException<ImageHistogramData>(new NotSupportedException("当前设备不支持异步直方图统计；仍可使用色阶手柄。"));
            space = EffectiveSpace(input, space);
            string key = Key(input, space);
            if (Cache.TryGetValue(key, out var data)) return Task.FromResult(data);
            if (Pending.TryGetValue(key, out var task)) return task;
            // Register before dispatch, even if an error or completion is synchronous.
            var completion = new TaskCompletionSource<ImageHistogramData>();
            Pending.Add(key, completion.Task);
            Compute(input.Retain(), space, key, completion);
            return completion.Task;
        }

        private static async void Compute(GpuImage input, ImageWorkingSpace space, string key, TaskCompletionSource<ImageHistogramData> completion)
        {
            ComputeBuffer buffer = null;
            try
            {
                if (shader == null) shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ShaderPath);
                if (shader == null) throw new InvalidOperationException("Histogram ComputeShader is missing.");
                int width = Math.Min(input.Width, 1024), height = Math.Min(input.Height, 1024);
                buffer = new ComputeBuffer(1024, sizeof(uint));
                int clear = shader.FindKernel("ClearHistogram"), count = shader.FindKernel("CountHistogram");
                shader.SetBuffer(clear, "_Bins", buffer); shader.Dispatch(clear, 1, 1, 1);
                shader.SetBuffer(count, "_Bins", buffer); shader.SetTexture(count, "_Input", input.Texture);
                shader.SetInt("_SampleWidth", width); shader.SetInt("_SampleHeight", height);
                shader.SetInt("_EncodeSrgb", space == ImageWorkingSpace.SRgb ? 1 : 0);
                shader.Dispatch(count, (width + 15) / 16, (height + 15) / 16, 1);
                DispatchCount++;
                var request = await AsyncGPUReadback.RequestAsync(buffer);
                if (request.hasError) throw new InvalidOperationException("GPU 直方图统计读取失败。");
                var data = new ImageHistogramData(request.GetData<uint>().ToArray(), input, width, height, space);
                if (!stopping)
                {
                    while (Cache.Count >= 32) Cache.Remove(Order.Dequeue());
                    Cache[key] = data; Order.Enqueue(key);
                }
                completion.TrySetResult(data);
            }
            catch (Exception exception) { completion.TrySetException(exception); }
            finally { Pending.Remove(key); buffer?.Dispose(); input.Dispose(); }
        }
    }
}
