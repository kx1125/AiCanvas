using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    [InitializeOnLoad]
    public static class ImageGraphRunner
    {
        private static CancellationTokenSource activeCancellation;
        public static bool IsRunning => activeCancellation != null;
        public static string ActiveNodeId { get; private set; }
        public static string Status { get; private set; } = "Ready";
        internal static string StatusGraphGuid { get; private set; }
        public static IReadOnlyList<string> LastSavedFiles { get; private set; } = Array.Empty<string>();
        public static event Action StateChanged;

        static ImageGraphRunner()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Cancel;
            EditorApplication.quitting += Cancel;
        }

        public static void Cancel() => activeCancellation?.Cancel();

        public static Task<IReadOnlyList<string>> RunAsync(AiCanvasGraph graph,
            string outputFolder = AiCanvasResourcePaths.Generated,
            CancellationToken cancellationToken = default)
            => RunInternalAsync(graph, null, outputFolder, cancellationToken);

        public static Task<IReadOnlyList<string>> RunNodeAsync(AiCanvasGraph graph, Hash128 nodeId,
            string outputFolder = AiCanvasResourcePaths.Generated,
            CancellationToken cancellationToken = default)
            => RunInternalAsync(graph, nodeId.ToString(), outputFolder, cancellationToken);

        internal static async Task<IReadOnlyList<string>> RunInternalAsync(AiCanvasGraph graph, string targetNodeId,
            string outputFolder, CancellationToken cancellationToken, Func<IImageProvider> providerFactory = null)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (IsRunning) throw new InvalidOperationException("An AiCanvas task is already running.");
            LastSavedFiles = Array.Empty<string>();
            outputFolder = ImageAssetStore.ValidateFolder(outputFolder);
            ImageInputConnections.UpgradeLegacy(graph);
            var plan = ImageExecutionPlanBuilder.Build(graph, targetNodeId);
            var fingerprint = plan.Fingerprint;
            var graphPath = GraphDatabase.GetGraphAssetPath(graph);
            var graphGuid = GraphDatabase.GetGraphAssetGUID(graph).ToString();
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            activeCancellation = cancellation;
            ActiveNodeId = targetNodeId;
            StatusGraphGuid = graphGuid;
            var files = new List<string>();
            LastSavedFiles = files.AsReadOnly();
            var results = new Dictionary<string, Texture2D>();
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var provider = plan.HasGeneration ? (providerFactory ?? OpenAIImageSettings.CreateProvider)() : null;
                SetStatus("Preparing image inputs");
                using var session = new ImageEvaluationSession(plan, cancellation.Token);
                int completedSteps = 0;
                foreach (var step in plan.Steps)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    SetStatus($"Running {++completedSteps}/{plan.Steps.Count} - {(step.Kind == ImageStepKind.Compute ? "GPU" : provider.Name)}");
                    if (step.Kind == ImageStepKind.Compute)
                    {
                        session.Execute(step, cancellation.Token);
                        continue;
                    }
                    var inputs = new List<ImageInputData>();
                    foreach (var input in step.Inputs)
                        inputs.Add(new ImageInputData(await session.SnapshotAsync(input.Source, cancellation.Token),
                            input.Role, input.Prompt, input.NodeId));
                    var request = new ImageRequest(step.Prompt, inputs, step.Settings);
                    string batchId = Guid.NewGuid().ToString("N");
                    await ImageBatchDelivery.GenerateAsync(provider, request, (index, png) =>
                    {
                        var path = ImageAssetStore.GeneratedPath(outputFolder, batchId, index);
                        var texture = ImageAssetStore.SavePng(path, png);
                        var key = ExecutionPlan.ResultKey(step.Id, index);
                        results.Add(key, texture);
                        files.Add(path);
                        ImageAssetStore.SaveRecord(path, ImageRunRecord.Create(request, provider.Name,
                            graphPath, step.Id, targetNodeId == null ? "graph" : "node", batchId, index, path, texture));
                        session.AddGenerated(key, texture, png);
                        SetStatus($"Saved {index + 1}/{request.Count} - {provider.Name}");
                    }, cancellation.Token);
                }
                cancellation.Token.ThrowIfCancellationRequested();
                bool attached = GraphResultPublisher.Publish(graphGuid, targetNodeId, plan, fingerprint, results,
                    provider?.Name ?? "Local GPU", session);
                SetStatus(attached ? (plan.HasGeneration ? $"Completed - {files.Count} image(s) saved" : "Completed - local GPU previews updated") :
                    "Inputs changed - generated files retained; run again to update previews");
                return files;
            }
            catch (OperationCanceledException)
            {
                SetStatus($"Cancelled - {files.Count} completed image(s) retained");
                throw;
            }
            catch (Exception exception)
            {
                SetStatus($"Failed - {files.Count} completed image(s) retained: " + exception.Message);
                throw;
            }
            finally
            {
                activeCancellation = null;
                ActiveNodeId = null;
                NotifyStateChanged();
            }
        }

        internal static string StatusForGraph(string path)
        {
            if (!string.IsNullOrEmpty(path) && string.Equals(AssetDatabase.AssetPathToGUID(path), StatusGraphGuid, StringComparison.Ordinal))
                return IsRunning ? Status : "上次运行：" + Status;
            return IsRunning ? "其他图正在生成" : "就绪";
        }

        private static void SetStatus(string status)
        {
            Status = status;
            NotifyStateChanged();
        }

        private static void NotifyStateChanged()
        {
            // A stale UI observer must never turn a successful task into a failed one.
            EditorEvent.Invoke(StateChanged);
        }
    }
}
