using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Tsukuyomi.AiCanvas.Editor
{
    [CustomPropertyDrawer(typeof(UniformColorSettings))]
    public sealed class UniformColorSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = ImageFilterFields.Create(property, ("width", "Width"), ("height", "Height"), ("mode", "Mode"));
            var color = new PropertyField(property.FindPropertyRelative("color"), "Color");
            var value = new PropertyField(property.FindPropertyRelative("value"), "Value");
            root.Add(color); root.Add(value);
            var mode = property.FindPropertyRelative("mode");
            void UpdateMode(SerializedProperty p)
            {
                bool grayscale = p.intValue == (int)ImageUniformMode.Grayscale;
                color.style.display = grayscale ? DisplayStyle.None : DisplayStyle.Flex;
                value.style.display = grayscale ? DisplayStyle.Flex : DisplayStyle.None;
            }
            UpdateMode(mode); root.TrackPropertyValue(mode, UpdateMode);
            return root;
        }
    }

    [CustomPropertyDrawer(typeof(GrayscaleSettings))]
    public sealed class GrayscaleSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property) => ImageFilterFields.Create(property,
            ("channel", "Channel"), ("preserveAlpha", "Preserve Alpha"));
    }

    [CustomPropertyDrawer(typeof(InvertSettings))]
    public sealed class InvertSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property) => ImageFilterFields.Create(property,
            ("affectAlpha", "Affect Alpha"));
    }

    [CustomPropertyDrawer(typeof(HistogramScanSettings))]
    public sealed class HistogramScanSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property) => ImageFilterFields.Create(property,
            ("position", "Position"), ("contrast", "Contrast"), ("invert", "Invert"));
    }

    [CustomPropertyDrawer(typeof(Transform2DSettings))]
    public sealed class Transform2DSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property) => ImageFilterFields.Create(property,
            ("offset", "Offset (UV)"), ("scale", "Scale"), ("rotation", "Rotation (°)"), ("border", "Border"), ("sampling", "Sampling"));
    }

    [CustomPropertyDrawer(typeof(GaussianBlurSettings))]
    public sealed class GaussianBlurSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property) => ImageFilterFields.Create(property,
            ("sigma", "Sigma (px)"), ("border", "Border"));
    }

    [CustomPropertyDrawer(typeof(DirectionalWarpSettings))]
    public sealed class DirectionalWarpSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property) => ImageFilterFields.Create(property,
            ("intensity", "Intensity (px)"), ("angle", "Angle (°)"), ("mapOffset", "Map Offset"),
            ("border", "Border"), ("sampling", "Sampling"));
    }

    [CustomPropertyDrawer(typeof(NormalSettings))]
    public sealed class NormalSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property) => ImageFilterFields.Create(property,
            ("strength", "Strength"), ("format", "Format"), ("border", "Border"), ("heightInAlpha", "Height in Alpha"));
    }
}
