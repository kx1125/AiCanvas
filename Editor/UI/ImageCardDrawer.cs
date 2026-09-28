using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tsukuyomi.AiCanvas.Editor
{
    [CustomPropertyDrawer(typeof(ImageCard))]
    public sealed class ImageCardDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement { name = "ai-canvas-card" };
            root.style.minWidth = 230;
            root.style.maxWidth = 360;
            var textureProperty = property.FindPropertyRelative("texture");
            var captionProperty = property.FindPropertyRelative("caption");
            string nodeId = property.FindPropertyRelative("nodeId")?.stringValue;
            var outputNames = property.FindPropertyRelative("outputNames");
            var outputIndex = property.FindPropertyRelative("outputIndex");
            var choices = new List<string>();
            if (outputNames != null)
                for (int index = 0; index < outputNames.arraySize; index++) choices.Add(outputNames.GetArrayElementAtIndex(index).stringValue);
            int selectedOutput = choices.Count > 1 ? Mathf.Clamp(outputIndex.intValue, 0, choices.Count - 1) : 0;
            string PreviewKey() => ImagePreviewStore.OutputKey(nodeId, selectedOutput);
            var picker = new ObjectField { objectType = typeof(Texture2D), allowSceneObjects = false };
            picker.BindProperty(textureProperty);
            if (!string.IsNullOrEmpty(nodeId)) picker.style.display = DisplayStyle.None;
            var preview = new Image { name = "ai-canvas-preview", scaleMode = ScaleMode.ScaleToFit };
            preview.style.height = 160;
            preview.style.backgroundColor = new Color(0.075f, 0.085f, 0.10f);
            preview.pickingMode = PickingMode.Ignore;
            var caption = new Label { name = "ai-canvas-caption" };
            caption.style.whiteSpace = WhiteSpace.Normal;
            caption.style.fontSize = 11;
            caption.style.marginTop = 4;
            root.Add(picker);
            DropdownField channel = null;
            if (choices.Count > 1)
            {
                channel = new DropdownField("Channel", choices, selectedOutput) { name = "ai-canvas-output-channel" };
                root.Add(channel);
                // Keep selection on Toolkit's serialized binding path for persistence and Undo.
                var binding = new PropertyField(outputIndex, "Preview Channel Binding");
                binding.style.display = DisplayStyle.None;
                root.Add(binding);
                channel.RegisterValueChangedCallback(_ =>
                {
                    var field = binding.Q<IntegerField>();
                    if (field != null) field.value = channel.index;
                });
            }
            root.Add(preview);
            root.Add(caption);
            root.Add(new Button(() =>
            {
                var graph = AiCanvasWindowRegistry.FindGraph(root);
                string key = PreviewKey();
                var gpu = string.IsNullOrEmpty(nodeId) ? null : ImagePreviewStore.Find(graph, key, out _);
                if (gpu != null) { ImagePreviewWindow.ShowGpu(graph, key); return; }
                var texture = picker.value as Texture2D;
                if (texture != null) ImagePreviewWindow.ShowTexture(texture);
            }) { text = "Preview" });
            void UpdateImage(SerializedProperty value) { preview.image = value.objectReferenceValue as Texture2D; }
            UpdateImage(textureProperty);
            caption.text = captionProperty.stringValue;
            root.TrackPropertyValue(textureProperty, UpdateImage);
            root.TrackPropertyValue(captionProperty, value => caption.text = value.stringValue);
            if (!string.IsNullOrEmpty(nodeId))
            {
                void RefreshGpu()
                {
                    var graph = AiCanvasWindowRegistry.FindGraph(root);
                    var gpu = ImagePreviewStore.Find(graph, PreviewKey(), out string error);
                    if (gpu != null)
                    {
                        preview.image = gpu.Texture;
                        caption.text = $"{gpu.Width} x {gpu.Height} | {gpu.Kind}" + (channel == null ? "" : " | " + choices[selectedOutput]);
                    }
                    else
                    {
                        preview.image = error == null ? picker.value as Texture2D : null;
                        caption.text = error ?? "等待本地预览";
                    }
                    preview.MarkDirtyRepaint();
                }
                void OnPreviewChanged(string key)
                {
                    var graph = AiCanvasWindowRegistry.FindGraph(root);
                    if (graph != null && (key == null || key == ImagePreviewStore.Key(graph))) RefreshGpu();
                }
                if (channel != null)
                    root.TrackPropertyValue(outputIndex, value =>
                    {
                        string previousKey = PreviewKey();
                        selectedOutput = Mathf.Clamp(value.intValue, 0, choices.Count - 1);
                        channel.SetValueWithoutNotify(choices[selectedOutput]);
                        ImagePreviewWindow.FollowOutputSelection(AiCanvasWindowRegistry.FindGraph(root), previousKey, PreviewKey());
                        RefreshGpu();
                    });
                root.RegisterCallback<AttachToPanelEvent>(_ =>
                {
                    ImagePreviewStore.Changed -= OnPreviewChanged;
                    ImagePreviewStore.Changed += OnPreviewChanged;
                    root.schedule.Execute(RefreshGpu);
                });
                root.RegisterCallback<DetachFromPanelEvent>(_ => ImagePreviewStore.Changed -= OnPreviewChanged);
            }
            return root;
        }
    }

}
