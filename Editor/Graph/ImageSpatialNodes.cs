using System;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    public enum ImageBorderMode { Clamp, Repeat, Transparent }

    [Serializable]
    public struct Transform2DSettings
    {
        [Tooltip("图像平移量，以画布比例为单位；X 向右，Y 向上。")]
        public Vector2 offset;
        [Tooltip("1 保持原大小，负数镜像，绝对值不得小于 0.001。")]
        public Vector2 scale;
        [Range(-360, 360), Tooltip("绕中心逆时针旋转的角度，保持像素宽高比。")]
        public float rotation;
        public ImageBorderMode border;
        public ImageResizeSampling sampling;
        public static Transform2DSettings Default => new()
            { scale = Vector2.one, border = ImageBorderMode.Repeat, sampling = ImageResizeSampling.Bilinear };
    }

    [Serializable]
    public struct GaussianBlurSettings
    {
        [Range(0, 16), Tooltip("高斯标准差，单位为当前图片像素；0 保持原图，核半径为 ceil(3 × Sigma)。")]
        public float sigma;
        public ImageBorderMode border;
        public static GaussianBlurSettings Default => new() { sigma = 2, border = ImageBorderMode.Repeat };
    }

    [Serializable]
    public struct DirectionalWarpSettings
    {
        [Range(-256, 256), Tooltip("位移像素数，与 Intensity 的 R 通道减去 Map Offset 后的值相乘。")]
        public float intensity;
        [Range(-360, 360), Tooltip("正位移方向：0 向右，90 向上。")]
        public float angle;
        [Range(0, 1), Tooltip("0：黑色无位移；0.5：中灰无位移，可双向变形。")]
        public float mapOffset;
        public ImageBorderMode border;
        public ImageResizeSampling sampling;
        public static DirectionalWarpSettings Default => new()
            { intensity = 10, border = ImageBorderMode.Repeat, sampling = ImageResizeSampling.Bilinear };
    }

    [Serializable]
    [Node("Filters/Transform", "", "Transform 2D")]
    public sealed class Transform2DNode : ComputeImageNode
    {
        public Transform2DSettings Settings => GraphOptionAccess.Read<Transform2DSettings>(this, "Transform");
        protected override void OnDefineImageInputs(IPortDefinitionContext context)
            => context.AddInputPort<Texture2D>("Input").WithDisplayName("Image").Build();
        protected override void OnDefineComputeOptions(IOptionDefinitionContext context)
            => context.AddOption<Transform2DSettings>("Transform").WithDefaultValue(Transform2DSettings.Default).Build();
        public override ComputeImageOperation CreateOperation() => CreateOperation(default);
        public override ComputeImageOperation CreateOperation(ImageProcessingContext context)
        {
            var s = Settings;
            ImageFilterShader.Range(s.offset.x, -100, 100, "Offset X"); ImageFilterShader.Range(s.offset.y, -100, 100, "Offset Y");
            ImageFilterShader.Range(Mathf.Abs(s.scale.x), 0.001f, 100, "|Scale X|");
            ImageFilterShader.Range(Mathf.Abs(s.scale.y), 0.001f, 100, "|Scale Y|");
            ImageFilterShader.Range(s.rotation, -360, 360, "Rotation");
            ImageFilterShader.ValidEnum(s.border, "Border"); ImageFilterShader.ValidEnum(s.sampling, "Sampling");
            float angle = s.rotation * Mathf.Deg2Rad;
            return new ComputeImageOperation(ImageFilterShader.LoadMaterial(), "TransformImage",
                new[] { new ComputeImageInput("Input", "_Input") }, new[]
                {
                    ComputeImageParameter.Vector("_Transform", new Vector4(s.offset.x, s.offset.y, 1 / s.scale.x, 1 / s.scale.y)),
                    ComputeImageParameter.Vector("_Rotation", new Vector4(Mathf.Cos(angle), Mathf.Sin(angle), 0, 0)),
                    ComputeImageParameter.Int("_Border", (int)s.border), ComputeImageParameter.Int("_Sampling", (int)s.sampling),
                    ImageFilterShader.WorkingSpace(context)
                });
        }
        public override void OnEnable() { DefaultColor = new Color(0.35f, 0.72f, 0.72f); }
    }

    [Serializable]
    [Node("Filters/Blur", "", "Gaussian Blur")]
    public sealed class GaussianBlurNode : ComputeImageNode
    {
        public GaussianBlurSettings Settings => GraphOptionAccess.Read<GaussianBlurSettings>(this, "Blur");
        protected override void OnDefineImageInputs(IPortDefinitionContext context)
            => context.AddInputPort<Texture2D>("Input").WithDisplayName("Image").Build();
        protected override void OnDefineComputeOptions(IOptionDefinitionContext context)
            => context.AddOption<GaussianBlurSettings>("Blur").WithDefaultValue(GaussianBlurSettings.Default).Build();
        public override ComputeImageOperation CreateOperation() => CreateOperation(default);
        public override ComputeImageOperation CreateOperation(ImageProcessingContext context)
        {
            var s = Settings;
            ImageFilterShader.Range(s.sigma, 0, 16, "Sigma"); ImageFilterShader.ValidEnum(s.border, "Border");
            // Small radii cost less with direct sampling than with a shared tile.
            string suffix = s.sigma > 1 ? "Tiled" : "";
            return new ComputeImageOperation(ImageFilterShader.LoadMaterial(), "BlurHorizontal" + suffix,
                new[] { new ComputeImageInput("Input", "_Input") }, new[]
                {
                    ComputeImageParameter.Float("_Sigma", s.sigma), ComputeImageParameter.Int("_Border", (int)s.border),
                    ImageFilterShader.WorkingSpace(context)
                }, followUpKernel: s.sigma == 0 ? null : "BlurVertical" + suffix);
        }
        public override void OnEnable() { DefaultColor = new Color(0.45f, 0.6f, 0.8f); }
    }

    [Serializable]
    [Node("Filters/Transform", "", "Directional Warp")]
    public sealed class DirectionalWarpNode : ComputeImageNode
    {
        public DirectionalWarpSettings Settings => GraphOptionAccess.Read<DirectionalWarpSettings>(this, "Warp");
        protected override void OnDefineImageInputs(IPortDefinitionContext context)
        {
            context.AddInputPort<Texture2D>("Input").WithDisplayName("Image").Build();
            context.AddInputPort<Texture2D>("Intensity").WithTooltip("必需的位移强度图，读取原始 R 通道；尺寸不同时双线性铺满输入画布。").Build();
        }
        protected override void OnDefineComputeOptions(IOptionDefinitionContext context)
            => context.AddOption<DirectionalWarpSettings>("Warp").WithDefaultValue(DirectionalWarpSettings.Default).Build();
        public override ComputeImageOperation CreateOperation() => CreateOperation(default);
        public override ComputeImageOperation CreateOperation(ImageProcessingContext context)
        {
            var s = Settings;
            ImageFilterShader.Range(s.intensity, -256, 256, "Intensity"); ImageFilterShader.Range(s.angle, -360, 360, "Angle");
            ImageFilterShader.UnitRange(s.mapOffset, "Map Offset");
            ImageFilterShader.ValidEnum(s.border, "Border"); ImageFilterShader.ValidEnum(s.sampling, "Sampling");
            float angle = s.angle * Mathf.Deg2Rad;
            return new ComputeImageOperation(ImageFilterShader.LoadMaterial(), "DirectionalWarpImage",
                new[] { new ComputeImageInput("Input", "_Input"), new ComputeImageInput("Intensity", "_IntensityMap") }, new[]
                {
                    ComputeImageParameter.Vector("_Warp", new Vector4(Mathf.Cos(angle) * s.intensity, Mathf.Sin(angle) * s.intensity, s.mapOffset, 0)),
                    ComputeImageParameter.Int("_Border", (int)s.border), ComputeImageParameter.Int("_Sampling", (int)s.sampling),
                    ImageFilterShader.WorkingSpace(context)
                });
        }
        public override void OnEnable() { DefaultColor = new Color(0.35f, 0.72f, 0.72f); }
    }
}
