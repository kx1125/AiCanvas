using System;
using System.Linq;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tsukuyomi.AiCanvas.Editor
{
    [CustomPropertyDrawer(typeof(GenerateControls))]
    public sealed class GenerateControlsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            string nodeId = property.FindPropertyRelative("nodeId").stringValue;
            var root = new VisualElement { name = "ai-canvas-generate-controls", userData = nodeId };
            root.style.minWidth = 230;
            root.style.maxWidth = 360;
            var message = new Label { name = "ai-canvas-node-run-status" };
            message.style.whiteSpace = WhiteSpace.Normal;
            message.style.fontSize = 11;
            message.style.display = DisplayStyle.None;
            var button = new Button { name = "ai-canvas-run-node", text = "Run", userData = nodeId,
                tooltip = "使用 GPT Image 2 生成当前节点（使用账号额度或 API 计费）；上游 AiGenerate 使用已有结果。" };
            root.Add(button);
            root.Add(message);
            button.clicked += async () =>
            {
                if (ImageGraphRunner.IsRunning) return;
                var graph = AiCanvasWindowRegistry.FindGraph(root);
                if (graph == null) return;
                var node = graph.GetNodes().OfType<GenerateImageNode>().FirstOrDefault(candidate => candidate.GetStableId().ToString() == nodeId);
                if (node == null) return;
                message.style.display = DisplayStyle.Flex;
                if (!PromptFieldCompatibility.CommitPendingEdits(graph))
                {
                    message.text = "请先完成输入法选字，再运行节点。";
                    return;
                }
                try
                {
                    message.text = "正在生成…";
                    var pending = ImageGraphRunner.RunNodeAsync(graph, node.GetStableId());
                    UpdateButton();
                    await pending;
                    message.text = ImageGraphRunner.Status;
                }
                catch (OperationCanceledException) { message.text = ImageGraphRunner.Status; }
                catch (Exception exception) { message.text = exception.Message + $"\n已保存 {ImageGraphRunner.LastSavedFiles.Count} 张图片。"; }
                finally { UpdateButton(); }
            };
            void UpdateButton()
            {
                button.SetEnabled(!ImageGraphRunner.IsRunning);
                button.text = ImageGraphRunner.IsRunning && ImageGraphRunner.ActiveNodeId == nodeId ? "Running…" : "Run";
            }
            AiCanvasUiBinding.BindRunner(root, UpdateButton);
            return root;
        }
    }
}
