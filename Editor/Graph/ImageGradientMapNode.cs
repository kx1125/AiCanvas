using System;
using System.Collections.Generic;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    public enum ImageGradientAddress { Clamp, Repeat }

    [Serializable]
    public struct GradientMapSettings
    {
        [Tooltip("编辑颜色与 Alpha 色标；支持 Blend、Fixed、Perceptual Blend。")]
        public Gradient gradient;
        public ImageGradientAddress address;
        public bool multiplyInputAlpha;
        public static GradientMapSettings Default => new()
        {
            gradient = new Gradient
            {
                colorSpace = ColorSpace.Gamma,
                colorKeys = new[] { new GradientColorKey(Color.black, 0), new GradientColorKey(Color.white, 1) },
                alphaKeys = new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) }
            },
            multiplyInputAlpha = true
        };
    }

    [Serializable]
    [Node("Filters/Adjustment", "", "Gradient Map")]
    public sealed class GradientMapNode : ComputeImageNode
    {
        public GradientMapSettings Settings => GraphOptionAccess.Read<GradientMapSettings>(this, "GradientMap");
        protected override void OnDefineImageInputs(IPortDefinitionContext context)
            => context.AddInputPort<Texture2D>("Input").WithDisplayName("Grayscale")
                .WithTooltip("以原始 R 通道采样渐变；彩色图片请先连接 Grayscale。").Build();
        protected override void OnDefineComputeOptions(IOptionDefinitionContext context)
            => context.AddOption<GradientMapSettings>("GradientMap").WithDefaultValue(GradientMapSettings.Default).Build();
        public override ComputeImageOperation CreateOperation() => CreateOperation(default);
        public override ComputeImageOperation CreateOperation(ImageProcessingContext context)
        {
            var s = Settings;
            if (s.gradient == null) throw new InvalidOperationException("Gradient Map 缺少渐变。");
            ImageFilterShader.ValidEnum(s.address, "Gradient Address");
            ImageFilterShader.ValidEnum(s.gradient.mode, "Gradient Mode");
            ImageFilterShader.ValidEnum(s.gradient.colorSpace, "Gradient Color Space");
            var colors = s.gradient.colorKeys;
            var alphas = s.gradient.alphaKeys;
            if (colors.Length < 1 || colors.Length > 8 || alphas.Length < 1 || alphas.Length > 8)
                throw new InvalidOperationException("Gradient Map 支持 1–8 个颜色和 Alpha 色标。");
            var parameters = new List<ComputeImageParameter>
            {
                ComputeImageParameter.Int("_GradientMode", (int)s.gradient.mode),
                ComputeImageParameter.Int("_GradientAddress", (int)s.address),
                ComputeImageParameter.Int("_ColorCount", colors.Length), ComputeImageParameter.Int("_AlphaCount", alphas.Length),
                ComputeImageParameter.Int("_MultiplyInputAlpha", s.multiplyInputAlpha ? 1 : 0),
                ImageFilterShader.WorkingSpace(context)
            };
            // Snapshot individual keys into ordinary immutable vector parameters. No live Gradient,
            // GPU LUT allocation or mutable array is retained by an in-flight operation.
            for (int i = 0; i < 8; i++)
            {
                Vector4 color = default, alpha = default;
                if (i < colors.Length)
                {
                    var key = colors[i];
                    ImageFilterShader.UnitRange(key.time, "Color Key Position");
                    ImageFilterShader.UnitRange(key.color.r, "Gradient Red");
                    ImageFilterShader.UnitRange(key.color.g, "Gradient Green");
                    ImageFilterShader.UnitRange(key.color.b, "Gradient Blue");
                    var linear = s.gradient.colorSpace == ColorSpace.Linear ? key.color : key.color.linear;
                    var working = context.WorkingSpace == ImageWorkingSpace.SRgb ? linear.gamma : linear;
                    if (s.gradient.mode == GradientMode.PerceptualBlend) working = linear;
                    color = new Vector4(working.r, working.g, working.b, key.time);
                }
                if (i < alphas.Length)
                {
                    ImageFilterShader.UnitRange(alphas[i].time, "Alpha Key Position");
                    ImageFilterShader.UnitRange(alphas[i].alpha, "Gradient Alpha");
                    alpha = new Vector4(alphas[i].alpha, alphas[i].time, 0, 0);
                }
                parameters.Add(ComputeImageParameter.Vector("_GradientColor" + i, color));
                parameters.Add(ComputeImageParameter.Vector("_GradientAlpha" + i, alpha));
            }
            return new ComputeImageOperation(ImageFilterShader.LoadProcedural(), "GradientMapImage",
                new[] { new ComputeImageInput("Input", "_Input") }, parameters, valueKind: ImageValueKind.Color);
        }
        public override void OnEnable() { DefaultColor = new Color(0.78f, 0.65f, 0.32f); }
    }
}
