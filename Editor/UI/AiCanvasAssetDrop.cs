using System;
using System.Collections.Generic;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Tsukuyomi.AiCanvas.Editor
{
    // Graph Toolkit 6000.6 has no public graph-window drop/coordinate hook.
    // Keep its named visual-element lookup here; all events, transforms, graph
    // changes and Undo use the shared Graph Toolkit compatibility layer.
    internal static class AiCanvasAssetDrop
    {
        private const string AttachedClass = "ai-canvas-asset-drop";

        internal static void Attach(EditorWindow window)
        {
            var root = window.rootVisualElement.Q("graphContainer");
            if (root == null || root.ClassListContains(AttachedClass)) return;
            root.AddToClassList(AttachedClass);
            // Intercept before the built-in graph asset-drop manipulator.
            root.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                if (!TryGetCanvas(root, evt.target, evt.mousePosition, out _)) return;
                if (GetTextures(DragAndDrop.objectReferences, DragAndDrop.paths).Count == 0) return;
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            root.RegisterCallback<DragPerformEvent>(evt =>
            {
                if (GraphToolkitCompatibility.GetGraph(window) is not AiCanvasGraph graph ||
                    !TryGetCanvas(root, evt.target, evt.mousePosition, out var content)) return;
                var textures = GetTextures(DragAndDrop.objectReferences, DragAndDrop.paths);
                if (textures.Count == 0) return;
                var position = content.WorldToLocal(evt.mousePosition);
                evt.StopImmediatePropagation();
                DragAndDrop.AcceptDrag();
                CreateNodes(graph, textures, position);
            }, TrickleDown.TrickleDown);
        }

        private static bool TryGetCanvas(VisualElement root, IEventHandler eventTarget,
            Vector2 panelPosition, out VisualElement content)
        {
            content = root.Q("content-view-container");
            var viewport = root.Q("graph-view-container");
            if (content == null || viewport == null || !viewport.worldBound.Contains(panelPosition)) return false;
            var target = eventTarget as VisualElement;
            if (target == null || (target != viewport.parent && target != viewport && !viewport.Contains(target))) return false;
            // Retain normal assignment when dropping onto an existing asset field.
            for (var current = target; current != null && current != root; current = current.parent)
                if (current is ObjectField || current is TextField || current is IMGUIContainer) return false;
            return true;
        }

        internal static List<Texture2D> GetTextures(IEnumerable<Object> objects, IEnumerable<string> paths)
        {
            var result = new List<Texture2D>();
            var seen = new HashSet<Texture2D>();
            void Add(Texture2D texture)
            {
                if (texture != null && AssetDatabase.Contains(texture) && seen.Add(texture)) result.Add(texture);
            }
            if (objects != null)
                foreach (var asset in objects)
                {
                    if (asset is Texture2D texture) Add(texture);
                    else if (asset is Sprite sprite)
                        Add(AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GetAssetPath(sprite)));
                }
            if (paths != null)
                foreach (var rawPath in paths)
                {
                    var path = rawPath?.Replace('\\', '/');
                    if (path != null && (path.StartsWith("Assets/", StringComparison.Ordinal) ||
                                         path.StartsWith("Packages/", StringComparison.Ordinal)))
                        Add(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
                }
            return result;
        }

        internal static IReadOnlyList<ImageNode> CreateNodes(AiCanvasGraph graph,
            IReadOnlyList<Texture2D> textures, Vector2 position)
        {
            var nodes = new List<ImageNode>();
            if (textures.Count == 0) return nodes;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            string undoName = textures.Count == 1 ? "Add Image node" : "Add Image nodes";
            Undo.SetCurrentGroupName(undoName);
            graph.UndoBeginRecordGraph(undoName);
            try
            {
                for (int index = 0; index < textures.Count; index++)
                {
                    var texture = textures[index];
                    var node = new ImageNode { Position = position + new Vector2(index % 3 * 380, index / 3 * 360) };
                    graph.AddNode(node);
                    GraphOptionAccess.Set(node, "Image",
                        new ImageCard(texture, $"{texture.name} | {texture.width} x {texture.height}"));
                    nodes.Add(node);
                }
            }
            finally
            {
                graph.UndoEndRecordGraph();
                Undo.CollapseUndoOperations(group);
                Undo.IncrementCurrentGroup();
            }
            GraphDatabase.SaveGraph(graph);
            return nodes;
        }
    }
}
