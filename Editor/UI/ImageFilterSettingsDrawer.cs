using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Tsukuyomi.AiCanvas.Editor
{
    internal static class ImageFilterFields
    {
        internal static VisualElement Create(SerializedProperty property, params (string field, string label)[] fields)
        {
            var root = new VisualElement();
            root.style.minWidth = 280;
            root.style.maxWidth = 340;
            foreach (var field in fields) root.Add(new PropertyField(property.FindPropertyRelative(field.field), field.label));
            return root;
        }
    }

    [CustomPropertyDrawer(typeof(ResizeSettings))]
    public sealed class ResizeSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property) => ImageFilterFields.Create(property,
            ("width", "Width"), ("height", "Height"), ("fit", "Fit"), ("sampling", "Sampling"));
    }

    [CustomPropertyDrawer(typeof(LevelsSettings))]
    public sealed class LevelsSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property) => new LevelsEditor(property);
    }

    [CustomPropertyDrawer(typeof(BlendSettings))]
    public sealed class BlendSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property) => ImageFilterFields.Create(property,
            ("mode", "Mode"), ("opacity", "Opacity"));
    }
}
