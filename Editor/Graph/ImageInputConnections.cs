using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    internal static class ImageInputConnections
    {
        internal static ImageInputNode Connect(AiCanvasGraph graph, INode source, GenerateImageNode target,
            ImageInputRole role = ImageInputRole.Reference, string prompt = "")
        {
            var node = new ImageInputNode { Position = (source.Position + target.Position) * 0.5f + new Vector2(0, 180) };
            graph.AddNode(node);
            try
            {
                GraphOptionAccess.Set(node, "Role", new ImageRoleOption(role));
                node.GetInputPortByName("Prompt").TrySetValue(prompt);
                if (!graph.Connect(source.GetOutputPortByName("Image"), node.GetInputPortByName("Image")) ||
                    !graph.Connect(node.GetOutputPortByName("Input"), target.GetInputPortByName("Reference")))
                    throw new InvalidOperationException("Could not connect Image Input.");
            }
            catch { graph.RemoveNode(node); throw; }
            return node;
        }

        internal static int UpgradeLegacy(AiCanvasGraph graph)
        {
            var changes = new List<(GenerateImageNode target, List<IPort> ports)>();
            foreach (var target in graph.GetNodes().OfType<GenerateImageNode>())
            {
                var ports = new List<IPort>();
                target.GetInputPortByName("Reference").GetConnectedPorts(ports);
                if (ports.Any(port => port.DataType == typeof(Texture2D))) changes.Add((target, ports));
            }
            if (changes.Count == 0) return 0;
            // Retain the original serialized graph before the first automatic upgrade.
            var path = GraphDatabase.GetGraphAssetPath(graph);
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                const string folder = "Library/AiCanvas/MigrationBackups";
                Directory.CreateDirectory(folder);
                var backup = folder + "/" + GraphDatabase.GetGraphAssetGUID(graph) + "-before-image-input.aicanvas";
                if (!File.Exists(backup)) File.Copy(path, backup);
            }
            int count = 0;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            bool succeeded = false;
            graph.UndoBeginRecordGraph("Upgrade AiCanvas image inputs");
            try
            {
                foreach (var change in changes)
                {
                    var destination = change.target.GetInputPortByName("Reference");
                    foreach (var port in change.ports) graph.Disconnect(port, destination);
                    foreach (var port in change.ports)
                    {
                        if (port.DataType != typeof(Texture2D)) { graph.Connect(port, destination); continue; }
                        var source = graph.GetNodes().First(node => node.GetOutputPorts().Any(output => output.GetStableId() == port.GetStableId()));
                        var adapter = Connect(graph, source, change.target);
                        adapter.Position += new Vector2(0, count * 60);
                        count++;
                    }
                }
                succeeded = true;
            }
            finally
            {
                graph.UndoEndRecordGraph();
                Undo.FlushUndoRecordObjects();
                if (!succeeded) Undo.RevertAllDownToGroup(undoGroup);
                Undo.IncrementCurrentGroup();
            }
            return count;
        }
    }
}
