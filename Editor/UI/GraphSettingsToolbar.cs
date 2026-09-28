using System;
using System.Collections.Generic;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tsukuyomi.AiCanvas.Editor
{
#if UNITY_6000_6_OR_NEWER
    [GraphToolbarElement("AiCanvas/GraphSettings", typeof(AiCanvasGraph), 198)]
#endif
    public sealed class GraphSettingsToolbar : EditorToolbarDropdown, IAccessContainerWindow
    {
        public EditorWindow containerWindow { get; set; }

        public GraphSettingsToolbar()
        {
            name = "ai-canvas-graph-settings";
            text = "Graph Settings";
            tooltip = "配置当前 graph 的全局设置。";
            clicked += () =>
            {
                if (GraphToolkitCompatibility.GetGraph(containerWindow) != null)
                    UnityEditor.PopupWindow.Show(worldBound, new GraphSettingsPopup(containerWindow));
            };
        }
    }

    internal sealed class GraphSettingsPopup : PopupWindowContent
    {
        private readonly EditorWindow owner;
        internal GraphSettingsPopup(EditorWindow owner) { this.owner = owner; }
        public override Vector2 GetWindowSize() => new(340, 164);
        public override void OnOpen() => editorWindow.rootVisualElement.Add(new GraphSettingsPanel(() =>
            GraphToolkitCompatibility.GetGraph(owner)));
        public override void OnGUI(Rect rect) { }
    }

    // Resolve the graph from its window on every action: Undo may replace the graph instance.
    internal sealed class GraphSettingsPanel : VisualElement
    {
        private readonly Func<AiCanvasGraph> getGraph;
        private readonly Label graphName;
        private readonly DropdownField workingSpace;

        internal GraphSettingsPanel(Func<AiCanvasGraph> getGraph)
        {
            this.getGraph = getGraph;
            name = "ai-canvas-graph-settings-panel";
            style.paddingLeft = style.paddingRight = 12;
            style.paddingTop = style.paddingBottom = 10;
            var title = new Label("Graph Settings");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            Add(title);
            graphName = new Label(); graphName.style.marginBottom = 8; Add(graphName);
            workingSpace = new DropdownField("Working Space", new List<string> { "sRGB", "Linear" }, 0)
            {
                name = "graph-working-space",
                tooltip = "统一设置所有图像节点的工作空间。数据贴图和 Alpha 保留原始数值。"
            };
            workingSpace.labelElement.style.minWidth = 0;
            workingSpace.labelElement.style.width = 108;
            workingSpace.RegisterValueChangedCallback(evt =>
            {
                var graph = getGraph();
                if (graph == null) return;
                var settings = graph.Settings;
                settings.workingSpace = evt.newValue == "sRGB" ? ImageWorkingSpace.SRgb : ImageWorkingSpace.Linear;
                graph.SetSettings(settings);
                Refresh();
            });
            Add(workingSpace);
            var description = new Label("统一用于色阶、缩放与混合。数据贴图和 Alpha 保留原始数值。");
            description.style.fontSize = 10;
            description.style.whiteSpace = WhiteSpace.Normal;
            description.style.marginTop = 6;
            Add(description);
            var note = new Label("设置随 graph 保存，支持 Undo / Redo。");
            note.style.fontSize = 10; note.style.marginTop = 6; Add(note);
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                AiCanvasGraph.Changed += OnGraphChanged;
                Undo.undoRedoPerformed += Refresh;
                Refresh();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                AiCanvasGraph.Changed -= OnGraphChanged;
                Undo.undoRedoPerformed -= Refresh;
            });
            Refresh();
        }

        private void OnGraphChanged(AiCanvasGraph graph) { if (ReferenceEquals(graph, getGraph())) Refresh(); }
        private void Refresh()
        {
            var graph = getGraph();
            SetEnabled(graph != null);
            graphName.text = graph?.Name ?? "No graph";
            workingSpace.SetValueWithoutNotify(graph?.Settings.workingSpace == ImageWorkingSpace.SRgb ? "sRGB" : "Linear");
        }
    }
}
