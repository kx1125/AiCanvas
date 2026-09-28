using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.GraphToolkit.Editor;

namespace Tsukuyomi.AiCanvas.Editor
{
    internal static class ImagePreviewStore
    {
        private sealed class PreviewSet : IDisposable
        {
            internal readonly Dictionary<string, GpuImage> Images = new();
            internal readonly Dictionary<string, GpuImage> Inputs = new();
            internal readonly Dictionary<string, string> Errors = new();
            internal string Error;
            internal string Fingerprint;
            public void Dispose()
            {
                foreach (var image in Images.Values) image.Dispose(); Images.Clear();
                foreach (var image in Inputs.Values) image.Dispose(); Inputs.Clear();
                Errors.Clear(); Fingerprint = null;
            }
        }
        private static readonly Dictionary<string, PreviewSet> Graphs = new();
        private sealed class Identity { internal string Key; }
        private static readonly ConditionalWeakTable<AiCanvasGraph, Identity> Identities = new();
        // null means all graphs were cleared; otherwise notify only observers of this identity.
        internal static event Action<string> Changed;
        internal static string OutputKey(string nodeId, int index) => index == 0 ? nodeId : ExecutionPlan.ResultKey(nodeId, index);
        internal static string Key(AiCanvasGraph graph)
        {
            return Identities.GetValue(graph, value =>
            {
                var guid = GraphDatabase.GetGraphAssetGUID(value).ToString();
                return new Identity { Key = !string.IsNullOrEmpty(guid) && guid.Any(c => c != '0')
                    ? "asset:" + guid : "memory:" + RuntimeHelpers.GetHashCode(value) };
            }).Key;
        }

        internal static bool IsCurrent(AiCanvasGraph graph, string fingerprint)
            => Graphs.TryGetValue(Key(graph), out var set) && set.Fingerprint == fingerprint;

        internal static void Invalidate(AiCanvasGraph graph)
        {
            if (Graphs.TryGetValue(Key(graph), out var set)) set.Fingerprint = null;
        }

        internal static GpuImage Find(AiCanvasGraph graph, string nodeId, out string error)
        {
            error = null;
            if (graph == null || !Graphs.TryGetValue(Key(graph), out var set)) return null;
            error = set.Errors.TryGetValue(nodeId, out var nodeError) ? nodeError : set.Error;
            return set.Images.TryGetValue(nodeId, out var image) ? image : null;
        }

        internal static GpuImage FindInput(AiCanvasGraph graph, string nodeId, out string error)
        {
            error = null;
            if (graph == null || string.IsNullOrEmpty(nodeId) || !Graphs.TryGetValue(Key(graph), out var set)) return null;
            error = set.Errors.TryGetValue(nodeId, out var nodeError) ? nodeError : set.Error;
            return set.Inputs.TryGetValue(nodeId, out var image) ? image : null;
        }

        internal static void Publish(AiCanvasGraph graph, ExecutionPlan plan, ImageEvaluationSession session, bool replace)
        {
            string key = Key(graph);
            if (!Graphs.TryGetValue(key, out var set)) Graphs.Add(key, set = new PreviewSet());
            if (replace) set.Dispose();
            set.Error = null;
            set.Fingerprint = replace && !plan.HasGeneration ? plan.Fingerprint : null;
            void Set(string id, GpuImage image)
            {
                if (set.Images.TryGetValue(id, out var previous)) previous.Dispose();
                set.Images[id] = image.Retain();
                set.Errors.Remove(id);
            }
            var levels = graph.GetNodes().OfType<LevelsNode>().Select(node => node.GetStableId().ToString()).ToHashSet();
            foreach (var step in plan.Steps.Where(step => step.Kind == ImageStepKind.Compute))
            {
                Set(OutputKey(step.Id, step.OutputIndex), session.Results[ExecutionPlan.ResultKey(step.Id, step.OutputIndex)]);
                if (!levels.Contains(step.Id)) continue;
                if (set.Inputs.TryGetValue(step.Id, out var previous)) previous.Dispose();
                set.Inputs[step.Id] = session.Resolve(step.Images[0]).Retain();
            }
            foreach (var output in plan.Outputs) Set(output.Key, session.Resolve(output.Value));
            foreach (var failure in plan.PreviewErrors)
            {
                if (set.Images.Remove(failure.Key, out var image)) image.Dispose();
                if (set.Inputs.Remove(failure.Key, out var input)) input.Dispose();
                set.Errors[failure.Key] = failure.Value;
            }
            EditorEvent.Invoke(Changed, key);
        }

        internal static void Fail(AiCanvasGraph graph, string error)
        {
            string key = Key(graph);
            if (!Graphs.TryGetValue(key, out var set)) Graphs.Add(key, set = new PreviewSet());
            set.Dispose(); set.Error = error; EditorEvent.Invoke(Changed, key);
        }

        internal static void Clear(AiCanvasGraph graph)
            => Clear(Key(graph));

        internal static void Clear(string key)
        {
            if (Graphs.Remove(key, out var set)) { set.Dispose(); EditorEvent.Invoke(Changed, key); }
        }
        internal static void ClearAll()
        {
            foreach (var set in Graphs.Values) set.Dispose();
            Graphs.Clear(); EditorEvent.Invoke(Changed, (string)null);
        }
    }
}
