using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tsukuyomi.AiCanvas.Editor
{
    [CustomPropertyDrawer(typeof(GenerationSettings))]
    public sealed class GenerationSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement { name = "ai-canvas-generation-settings" };
            root.style.minWidth = 260;
            GenerateNodeLayout.Attach(root);
            var size = property.FindPropertyRelative("size");
            root.Add(new PropertyField(size, "Image Size"));
            var custom = new VisualElement();
            custom.Add(new PropertyField(property.FindPropertyRelative("width"), "Width"));
            custom.Add(new PropertyField(property.FindPropertyRelative("height"), "Height"));
            root.Add(custom);
            var hint = new Label { name = "ai-canvas-size-hint" };
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.maxWidth = 320;
            root.Add(hint);
            root.Add(new PropertyField(property.FindPropertyRelative("quality"), "Quality"));
            root.Add(new OutputCountField(property.FindPropertyRelative("count")));
            void Refresh()
            {
                if (size.serializedObject.targetObject == null) return;
                var preset = (ImageSizePreset)size.intValue;
                custom.style.display = preset == ImageSizePreset.Custom ? DisplayStyle.Flex : DisplayStyle.None;
                hint.text = preset == ImageSizePreset.Auto ? "由图片服务根据提示词和输入图片决定输出尺寸。" :
                    preset == ImageSizePreset.Custom ? "宽高须为 16 的倍数；超过 2560 × 1440 的尺寸为实验性支持。" :
                    preset == ImageSizePreset.Landscape4K || preset == ImageSizePreset.Portrait4K || preset == ImageSizePreset.Square2K
                        ? "该尺寸在 GPT Image 2 中为实验性支持。" : "";
            }
            root.TrackPropertyValue(size, _ => Refresh());
            Refresh();
            return root;
        }
    }

    // Changing count rebuilds NodeOptionsInspector, destroying a bound slider
    // while it owns pointer capture. Edit a local draft for the whole gesture,
    // then use the native bound field to submit one Graph Toolkit Undo command.
    internal sealed class OutputCountField : VisualElement
    {
        private readonly SliderInt slider;
        private readonly PropertyField binding;
        private int activePointer = -1;
        private bool dirty;

        internal OutputCountField(SerializedProperty property)
        {
            name = "ai-canvas-output-count";
            slider = new SliderInt("Output Count", 1, 10)
            {
                name = "ai-canvas-output-count-slider",
                showInputField = true,
                tooltip = "拖动时预览数量，松开后统一更新输出端口。减少数量会移除末尾端口及其连线，可通过 Undo 恢复。"
            };
            slider.SetValueWithoutNotify(Mathf.Clamp(property.intValue, 1, 10));
            Add(slider);
            // Keep the adapter under the same serialized PropertyDrawer so the
            // Toolkit's normal binding, persistence and undo path remains active.
            binding = new PropertyField(property, "Output Count Binding")
            {
                name = "ai-canvas-output-count-binding"
            };
            binding.style.display = DisplayStyle.None;
            Add(binding);
            var number = slider.Q<TextField>("unity-text-field");
            if (number != null) number.isDelayed = true;

            slider.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0) activePointer = evt.pointerId;
            }, TrickleDown.TrickleDown);
            slider.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.pointerId == activePointer) FinishGesture();
            }, TrickleDown.TrickleDown);
            slider.RegisterCallback<PointerCaptureOutEvent>(evt =>
            {
                if (evt.pointerId != activePointer) return;
                if (evt.relatedTarget is VisualElement next && slider.Contains(next)) return;
                FinishGesture();
            });
            slider.RegisterCallback<PointerCancelEvent>(evt =>
            {
                if (evt.pointerId == activePointer) FinishGesture();
            }, TrickleDown.TrickleDown);
            slider.RegisterValueChangedCallback(evt =>
            {
                dirty = true;
                if (activePointer < 0) Commit();
            });
            this.TrackPropertyValue(property, value =>
            {
                if (activePointer < 0 && !dirty)
                    slider.SetValueWithoutNotify(Mathf.Clamp(value.intValue, 1, 10));
            });
        }

        private void FinishGesture()
        {
            activePointer = -1;
            // Finish pointer dispatch and release native capture before a model
            // update is allowed to detach this field and rebuild the option list.
            schedule.Execute(Commit);
        }

        private void Commit()
        {
            if (!dirty || panel == null) return;
            var source = binding.Q<SliderInt>();
            if (source == null) return;
            dirty = false;
            source.value = Mathf.Clamp(slider.value, 1, 10);
        }
    }
}
