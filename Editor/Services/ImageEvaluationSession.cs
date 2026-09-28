using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    /// <summary>Owns the frozen input and result leases of one evaluation; no GPU data is serialized.</summary>
    internal sealed class ImageEvaluationSession : IDisposable
    {
        private readonly Dictionary<Texture2D, GpuImage> sources = new();
        private readonly Dictionary<string, GpuImage> results = new();
        private readonly Dictionary<string, ImageInputSnapshot> snapshots = new();
        private readonly Dictionary<string, ComputeImageOperation[]> splitBatches;
        internal IReadOnlyDictionary<string, GpuImage> Results => results;

        internal ImageEvaluationSession(ExecutionPlan plan, CancellationToken token)
        {
            splitBatches = plan.Steps.Where(step => step.Operation?.SplitChannel >= 0)
                .GroupBy(step => step.Id).Where(group => group.Count() == 4)
                .ToDictionary(group => group.Key, group => group.OrderBy(step => step.OutputIndex).Select(step => step.Operation).ToArray());
            try
            {
                using var assetQueries = new ImageAssetQueryScope();
                // Snapshot every external input on the GPU before any asynchronous work starts.
                foreach (var source in plan.Sources)
                {
                    token.ThrowIfCancellationRequested();
                    if (source.ReferenceId != null) continue;
                    if (source.Reference == null) throw new InvalidOperationException("An image source is missing.");
                    if (sources.ContainsKey(source.Reference)) continue;
                    sources.Add(source.Reference, ImageComputeDevice.Import(source.Reference, ImageInputSnapshot.GetVersion(source.Reference)));
                }
            }
            catch { Dispose(); throw; }
        }

        internal GpuImage Resolve(ImageSource source) => source.ReferenceId == null ? sources[source.Reference] : results[source.ReferenceId];

        internal void Execute(PlanStep step, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string key = ExecutionPlan.ResultKey(step.Id, step.OutputIndex);
            if (step.Operation.SplitChannel >= 0 && results.ContainsKey(key)) return;
            var images = step.Images.Select(Resolve).ToArray();
            if (splitBatches.Remove(step.Id, out var operations))
            {
                var batch = ImageComputeDevice.ExecuteChannelSplit(operations, images[0]);
                for (int index = 0; index < batch.Length; index++) results.Add(ExecutionPlan.ResultKey(step.Id, index), batch[index]);
            }
            else results.Add(key, ImageComputeDevice.Execute(step.Operation, images));
        }

        internal void AddGenerated(string key, Texture2D texture, byte[] png)
        {
            var snapshot = ImageInputSnapshot.FromPng(texture, png);
            results.Add(key, ImageComputeDevice.Import(texture, snapshot.ContentHash));
            snapshots[results[key].Version] = snapshot;
        }

        internal async Task<ImageInputSnapshot> SnapshotAsync(ImageSource source, CancellationToken token)
        {
            var image = Resolve(source);
            if (snapshots.TryGetValue(image.Version, out var existing)) return existing;
            var png = await ImageComputeDevice.ReadPngAsync(image, token);
            var snapshot = ImageInputSnapshot.FromGpu(png, image.Width, image.Height, image.HasAlpha,
                source.Reference == null ? "" : UnityEditor.AssetDatabase.GetAssetPath(source.Reference), image.Version);
            snapshots.Add(image.Version, snapshot);
            return snapshot;
        }

        public void Dispose()
        {
            foreach (var image in sources.Values) image.Dispose();
            foreach (var image in results.Values) image.Dispose();
            sources.Clear(); results.Clear(); snapshots.Clear();
        }
    }
}
