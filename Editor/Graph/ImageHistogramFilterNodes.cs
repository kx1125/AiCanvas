using System;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    [Serializable]
    public struct HistogramRangeSettings
    {
        [Range(0, 1), Tooltip("输出区间宽度；0 输出常量 Position，1 保持完整范围。")]
        public float range;
        [Range(0, 1), Tooltip("输出区间中心，超出 0–1 的部分会截断。")]
        public float position;
        public static HistogramRangeSettings Default => new() { range = 1, position = 0.5f };
    }

    [Serializable]
    public struct HistogramSelectSettings
    {
        [Range(0, 1)] public float position;
        [Range(0, 1), Tooltip("选择区间的完整宽度；0 不选中任何值。")]
        public float range;
        [Range(0, 1), Tooltip("1 为硬边界；较小值使选择区间向内柔化。")]
        public float contrast;
        public bool invert;
        public static HistogramSelectSettings Default => new() { position = 0.5f, range = 0.5f, contrast = 0.5f };
    }

    [Serializable]
    [Node("Filters/Adjustment", "", "Histogram Range")]
    public sealed class HistogramRangeNode : ComputeImageNode
    {
        public HistogramRangeSettings Settings => GraphOptionAccess.Read<HistogramRangeSettings>(this, "Range");
        protected override void OnDefineImageInputs(IPortDefinitionContext context)
            => context.AddInputPort<Texture2D>("Input").WithDisplayName("Grayscale").WithTooltip("读取原始 R 通道，输出不透明 Data。").Build();
        protected override void OnDefineComputeOptions(IOptionDefinitionContext context)
            => context.AddOption<HistogramRangeSettings>("Range").WithDefaultValue(HistogramRangeSettings.Default).Build();
        public override ComputeImageOperation CreateOperation()
        {
            var s = Settings;
            ImageFilterShader.UnitRange(s.range, "Range"); ImageFilterShader.UnitRange(s.position, "Position");
            return new ComputeImageOperation(ImageFilterShader.LoadProcedural(), "HistogramRangeImage",
                new[] { new ComputeImageInput("Input", "_Input") },
                new[] { ComputeImageParameter.Vector("_Histogram", new Vector4(s.position, s.range, 0, 0)) }, valueKind: ImageValueKind.Data);
        }
        public override void OnEnable() { DefaultColor = new Color(0.78f, 0.65f, 0.32f); }
    }

    [Serializable]
    [Node("Filters/Adjustment", "", "Histogram Select")]
    public sealed class HistogramSelectNode : ComputeImageNode
    {
        public HistogramSelectSettings Settings => GraphOptionAccess.Read<HistogramSelectSettings>(this, "Select");
        protected override void OnDefineImageInputs(IPortDefinitionContext context)
            => context.AddInputPort<Texture2D>("Input").WithDisplayName("Grayscale").WithTooltip("读取原始 R 通道，输出不透明 Data 蒙版。").Build();
        protected override void OnDefineComputeOptions(IOptionDefinitionContext context)
            => context.AddOption<HistogramSelectSettings>("Select").WithDefaultValue(HistogramSelectSettings.Default).Build();
        public override ComputeImageOperation CreateOperation()
        {
            var s = Settings;
            ImageFilterShader.UnitRange(s.range, "Range"); ImageFilterShader.UnitRange(s.position, "Position");
            ImageFilterShader.UnitRange(s.contrast, "Contrast");
            return new ComputeImageOperation(ImageFilterShader.LoadProcedural(), "HistogramSelectImage",
                new[] { new ComputeImageInput("Input", "_Input") },
                new[] { ComputeImageParameter.Vector("_Histogram", new Vector4(s.position, s.range, s.contrast, s.invert ? 1 : 0)) }, valueKind: ImageValueKind.Data);
        }
        public override void OnEnable() { DefaultColor = new Color(0.78f, 0.65f, 0.32f); }
    }
}
