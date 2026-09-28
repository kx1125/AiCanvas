using System;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    public enum ImageGrayscaleChannel { Luminance, Red, Green, Blue, Alpha }

    [Serializable]
    public struct GrayscaleSettings
    {
        [Tooltip("Color 的 RGB 在 graph 工作空间中读取；Alpha 与 Data 保留原始值。")]
        public ImageGrayscaleChannel channel;
        [Tooltip("默认输出不透明数据图；勾选后保留输入 Alpha。")]
        public bool preserveAlpha;
    }

    [Serializable]
    public struct InvertSettings { public bool affectAlpha; }

    [Serializable]
    public struct HistogramScanSettings
    {
        [Range(0, 1), Tooltip("增大 Position 扩大白色区域；0 为全黑，1 为全白。")]
        public float position;
        [Range(0, 1), Tooltip("1 为硬阈值，较小值使过渡更柔和。")]
        public float contrast;
        public bool invert;
        public static HistogramScanSettings Default => new() { position = 0.5f, contrast = 0.5f };
    }

    [Serializable]
    [Node("Filters/Channels", "", "Grayscale")]
    public sealed class GrayscaleNode : ComputeImageNode
    {
        public GrayscaleSettings Settings => GraphOptionAccess.Read<GrayscaleSettings>(this, "Grayscale");
        protected override void OnDefineImageInputs(IPortDefinitionContext context)
            => context.AddInputPort<Texture2D>("Input").WithDisplayName("Image").Build();
        protected override void OnDefineComputeOptions(IOptionDefinitionContext context)
            => context.AddOption<GrayscaleSettings>("Grayscale").WithDefaultValue(default).Build();
        public override ComputeImageOperation CreateOperation() => CreateOperation(default);
        public override ComputeImageOperation CreateOperation(ImageProcessingContext context)
        {
            var s = Settings;
            ImageFilterShader.ValidEnum(s.channel, "Grayscale Channel");
            return new ComputeImageOperation(ImageFilterShader.LoadMaterial(), "GrayscaleImage",
                new[] { new ComputeImageInput("Input", "_Input") }, new[]
                {
                    ComputeImageParameter.Int("_Channel", (int)s.channel),
                    ComputeImageParameter.Int("_PreserveAlpha", s.preserveAlpha ? 1 : 0),
                    ImageFilterShader.WorkingSpace(context)
                }, valueKind: ImageValueKind.Data);
        }
        public override void OnEnable() { DefaultColor = new Color(0.6f, 0.65f, 0.7f); }
    }

    [Serializable]
    [Node("Filters/Adjustment", "", "Invert")]
    public sealed class InvertNode : ComputeImageNode
    {
        public InvertSettings Settings => GraphOptionAccess.Read<InvertSettings>(this, "Invert");
        protected override void OnDefineImageInputs(IPortDefinitionContext context)
            => context.AddInputPort<Texture2D>("Input").WithDisplayName("Image").Build();
        protected override void OnDefineComputeOptions(IOptionDefinitionContext context)
            => context.AddOption<InvertSettings>("Invert").WithDefaultValue(default).Build();
        public override ComputeImageOperation CreateOperation() => CreateOperation(default);
        public override ComputeImageOperation CreateOperation(ImageProcessingContext context)
            => new(ImageFilterShader.LoadMaterial(), "InvertImage", new[] { new ComputeImageInput("Input", "_Input") },
                new[] { ComputeImageParameter.Int("_AffectAlpha", Settings.affectAlpha ? 1 : 0), ImageFilterShader.WorkingSpace(context) });
        public override void OnEnable() { DefaultColor = new Color(0.78f, 0.65f, 0.32f); }
    }

    [Serializable]
    [Node("Filters/Adjustment", "", "Histogram Scan")]
    public sealed class HistogramScanNode : ComputeImageNode
    {
        public HistogramScanSettings Settings => GraphOptionAccess.Read<HistogramScanSettings>(this, "Scan");
        protected override void OnDefineImageInputs(IPortDefinitionContext context)
            => context.AddInputPort<Texture2D>("Input").WithDisplayName("Grayscale")
                .WithTooltip("读取 R 通道；彩色图片请先用 Grayscale 转换。输出为不透明 Data 蒙版。").Build();
        protected override void OnDefineComputeOptions(IOptionDefinitionContext context)
            => context.AddOption<HistogramScanSettings>("Scan").WithDefaultValue(HistogramScanSettings.Default).Build();
        public override ComputeImageOperation CreateOperation()
        {
            var s = Settings;
            ImageFilterShader.UnitRange(s.position, "Position");
            ImageFilterShader.UnitRange(s.contrast, "Contrast");
            return new ComputeImageOperation(ImageFilterShader.LoadMaterial(), "HistogramScanImage",
                new[] { new ComputeImageInput("Input", "_Input") }, new[]
                {
                    ComputeImageParameter.Float("_Position", s.position), ComputeImageParameter.Float("_Contrast", s.contrast),
                    ComputeImageParameter.Int("_Invert", s.invert ? 1 : 0)
                }, valueKind: ImageValueKind.Data);
        }
        public override void OnEnable() { DefaultColor = new Color(0.78f, 0.65f, 0.32f); }
    }
}
