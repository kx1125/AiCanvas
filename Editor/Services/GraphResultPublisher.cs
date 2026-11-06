using System;
using System.Collections.Generic;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    internal static class GraphResultPublisher
    {
        internal static bool Publish(string graphGuid, string targetNodeId, ExecutionPlan plan,
            string fingerprint, IReadOnlyDictionary<string, Texture2D> results, string providerName, ImageEvaluationSession session)
        {
            var graphPath = AssetDatabase.GUIDToAssetPath(graphGuid);
            var graph = string.IsNullOrEmpty(graphPath) ? null : GraphDatabase.LoadGraph<AiCanvasGraph>(graphPath);
            if (graph == null)
                throw new InvalidOperationException("The graph was deleted. Generated files were retained.");
            try
            {
                if (ImageExecutionPlanBuilder.Build(graph, targetNodeId).Fingerprint != fingerprint) return false;
            }
            catch (InvalidOperationException) { return false; }

            // Local results live only in the GPU preview store. They never become graph asset references.
            if (!plan.HasGeneration)
            {
                ImagePreviewStore.Publish(graph, plan, session, targetNodeId == null);
                return true;
            }
            graph.UndoBeginRecordGraph("Attach AiCanvas results");
            try
            {
                foreach (var node in graph.GetNodes())
                {
                    var id = node.GetStableId().ToString();
                    if (node is GenerateImageNode generate)
                        for (int index = 0; index < generate.Settings.OutputCount; index++)
                            if (results.TryGetValue(ExecutionPlan.ResultKey(id, index), out var generated))
                            {
                                var size = new ImageSourcePixels(generated).Size;
                                GraphOptionAccess.Set(node, GenerateImageNode.ResultName(index), new ImageCard(generated,
                                    $"Image {index + 1} | {providerName} | {size.x} x {size.y}"));
                            }
                    if (node is ImageOutputNode && plan.Outputs.TryGetValue(id, out var source))
                    {
                        Texture2D texture = source.Reference;
                        if (source.ReferenceId != null) results.TryGetValue(source.ReferenceId, out texture);
                        GraphOptionAccess.Set(node, "Preview", new ImageCard(texture,
                            texture == null ? "Local GPU preview" : texture.name, id));
                    }
                }
            }
            finally { graph.UndoEndRecordGraph(); }
            GraphDatabase.SaveGraph(graph);
            ImagePreviewStore.Publish(graph, plan, session, targetNodeId == null);
            return true;
        }
    }
}
