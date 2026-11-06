using System;
using System.Linq;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tsukuyomi.AiCanvas.Editor
{
    [CustomPropertyDrawer(typeof(CameraColorSettings))]
    public sealed class CameraColorSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement { name = "ai-canvas-camera-settings" };
            root.style.minWidth = 280;
            root.style.maxWidth = 360;
            var main = property.FindPropertyRelative("useMainCamera");
            var cameraId = property.FindPropertyRelative("cameraId");
            var cameraSize = property.FindPropertyRelative("useCameraSize");
            root.Add(new PropertyField(main, "Main Camera"));
            var picker = new ObjectField("Camera") { objectType = typeof(Camera), allowSceneObjects = true,
                name = "ai-canvas-camera-picker", tooltip = "指定场景中的 Base Camera；包含其 Overlay 相机。" };
            root.Add(picker);
            var binding = new PropertyField(cameraId, "Camera ID");
            binding.style.display = DisplayStyle.None;
            root.Add(binding);
            var message = new Label { name = "ai-canvas-camera-hint" };
            message.style.whiteSpace = WhiteSpace.Normal;
            message.style.fontSize = 11;
            root.Add(message);
            root.Add(new PropertyField(cameraSize, "Camera Size"));
            var dimensions = new VisualElement();
            dimensions.Add(new PropertyField(property.FindPropertyRelative("width"), "Width"));
            dimensions.Add(new PropertyField(property.FindPropertyRelative("height"), "Height"));
            root.Add(dimensions);
            picker.RegisterValueChangedCallback(evt =>
            {
                try
                {
                    // Use the native serialized binding to retain Toolkit Undo.
                    string id = CameraColorCapture.Reference(evt.newValue as Camera);
                    var field = binding.Q<TextField>();
                    if (field != null) field.value = id;
                }
                catch (Exception exception)
                {
                    picker.SetValueWithoutNotify(evt.previousValue);
                    message.text = exception.Message;
                }
            });
            void Refresh()
            {
                if (property.serializedObject.targetObject == null) return;
                picker.SetEnabled(!main.boolValue);
                picker.SetValueWithoutNotify(main.boolValue ? Camera.main : CameraColorCapture.ResolveReference(cameraId.stringValue));
                dimensions.style.display = cameraSize.boolValue ? DisplayStyle.None : DisplayStyle.Flex;
                message.text = main.boolValue ? "使用当前启用且标记为 MainCamera 的相机。" :
                    cameraId.stringValue.StartsWith("session:", StringComparison.Ordinal)
                        ? "未保存场景的相机仅本次会话有效；保存场景后请重新指定。"
                        : picker.value == null ? "请指定相机，或加载该相机所在的场景。" : "场景相机引用随图保存。";
            }
            root.TrackPropertyValue(main, _ => Refresh());
            root.TrackPropertyValue(cameraId, _ => Refresh());
            root.TrackPropertyValue(cameraSize, _ => Refresh());
            Refresh();
            return root;
        }
    }

    [CustomPropertyDrawer(typeof(CameraColorControls))]
    public sealed class CameraColorControlsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            string nodeId = property.FindPropertyRelative("nodeId").stringValue;
            var root = new VisualElement { name = "ai-canvas-camera-controls", userData = nodeId };
            root.style.minWidth = 280;
            root.style.maxWidth = 360;
            var message = new Label { name = "ai-canvas-camera-status" };
            message.style.whiteSpace = WhiteSpace.Normal;
            message.style.fontSize = 11;
            var button = new Button { name = "ai-canvas-camera-capture", text = "Capture",
                tooltip = "渲染相机最新画面并保存当前帧；下游 Generate 需手动 Run。" };
            root.Add(button);
            root.Add(message);
            button.clicked += () =>
            {
                var graph = AiCanvasWindowRegistry.FindGraph(root);
                if (graph == null) return;
                var node = graph.GetNodes().OfType<CameraColorNode>().FirstOrDefault(candidate => candidate.GetStableId().ToString() == nodeId);
                if (node == null) return;
                try { CameraColorCapture.Capture(graph, node); }
                catch (Exception exception) { message.text = exception.Message; }
            };
            AiCanvasUiBinding.BindRunner(root, () => button.SetEnabled(!ImageGraphRunner.IsRunning));
            return root;
        }
    }

    [CustomPropertyDrawer(typeof(CameraColorFrame))]
    public sealed class CameraColorFrameDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement { name = "ai-canvas-camera-frame" };
            root.style.minWidth = 280;
            root.style.maxWidth = 360;
            var texture = property.FindPropertyRelative("texture");
            var caption = property.FindPropertyRelative("caption");
            var preview = new Image { name = "ai-canvas-camera-preview", scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore };
            preview.style.height = 180;
            var showPreview = ImageAssetPreview.BindThumbnail(preview);
            preview.style.backgroundColor = new Color(0.075f, 0.085f, 0.10f);
            var label = new Label { name = "ai-canvas-camera-caption" };
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.fontSize = 11;
            root.Add(preview);
            root.Add(label);
            root.Add(new Button(() =>
            {
                if (texture.objectReferenceValue is Texture2D image) ImagePreviewWindow.ShowTexture(image);
            }) { text = "Preview" });
            void Refresh()
            {
                if (property.serializedObject.targetObject == null) return;
                showPreview(texture.objectReferenceValue as Texture2D);
                label.text = caption.stringValue;
            }
            root.TrackPropertyValue(texture, _ => Refresh());
            root.TrackPropertyValue(caption, _ => Refresh());
            Refresh();
            return root;
        }
    }
}
