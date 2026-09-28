using System;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    public enum ImageUniformMode { Color, Grayscale }

    [Serializable]
    public struct UniformColorSettings
    {
        [Min(1)] public int width, height;
        public ImageUniformMode mode;
        [ColorUsage(true, false), Tooltip("标准 sRGB 颜色；在节点间保存为线性 Color。")]
        public Color color;
        [Range(0, 1), Tooltip("Grayscale 模式的原始数据值，Alpha 为 1。")]
        public float value;
        public static UniformColorSettings Default => new()
            { width = 1024, height = 1024, color = Color.gray, value = 0.5f };
    }

    [Serializable]
    [Node("Generators", "", "Uniform Color")]
    public sealed class UniformColorNode : ComputeImageNode
    {
        public UniformColorSettings Settings => GraphOptionAccess.Read<UniformColorSettings>(this, "Uniform");
        protected override void OnDefineImageInputs(IPortDefinitionContext context) { }
        protected override void OnDefineComputeOptions(IOptionDefinitionContext context)
            => context.AddOption<UniformColorSettings>("Uniform").WithDefaultValue(UniformColorSettings.Default).Build();
        public override ComputeImageOperation CreateOperation()
        {
            var s = Settings;
            ImageFilterShader.Size(s.width, s.height, "Uniform Color");
            ImageFilterShader.ValidEnum(s.mode, "Uniform Color Mode");
            Color value;
            if (s.mode == ImageUniformMode.Color)
            {
                ImageFilterShader.UnitRange(s.color.r, "Red"); ImageFilterShader.UnitRange(s.color.g, "Green");
                ImageFilterShader.UnitRange(s.color.b, "Blue"); ImageFilterShader.UnitRange(s.color.a, "Alpha");
                value = s.color.linear;
            }
            else
            {
                ImageFilterShader.UnitRange(s.value, "Value");
                value = new Color(s.value, s.value, s.value, 1);
            }
            return new ComputeImageOperation(ImageFilterShader.LoadMaterial(), "UniformImage", Array.Empty<ComputeImageInput>(),
                new[] { ComputeImageParameter.Vector("_Uniform", value) }, s.width, s.height,
                s.mode == ImageUniformMode.Color ? ImageValueKind.Color : ImageValueKind.Data);
        }
        public override void OnEnable() { DefaultColor = new Color(0.75f, 0.48f, 0.35f); }
    }
}
