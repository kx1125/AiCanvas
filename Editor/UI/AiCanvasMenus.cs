using System;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tsukuyomi.AiCanvas.Editor
{
    public static class AiCanvasMenus
    {
        [MenuItem("Assets/Create/AiCanvas/Graph", priority = 300)]
        [MenuItem("Tools/AiCanvas/Create Graph")]
        public static void CreateEmpty()
        {
            AiCanvasResourcePaths.EnsureFolder(AiCanvasResourcePaths.Graphs);
            string path = AssetDatabase.GenerateUniqueAssetPath(
                AiCanvasResourcePaths.Graphs + "/New AiCanvas." + AiCanvasGraph.Extension);
            CreateGraph(path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            Selection.activeObject = asset;
            AssetDatabase.OpenAsset(asset);
        }

        internal static AiCanvasGraph CreateGraph(string path)
        {
            var graph = GraphDatabase.CreateGraph<AiCanvasGraph>(path);
            graph.SetSettings(AiCanvasGraphSettings.Default);
            GraphDatabase.SaveGraph(graph);
            return graph;
        }

        [MenuItem("Tools/AiCanvas/Run on Active Graph")]
        public static void RunActive()
        {
            AiCanvasGraph graph = GraphToolkitCompatibility.GetGraph(EditorWindow.focusedWindow);
            if (graph == null && Selection.activeObject != null)
            {
                var path = AssetDatabase.GetAssetPath(Selection.activeObject);
                if (path.EndsWith("." + AiCanvasGraph.Extension, StringComparison.OrdinalIgnoreCase))
                    graph = GraphDatabase.LoadGraph<AiCanvasGraph>(path);
            }
            if (graph == null) { Debug.LogWarning("Open or select an AiCanvas graph first."); return; }
            Run(graph);
        }

        internal static async void Run(AiCanvasGraph graph)
        {
            if (!PromptFieldCompatibility.CommitPendingEdits(graph))
            {
                Debug.LogWarning("AiCanvas: 请先完成输入法选字，再执行 Run Graph。");
                return;
            }
            try { await ImageGraphRunner.RunAsync(graph); Debug.Log("AiCanvas: " + ImageGraphRunner.Status); }
            catch (OperationCanceledException) { Debug.Log("AiCanvas: cancelled."); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        [MenuItem("Tools/AiCanvas/Cancel Current Task")]
        public static void Cancel() { ImageGraphRunner.Cancel(); }
    }

#if UNITY_6000_6_OR_NEWER
    [GraphToolbarElement("AiCanvas/RunGraph", typeof(AiCanvasGraph), 200)]
#endif
    public sealed class RunImageToolbar : EditorToolbarButton, IAccessContainerWindow
    {
        public EditorWindow containerWindow { get; set; }
        public RunImageToolbar()
        {
            text = "Run Graph";
            tooltip = "按依赖顺序执行本地图像处理；经过 AiGenerate 时提交 AI 请求并使用所选账号额度。";
            clicked += () =>
            {
                if (GraphToolkitCompatibility.GetGraph(containerWindow) is AiCanvasGraph graph)
                    AiCanvasMenus.Run(graph);
            };
            AiCanvasUiBinding.BindRunner(this, () => SetEnabled(!ImageGraphRunner.IsRunning));
        }
    }

#if UNITY_6000_6_OR_NEWER
    [GraphToolbarElement("AiCanvas/Preview", typeof(AiCanvasGraph), 199)]
#endif
    public sealed class PreviewImageToolbar : EditorToolbarButton, IAccessContainerWindow
    {
        public EditorWindow containerWindow { get; set; }
        public PreviewImageToolbar()
        {
            text = "Preview";
            tooltip = "更新本地图像预览，使用已有 AI 结果和相机截图。";
            clicked += () =>
            {
                if (GraphToolkitCompatibility.GetGraph(containerWindow) is AiCanvasGraph graph)
                    ImageLocalPreview.Refresh(graph);
            };
            AiCanvasUiBinding.BindRunner(this, () => SetEnabled(!ImageGraphRunner.IsRunning));
        }
    }

#if UNITY_6000_6_OR_NEWER
    [GraphToolbarElement("AiCanvas/OpenAISettings", typeof(AiCanvasGraph), 203)]
#endif
    public sealed class OpenAISettingsToolbar : EditorToolbarButton
    {
        public OpenAISettingsToolbar()
        {
            text = "Connection";
            tooltip = "配置 ChatGPT 账号、插件运行组件，或 API Key、服务地址和超时。";
            clicked += OpenAIImageSettingsProvider.Open;
        }
    }

#if UNITY_6000_6_OR_NEWER
    [GraphToolbarElement("AiCanvas/Cancel", typeof(AiCanvasGraph), 201)]
#endif
    public sealed class CancelImageToolbar : EditorToolbarButton
    {
        public CancelImageToolbar()
        {
            text = "Cancel";
            clicked += ImageGraphRunner.Cancel;
            AiCanvasUiBinding.BindRunner(this, () => SetEnabled(ImageGraphRunner.IsRunning));
        }
    }

#if UNITY_6000_6_OR_NEWER
    [GraphToolbarElement("AiCanvas/Status", typeof(AiCanvasGraph), 202)]
#endif
    public sealed class ImageStatusToolbar : Label, IAccessContainerWindow
    {
        private EditorWindow window;
        public EditorWindow containerWindow { get => window; set { window = value; Refresh(); } }

        public ImageStatusToolbar()
        {
            name = "ai-canvas-status";
            style.marginLeft = 8;
            style.alignSelf = Align.Center;
            style.maxWidth = 640;
            style.overflow = Overflow.Hidden;
            style.textOverflow = TextOverflow.Ellipsis;
            AiCanvasUiBinding.BindStatus(this, Refresh);
        }

        private void Refresh()
        {
            string path = GraphToolkitCompatibility.GetGraph(window) is AiCanvasGraph graph
                ? GraphDatabase.GetGraphAssetPath(graph) : null;
            string status = OpenAIImageSettings.ConnectionLabel + " · " + ImageGraphRunner.StatusForGraph(path);
            text = status.Replace('\n', ' ').Replace('\r', ' ');
            tooltip = status;
        }
    }
}
