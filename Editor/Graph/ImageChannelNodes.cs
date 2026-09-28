using System;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    public enum ImageChannelSource { ARed, AGreen, ABlue, AAlpha, BRed, BGreen, BBlue, BAlpha, Zero, One }

    [Serializable]
    public struct ChannelShuffleSettings
    {
        public ImageChannelSource red, green, blue, alpha;
        public static ChannelShuffleSettings Default => new()
            { red = ImageChannelSource.ARed, green = ImageChannelSource.AGreen, blue = ImageChannelSource.ABlue, alpha = ImageChannelSource.AAlpha };
    }

    [Serializable]
    [Node("Filters/Channels", "", "RGBA Split")]
    public sealed class RgbaSplitNode : ComputeImageNode
    {
        public override int OutputCount => 4;
        public override string OutputName(int index)
        {
            if (index < 0 || index >= OutputCount) throw new ArgumentOutOfRangeException(nameof(index));
            return "RGBA"[index].ToString();
        }
        protected override void OnDefineImageInputs(IPortDefinitionContext context)
            => context.AddInputPort<Texture2D>("Input").WithTooltip("将原始 RGBA 数值拆为四张灰度数据图；各输出 Alpha 为 1。").Build();
        public override ComputeImageOperation CreateOperation() => CreateOperation(default, 0);
        public override ComputeImageOperation CreateOperation(ImageProcessingContext context, int outputIndex)
        {
            if (outputIndex < 0 || outputIndex >= OutputCount) throw new ArgumentOutOfRangeException(nameof(outputIndex));
            return ComputeImageOperation.CreateChannelSplit(ImageFilterShader.LoadProcedural(), outputIndex);
        }
        public override void OnEnable() { DefaultColor = new Color(0.6f, 0.65f, 0.7f); }
    }

    [Serializable]
    [Node("Filters/Channels", "", "RGBA Merge")]
    public sealed class RgbaMergeNode : ComputeImageNode
    {
        protected override void OnDefineImageInputs(IPortDefinitionContext context)
        {
            context.AddInputPort<Texture2D>("R").WithTooltip("必需；R 通道写入输出 R，并决定输出尺寸。").Build();
            context.AddInputPort<Texture2D>("G").WithTooltip("必需；R 通道写入输出 G。").Build();
            context.AddInputPort<Texture2D>("B").WithTooltip("必需；R 通道写入输出 B。").Build();
            context.AddInputPort<Texture2D>("A").WithTooltip("可选；R 通道写入输出 A，未连接时为 1。").Build();
        }
        public override ComputeImageOperation CreateOperation() => new(ImageFilterShader.LoadProcedural(), "RgbaMergeImage", new[]
        {
            new ComputeImageInput("R", "_Red"), new ComputeImageInput("G", "_Green"),
            new ComputeImageInput("B", "_Blue"), new ComputeImageInput("A", "_Alpha", optional: true)
        }, valueKind: ImageValueKind.Data);
        public override void OnEnable() { DefaultColor = new Color(0.6f, 0.65f, 0.7f); }
    }

    [Serializable]
    [Node("Filters/Channels", "", "Channel Shuffle")]
    public sealed class ChannelShuffleNode : ComputeImageNode
    {
        public ChannelShuffleSettings Settings => GraphOptionAccess.Read<ChannelShuffleSettings>(this, "Shuffle");
        protected override void OnDefineImageInputs(IPortDefinitionContext context)
        {
            context.AddInputPort<Texture2D>("A").WithTooltip("必需，决定输出尺寸；各通道按原始值读取。").Build();
            context.AddInputPort<Texture2D>("B").WithTooltip("可选第二张数据图；未连接时 RGBA 均为 1。").Build();
        }
        protected override void OnDefineComputeOptions(IOptionDefinitionContext context)
            => context.AddOption<ChannelShuffleSettings>("Shuffle").WithDefaultValue(ChannelShuffleSettings.Default).Build();
        public override ComputeImageOperation CreateOperation()
        {
            var s = Settings;
            ImageFilterShader.ValidEnum(s.red, "Red Source"); ImageFilterShader.ValidEnum(s.green, "Green Source");
            ImageFilterShader.ValidEnum(s.blue, "Blue Source"); ImageFilterShader.ValidEnum(s.alpha, "Alpha Source");
            return new ComputeImageOperation(ImageFilterShader.LoadProcedural(), "ChannelShuffleImage", new[]
            {
                new ComputeImageInput("A", "_Input"), new ComputeImageInput("B", "_Second", optional: true)
            }, new[] { ComputeImageParameter.Vector("_Channels", new Vector4((int)s.red, (int)s.green, (int)s.blue, (int)s.alpha)) },
                valueKind: ImageValueKind.Data);
        }
        public override void OnEnable() { DefaultColor = new Color(0.6f, 0.65f, 0.7f); }
    }
}
