using System;
using System.Collections.Generic;
using System.Linq;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tsukuyomi.AiCanvas.Editor
{
    // The only global window discovery loop. Existing windows refresh their adapters when dirty.
    [InitializeOnLoad]
    internal static class AiCanvasWindowRegistry
    {
        private static readonly Dictionary<EditorWindow, WindowBinding> Windows = new();
        private static double nextDiscovery;

        static AiCanvasWindowRegistry()
        {
            EditorApplication.update += Update;
            AiCanvasGraph.Changed += OnGraphChanged;
            Undo.undoRedoPerformed += RefreshAll;
            ImageAssetStore.ImageImported += RefreshImages;
        }

        private static void Update()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextDiscovery) return;
            nextDiscovery = EditorApplication.timeSinceStartup + 0.5;
            Discover();
            // Only cached draft adapters are touched here; no repeated graph/tree traversal.
            PromptFieldCompatibility.RefreshDrafts();
        }

        internal static void Discover()
        {
            foreach (var entry in Windows.ToArray())
                if (!ReferenceEquals(GraphToolkitCompatibility.GetGraph(entry.Key), entry.Value.Graph))
                {
                    entry.Value.Dispose();
                    Windows.Remove(entry.Key);
                }
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
                if (GraphToolkitCompatibility.GetGraph(window) is AiCanvasGraph graph && !Windows.ContainsKey(window))
                {
                    var binding = new WindowBinding(window, graph);
                    Windows.Add(window, binding);
                    ImageInputConnections.UpgradeLegacy(graph);
                    binding.Schedule();
                }
        }

        internal static AiCanvasGraph FindGraph(VisualElement element)
        {
            var window = Windows.Keys.FirstOrDefault(candidate => candidate != null && candidate.rootVisualElement.Contains(element));
            if (window == null)
            {
                Discover();
                window = Windows.Keys.FirstOrDefault(candidate => candidate != null && candidate.rootVisualElement.Contains(element));
            }
            return GraphToolkitCompatibility.GetGraph(window);
        }

        private static void OnGraphChanged(AiCanvasGraph graph)
        {
            foreach (var binding in Windows.Values)
                if (ReferenceEquals(binding.Graph, graph)) binding.Schedule();
        }

        private static void RefreshAll()
        {
            foreach (var binding in Windows.Values) binding.Schedule();
        }

        private static void RefreshImages(Texture2D texture)
        {
            foreach (var window in Windows.Keys)
            {
                if (window == null) continue;
                foreach (var image in window.rootVisualElement.Query<Image>().ToList())
                    if (image.image == texture) image.MarkDirtyRepaint();
                window.Repaint();
            }
        }

        private sealed class WindowBinding : IDisposable
        {
            private readonly EditorWindow window;
            private readonly VisualElement root;
            private bool pending, disposed;
            internal readonly AiCanvasGraph Graph;

            internal WindowBinding(EditorWindow window, AiCanvasGraph graph)
            {
                this.window = window;
                Graph = graph;
                ImageLocalPreview.Track(graph);
                root = window.rootVisualElement;
                root.RegisterCallback<AttachToPanelEvent>(OnAttach, TrickleDown.TrickleDown);
                root.RegisterCallback<GeometryChangedEvent>(OnGeometry);
            }

            private void OnAttach(AttachToPanelEvent evt) => Schedule();
            private void OnGeometry(GeometryChangedEvent evt) => Schedule();

            internal void Schedule()
            {
                if (pending || disposed) return;
                pending = true;
                root.schedule.Execute(() =>
                {
                    pending = false;
                    if (disposed || !ReferenceEquals(GraphToolkitCompatibility.GetGraph(window), Graph)) return;
                    GraphToolkitCompatibility.AttachToolbar(window);
                    AiCanvasAssetDrop.Attach(window);
                    PromptFieldCompatibility.Attach(window, Graph);
                });
            }

            public void Dispose()
            {
                disposed = true;
                ImageLocalPreview.Untrack(Graph);
                GraphToolkitCompatibility.RemoveToolbar(window);
                root.UnregisterCallback<AttachToPanelEvent>(OnAttach, TrickleDown.TrickleDown);
                root.UnregisterCallback<GeometryChangedEvent>(OnGeometry);
            }
        }
    }

    internal static class AiCanvasUiBinding
    {
        internal static void BindRunner(VisualElement element, Action refresh)
            => Bind(element, refresh, callback => ImageGraphRunner.StateChanged += callback,
                callback => ImageGraphRunner.StateChanged -= callback);

        internal static void BindStatus(VisualElement element, Action refresh)
            => Bind(element, refresh, callback =>
            {
                ImageGraphRunner.StateChanged += callback;
                CodexConnection.Changed += callback;
                OpenAIImageSettings.Changed += callback;
            }, callback =>
            {
                ImageGraphRunner.StateChanged -= callback;
                CodexConnection.Changed -= callback;
                OpenAIImageSettings.Changed -= callback;
            });

        private static void Bind(VisualElement element, Action refresh, Action<Action> subscribe, Action<Action> unsubscribe)
        {
            bool subscribed = false;
            void Attach()
            {
                if (!subscribed) { subscribe(refresh); subscribed = true; }
                refresh();
            }
            element.RegisterCallback<AttachToPanelEvent>(_ => Attach());
            element.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (subscribed) unsubscribe(refresh);
                subscribed = false;
            });
            if (element.panel != null) Attach(); else refresh();
        }
    }
}
