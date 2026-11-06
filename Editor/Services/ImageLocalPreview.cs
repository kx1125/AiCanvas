using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    [InitializeOnLoad]
    internal static class ImageLocalPreview
    {
        private sealed class Tracking
        {
            internal int References = 1;
            internal string Key, Path;
            internal bool Deleted;
            internal readonly HashSet<string> Dependencies = new(StringComparer.OrdinalIgnoreCase);
        }
        private const double RefreshInterval = 1.0 / 30.0;
        private static readonly Dictionary<AiCanvasGraph, Tracking> Tracked = new();
        private static readonly Dictionary<AiCanvasGraph, double> Pending = new();

        static ImageLocalPreview()
        {
            AiCanvasGraph.Changed += Schedule;
            Undo.undoRedoPerformed += ScheduleAll;
            ImageGraphRunner.StateChanged += () => { if (!ImageGraphRunner.IsRunning) ScheduleAll(); };
            EditorApplication.update += Update;
        }

        internal static void Track(AiCanvasGraph graph)
        {
            if (Tracked.TryGetValue(graph, out var tracking)) tracking.References++;
            else Tracked.Add(graph, new Tracking { Key = ImagePreviewStore.Key(graph), Path = GraphDatabase.GetGraphAssetPath(graph) });
            Schedule(graph);
        }

        internal static void Untrack(AiCanvasGraph graph)
        {
            if (!Tracked.TryGetValue(graph, out var tracking)) return;
            if (--tracking.References > 0) return;
            Tracked.Remove(graph); Pending.Remove(graph);
            // Different windows may temporarily hold different instances of the same saved graph.
            if (!Tracked.Values.Any(value => value.Key == tracking.Key)) ImagePreviewStore.Clear(tracking.Key);
        }

        internal static void Schedule(AiCanvasGraph graph)
        {
            // Throttle rather than debounce, so continuous dragging still produces previews.
            if (Tracked.TryGetValue(graph, out var tracking) && !tracking.Deleted && !Pending.ContainsKey(graph))
                Pending.Add(graph, EditorApplication.timeSinceStartup + RefreshInterval);
        }

        internal static void ScheduleAll() { foreach (var graph in Tracked.Keys) Schedule(graph); }

        internal static void AssetsChanged(IEnumerable<string> paths, IEnumerable<string> deleted)
        {
            var changed = paths.ToArray();
            var removed = deleted.ToArray();
            foreach (var pair in Tracked.ToArray())
            {
                var tracking = pair.Value;
                if (removed.Any(path => ContainsPath(path, tracking.Path)))
                {
                    tracking.Deleted = true;
                    Pending.Remove(pair.Key);
                    ImagePreviewStore.Clear(tracking.Key);
                    continue;
                }
                if (tracking.Deleted)
                {
                    if (string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(GraphDatabase.GetGraphAssetGUID(pair.Key).ToString()))) continue;
                    tracking.Deleted = false;
                }
                if (tracking.Dependencies.Count == 0 || changed.Any(path => ContainsPath(path, tracking.Path) ||
                    tracking.Dependencies.Any(dependency => ContainsPath(path, dependency))))
                {
                    ImagePreviewStore.Invalidate(pair.Key);
                    Schedule(pair.Key);
                }
            }
        }

        private static bool ContainsPath(string parent, string child)
            => !string.IsNullOrEmpty(child) && (string.Equals(parent, child, StringComparison.OrdinalIgnoreCase) ||
                child.StartsWith(parent.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));

        private static void Update()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || ImageGraphRunner.IsRunning) return;
            foreach (var pair in Pending.ToArray())
                if (pair.Value <= EditorApplication.timeSinceStartup) { Pending.Remove(pair.Key); Refresh(pair.Key); }
        }

        internal static bool Refresh(AiCanvasGraph graph)
        {
            try
            {
                var plan = ImageExecutionPlanBuilder.Build(graph, mode: ImageExecutionMode.LocalPreview);
                if (Tracked.TryGetValue(graph, out var tracking)) CaptureDependencies(graph, plan, tracking);
                if (ImagePreviewStore.IsCurrent(graph, plan.Fingerprint)) return plan.PreviewErrors.Count == 0;
                using var session = new ImageEvaluationSession(plan, CancellationToken.None);
                foreach (var step in plan.Steps) session.Execute(step, CancellationToken.None);
                ImagePreviewStore.Publish(graph, plan, session, true);
                return plan.PreviewErrors.Count == 0;
            }
            catch (Exception exception) { ImagePreviewStore.Fail(graph, exception.Message); return false; }
        }

        private static void CaptureDependencies(AiCanvasGraph graph, ExecutionPlan plan, Tracking tracking)
        {
            tracking.Path = GraphDatabase.GetGraphAssetPath(graph);
            tracking.Dependencies.Clear();
            void Add(UnityEngine.Object asset)
            {
                string path = AssetDatabase.GetAssetPath(asset);
                if (string.IsNullOrEmpty(path)) return;
                if (!tracking.Dependencies.Add(path)) return;
                foreach (var dependency in AssetDatabase.GetDependencies(path)) tracking.Dependencies.Add(dependency);
            }
            foreach (var source in plan.Sources) if (source.Reference != null) Add(source.Reference);
            foreach (var step in plan.Steps) if (step.Operation != null) Add(step.Operation.Shader);
            // Include inputs on incomplete branches so fixing an asset can make its preview valid.
            foreach (var node in graph.GetNodes())
            {
                foreach (var input in node.GetInputPorts())
                    if (input.DataType == typeof(Texture2D) && input.TryGetValue<Texture2D>(out var texture) && texture != null) Add(texture);
                if (node is ImageNode) Add(GraphOptionAccess.Read<ImageCard>(node, "Image").texture);
                if (node is CameraColorNode camera) Add(camera.Frame.texture);
                if (node is GenerateImageNode generate)
                    for (int index = 0; index < generate.Settings.OutputCount; index++)
                        Add(GraphOptionAccess.Read<ImageCard>(node, GenerateImageNode.ResultName(index)).texture);
            }
            tracking.Dependencies.Add(ImageComputeDevice.TransferPath);
            tracking.Dependencies.Add(ImageFilterShader.Path);
            tracking.Dependencies.Add(ImageFilterShader.MaterialPath);
            tracking.Dependencies.Add(ImageFilterShader.ProceduralPath);
            tracking.Dependencies.Add(ImageHistogram.ShaderPath);
        }
    }

    internal sealed class ImagePreviewAssetChanges : AssetPostprocessor
    {
        internal static event Action SourceFilesChanged;

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            ImageLocalPreview.AssetsChanged(imported.Concat(deleted).Concat(moved).Concat(movedFrom), deleted);
            if (SourceFilesChanged == null)
                return;

            // Reopen visible source images after the importer has finished updating the asset database.
            EditorApplication.delayCall -= NotifySourceFilesChanged;
            EditorApplication.delayCall += NotifySourceFilesChanged;
        }

        private static void NotifySourceFilesChanged()
        {
            EditorEvent.Invoke(SourceFilesChanged);
        }
    }
}
