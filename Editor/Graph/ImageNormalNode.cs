using System;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    public enum ImageNormalFormat { OpenGL, DirectX }

    [Serializable]
    public struct NormalSettings
    {
        [Range(0, 100), Tooltip("高度强度，按 UV 导数计算，不随贴图分辨率改变。")]
        public float strength;
        public ImageNormalFormat format;
        public ImageBorderMode border;
        [Tooltip("默认 Alpha 为 1；勾选后将输入高度的 R 通道写入 Alpha。")]
        public bool heightInAlpha;
        public static NormalSettings Default => new() { strength = 1, border = ImageBorderMode.Repeat };
    }

    [Serializable]
    [Node("Filters/Normal", "", "Normal")]
    public sealed class NormalNode : ComputeImageNode
    {
        public NormalSettings Settings => GraphOptionAccess.Read<NormalSettings>(this, "Normal");
        protected override void OnDefineImageInputs(IPortDefinitionContext context)
            => context.AddInputPort<Texture2D>("Input").WithDisplayName("Height")
                .WithTooltip("高度数据读取 R 通道；彩色图片请先连接 Grayscale。输出为切线空间 Data 法线。").Build();
        protected override void OnDefineComputeOptions(IOptionDefinitionContext context)
            => context.AddOption<NormalSettings>("Normal").WithDefaultValue(NormalSettings.Default).Build();
        public override ComputeImageOperation CreateOperation()
        {
            var s = Settings;
            ImageFilterShader.Range(s.strength, 0, 100, "Strength");
            ImageFilterShader.ValidEnum(s.format, "Normal Format"); ImageFilterShader.ValidEnum(s.border, "Border");
            return new ComputeImageOperation(ImageFilterShader.LoadMaterial(), "NormalImage",
                new[] { new ComputeImageInput("Input", "_Input") }, new[]
                {
                    ComputeImageParameter.Float("_Strength", s.strength), ComputeImageParameter.Int("_NormalFormat", (int)s.format),
                    ComputeImageParameter.Int("_HeightInAlpha", s.heightInAlpha ? 1 : 0), ComputeImageParameter.Int("_Border", (int)s.border)
                }, valueKind: ImageValueKind.Data);
        }
        public override void OnEnable() { DefaultColor = new Color(0.55f, 0.48f, 0.85f); }
    }
}
