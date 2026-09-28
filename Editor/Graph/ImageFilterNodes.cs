using System;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    public enum ImageResizeFit { Stretch, Contain, Cover }
    public enum ImageResizeSampling { Nearest, Bilinear }
    public enum ImageBlendMode { Normal, Multiply, Screen, Add }
    // Legacy serialized values only; execution now always uses the graph working space.
    public enum ImageLevelsSpace { Linear, [InspectorName("sRGB")] SRgb, [InspectorName("Follow Graph")] FollowGraph }

    [Serializable]
    public struct ResizeSettings
    {
        [Min(1)] public int width, height;
        [Tooltip("Stretch: 拉伸；Contain: 等比缩放并留透明边；Cover: 等比填满并居中裁切。")]
        public ImageResizeFit fit;
        public ImageResizeSampling sampling;
        public static ResizeSettings Default => new()
            { width = 1024, height = 1024, fit = ImageResizeFit.Stretch, sampling = ImageResizeSampling.Bilinear };
    }

    [Serializable]
    public struct LevelsSettings
    {
        [HideInInspector] public string nodeId;
        // Retained to read old graph files without changing the serialized layout during domain reload.
        // Ignored by both the operation and histogram; not exposed as a node setting.
        [HideInInspector] public ImageLevelsSpace workingSpace;
        [Range(0f, 1f)] public float inputBlack, inputWhite;
        [Range(0.01f, 10f), Tooltip("1 为不变；大于 1 提亮中间调，小于 1 压暗。")]
        public float gamma;
        [Range(0f, 1f)] public float outputBlack, outputWhite;
        [Tooltip("勾选后将相同的色阶映射应用到 Alpha；默认保留原 Alpha。")]
        public bool affectAlpha;
        public static LevelsSettings Default => new() { inputWhite = 1, gamma = 1, outputWhite = 1 };
    }

    [Serializable]
    public struct BlendSettings
    {
        public ImageBlendMode mode;
        [Range(0f, 1f), Tooltip("前景的不透明度，范围 0–1，与前景 Alpha 和 Mask 相乘。")]
        public float opacity;
        public static BlendSettings Default => new() { mode = ImageBlendMode.Normal, opacity = 1 };
    }

    internal static class ImageFilterShader
    {
        internal const string Path = "Packages/com.tsukuyomi.ai-canvas/Editor/Shaders/ImageFilters.compute";
        private static ComputeShader shader;
        internal const string MaterialPath = "Packages/com.tsukuyomi.ai-canvas/Editor/Shaders/ImageMaterialFilters.compute";
        private static ComputeShader materialShader;
        internal const string ProceduralPath = "Packages/com.tsukuyomi.ai-canvas/Editor/Shaders/ImageProcedural.compute";
        private static ComputeShader proceduralShader;
        internal static ComputeShader LoadProcedural() => proceduralShader != null ? proceduralShader :
            proceduralShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ProceduralPath)
                ?? throw new InvalidOperationException("AiCanvas procedural image ComputeShader is missing.");
        internal static void Size(int width, int height, string name)
        {
            int limit = Math.Min(16384, SystemInfo.maxTextureSize);
            if (width < 1 || height < 1 || width > limit || height > limit)
                throw new InvalidOperationException($"{name} 的宽高必须在 1–{limit} 之间。");
        }
        internal static ComputeShader LoadMaterial() => materialShader != null ? materialShader :
            materialShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(MaterialPath)
                ?? throw new InvalidOperationException("AiCanvas material filter ComputeShader is missing.");
        internal static ComputeImageParameter WorkingSpace(ImageProcessingContext context)
            => ComputeImageParameter.Int("_WorkingSrgb", context.WorkingSpace == ImageWorkingSpace.SRgb ? 1 : 0);
        internal static void Range(float value, float min, float max, string name)
        {
            if (!float.IsFinite(value) || value < min || value > max)
                throw new InvalidOperationException($"{name} 必须在 {min}–{max} 之间。");
        }
        internal static void ValidEnum<T>(T value, string name) where T : struct, Enum
        {
            if (!Enum.IsDefined(typeof(T), value)) throw new InvalidOperationException(name + " 无效。");
        }
        internal static ComputeShader Load() => shader != null ? shader :
            shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(Path)
                ?? throw new InvalidOperationException("AiCanvas image filter ComputeShader is missing.");
        internal static void UnitRange(float value, string name)
            => Range(value, 0, 1, name);
    }

    [Serializable]
    [Node("Filters/Transform", "", "Resize")]
    public sealed class ResizeNode : ComputeImageNode
    {
        public ResizeSettings Settings => GraphOptionAccess.Read<ResizeSettings>(this, "Resize");
        protected override void OnDefineImageInputs(IPortDefinitionContext context)
            => context.AddInputPort<Texture2D>("Input").WithDisplayName("Image").Build();
        protected override void OnDefineComputeOptions(IOptionDefinitionContext context)
            => context.AddOption<ResizeSettings>("Resize").WithDefaultValue(ResizeSettings.Default).Build();
        public override ComputeImageOperation CreateOperation() => CreateOperation(default);
        public override ComputeImageOperation CreateOperation(ImageProcessingContext context)
        {
            var settings = Settings;
            ImageFilterShader.Size(settings.width, settings.height, "Resize");
            if (!Enum.IsDefined(typeof(ImageResizeFit), settings.fit) || !Enum.IsDefined(typeof(ImageResizeSampling), settings.sampling))
                throw new InvalidOperationException("Resize 的缩放或采样模式无效。");
            return new ComputeImageOperation(ImageFilterShader.Load(), "ResizeImage",
                new[] { new ComputeImageInput("Input", "_Input") },
                new[] { ComputeImageParameter.Int("_Fit", (int)settings.fit), ComputeImageParameter.Int("_Sampling", (int)settings.sampling),
                    ComputeImageParameter.Int("_WorkingSrgb", context.WorkingSpace == ImageWorkingSpace.SRgb ? 1 : 0) },
                settings.width, settings.height);
        }
        public override void OnEnable() { DefaultColor = new Color(0.35f, 0.72f, 0.72f); }
    }

    [Serializable]
    [Node("Filters/Adjustment", "", "Levels")]
    public sealed class LevelsNode : ComputeImageNode
    {
        public LevelsSettings Settings => GraphOptionAccess.Read<LevelsSettings>(this, "Levels");
        protected override void OnDefineImageInputs(IPortDefinitionContext context)
            => context.AddInputPort<Texture2D>("Input").WithDisplayName("Image").Build();
        protected override void OnDefineComputeOptions(IOptionDefinitionContext context)
        {
            var option = context.AddOption<LevelsSettings>("Levels").WithDefaultValue(LevelsSettings.Default).Build();
            if (option.TryGetValue<LevelsSettings>(out var settings) && settings.nodeId != this.GetStableId().ToString())
            {
                settings.nodeId = this.GetStableId().ToString();
                option.TrySetValue(settings);
            }
        }
        public override ComputeImageOperation CreateOperation() => CreateOperation(default);
        public override ComputeImageOperation CreateOperation(ImageProcessingContext context)
        {
            var settings = Settings;
            ImageFilterShader.UnitRange(settings.inputBlack, "Input Black");
            ImageFilterShader.UnitRange(settings.inputWhite, "Input White");
            ImageFilterShader.UnitRange(settings.outputBlack, "Output Black");
            ImageFilterShader.UnitRange(settings.outputWhite, "Output White");
            if (settings.inputWhite - settings.inputBlack < 0.00001f)
                throw new InvalidOperationException("Input White 必须大于 Input Black。");
            if (!float.IsFinite(settings.gamma) || settings.gamma < 0.01f || settings.gamma > 10f)
                throw new InvalidOperationException("Gamma 必须在 0.01–10 之间。");
            return new ComputeImageOperation(ImageFilterShader.Load(), "LevelsImage",
                new[] { new ComputeImageInput("Input", "_Input") }, new[]
                {
                    ComputeImageParameter.Vector("_Levels", new Vector4(settings.inputBlack, settings.inputWhite, settings.outputBlack, settings.outputWhite)),
                    ComputeImageParameter.Float("_InverseGamma", 1f / settings.gamma),
                    ComputeImageParameter.Int("_AffectAlpha", settings.affectAlpha ? 1 : 0),
                    ComputeImageParameter.Int("_WorkingSrgb", context.WorkingSpace == ImageWorkingSpace.SRgb ? 1 : 0)
                });
        }
        public override void OnEnable() { DefaultColor = new Color(0.78f, 0.65f, 0.32f); }
    }

    [Serializable]
    [Node("Filters/Blending", "", "Blend")]
    public sealed class BlendNode : ComputeImageNode
    {
        public BlendSettings Settings => GraphOptionAccess.Read<BlendSettings>(this, "Blend");
        protected override void OnDefineImageInputs(IPortDefinitionContext context)
        {
            context.AddInputPort<Texture2D>("Background").WithTooltip("背景图片；输出继承其尺寸和颜色类型。").Build();
            context.AddInputPort<Texture2D>("Foreground").WithTooltip("前景图片；尺寸不同时缩放到背景大小。").Build();
            context.AddInputPort<Texture2D>("Mask").WithTooltip("可选蒙版，使用 R 通道：0 保留背景，1 应用前景；不连接时为 1。").Build();
        }
        protected override void OnDefineComputeOptions(IOptionDefinitionContext context)
            => context.AddOption<BlendSettings>("Blend").WithDefaultValue(BlendSettings.Default).Build();
        public override ComputeImageOperation CreateOperation() => CreateOperation(default);
        public override ComputeImageOperation CreateOperation(ImageProcessingContext context)
        {
            var settings = Settings;
            ImageFilterShader.UnitRange(settings.opacity, "Opacity");
            if (!Enum.IsDefined(typeof(ImageBlendMode), settings.mode)) throw new InvalidOperationException("Blend 模式无效。");
            return new ComputeImageOperation(ImageFilterShader.Load(), "BlendImage", new[]
            {
                new ComputeImageInput("Background", "_Background"),
                new ComputeImageInput("Foreground", "_Foreground"),
                new ComputeImageInput("Mask", "_Mask", optional: true)
            }, new[] { ComputeImageParameter.Int("_BlendMode", (int)settings.mode), ComputeImageParameter.Float("_Opacity", settings.opacity),
                ComputeImageParameter.Int("_WorkingSrgb", context.WorkingSpace == ImageWorkingSpace.SRgb ? 1 : 0) });
        }
        public override void OnEnable() { DefaultColor = new Color(0.68f, 0.47f, 0.78f); }
    }
}
