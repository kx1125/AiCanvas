using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tsukuyomi.AiCanvas.Editor
{
    [CustomPropertyDrawer(typeof(GradientMapSettings))]
    public sealed class GradientMapSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = ImageFilterFields.Create(property);
            var gradient = new GradientField("Gradient") { hdr = false, colorSpace = ColorSpace.Gamma };
            gradient.BindProperty(property.FindPropertyRelative("gradient"));
            root.Add(gradient);
            root.Add(new PropertyField(property.FindPropertyRelative("address"), "Address"));
            root.Add(new PropertyField(property.FindPropertyRelative("multiplyInputAlpha"), "Multiply Input Alpha"));
            return root;
        }
    }

    [CustomPropertyDrawer(typeof(HistogramRangeSettings))]
    public sealed class HistogramRangeSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property) => ImageFilterFields.Create(property,
            ("range", "Range"), ("position", "Position"));
    }

    [CustomPropertyDrawer(typeof(HistogramSelectSettings))]
    public sealed class HistogramSelectSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property) => ImageFilterFields.Create(property,
            ("position", "Position"), ("range", "Range"), ("contrast", "Contrast"), ("invert", "Invert"));
    }

    [CustomPropertyDrawer(typeof(ChannelShuffleSettings))]
    public sealed class ChannelShuffleSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property) => ImageFilterFields.Create(property,
            ("red", "Red Source"), ("green", "Green Source"), ("blue", "Blue Source"), ("alpha", "Alpha Source"));
    }

    [CustomPropertyDrawer(typeof(GradientLinearSettings))]
    public sealed class GradientLinearSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property) => ImageFilterFields.Create(property,
            ("width", "Width"), ("height", "Height"), ("direction", "Direction"), ("cycles", "Cycles"),
            ("phase", "Phase"), ("mirror", "Mirror"), ("invert", "Invert"));
    }

    [CustomPropertyDrawer(typeof(ShapeSettings))]
    public sealed class ShapeSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property) => ImageFilterFields.Create(property,
            ("width", "Width"), ("height", "Height"), ("shape", "Shape"), ("size", "Size"), ("feather", "Feather"));
    }

    [CustomPropertyDrawer(typeof(TileableNoiseSettings))]
    public sealed class TileableNoiseSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property) => ImageFilterFields.Create(property,
            ("width", "Width"), ("height", "Height"), ("scale", "Scale"), ("octaves", "Octaves"),
            ("roughness", "Roughness"), ("seed", "Seed"), ("offset", "Offset (UV)"));
    }

    [CustomPropertyDrawer(typeof(TileGeneratorSettings))]
    public sealed class TileGeneratorSettingsDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property) => ImageFilterFields.Create(property,
            ("width", "Width"), ("height", "Height"), ("columns", "Columns"), ("rows", "Rows"),
            ("shape", "Shape"), ("size", "Size"), ("feather", "Feather"), ("rowOffset", "Row Offset"),
            ("rotation", "Rotation (°)"), ("seed", "Seed"), ("jitter", "Jitter"), ("sizeRandom", "Size Random"),
            ("rotationRandom", "Rotation Random (°)"), ("valueRandom", "Value Random"), ("missing", "Missing"), ("offset", "Offset (UV)"));
    }
}
