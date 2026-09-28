using System;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    public enum ImageGradientDirection { Horizontal, Vertical }
    public enum ImageShapeType { Disc, Rectangle, Diamond }

    [Serializable]
    public struct GradientLinearSettings
    {
        [Min(1)] public int width, height;
        public ImageGradientDirection direction;
        [Range(1, 64)] public int cycles;
        [Range(0, 1)] public float phase;
        [Tooltip("往返渐变使首尾连续；关闭时每周期为黑到白的锯齿渐变。")]
        public bool mirror;
        public bool invert;
        public static GradientLinearSettings Default => new() { width = 1024, height = 1024, cycles = 1 };
    }

    [Serializable]
    public struct ShapeSettings
    {
        [Min(1)] public int width, height;
        public ImageShapeType shape;
        [Tooltip("形状占画布的宽高比例，各轴为 0.01–1。")]
        public Vector2 size;
        [Range(0, 1), Tooltip("边缘向内柔化的比例；0 仍保留一像素左右的抗锯齿。")]
        public float feather;
        public static ShapeSettings Default => new() { width = 1024, height = 1024, size = new Vector2(0.8f, 0.8f) };
    }

    [Serializable]
    public struct TileableNoiseSettings
    {
        [Min(1)] public int width, height;
        [Range(1, 128), Tooltip("基础周期中的格点数，X/Y 相同。")]
        public int scale;
        [Range(1, 8)] public int octaves;
        [Range(0, 1), Tooltip("每层相对上一层的振幅，0 只保留第一层。")]
        public float roughness;
        public int seed;
        [Tooltip("按 UV 平移，整数偏移仍返回同一周期；范围 -100–100。")]
        public Vector2 offset;
        public static TileableNoiseSettings Default => new() { width = 1024, height = 1024, scale = 8, octaves = 4, roughness = 0.5f };
    }

    [Serializable]
    public struct TileGeneratorSettings
    {
        [Min(1)] public int width, height;
        [Range(1, 64)] public int columns, rows;
        public ImageShapeType shape;
        [Tooltip("形状占每个格子的宽高比例，各轴为 0.01–1；减小以增加间距。")]
        public Vector2 size;
        [Range(0, 1)] public float feather;
        [Range(0, 1), Tooltip("奇数行相对偶数行的水平错位，单位为格子宽度。")]
        public float rowOffset;
        [Range(-360, 360)] public float rotation;
        public int seed;
        [Range(0, 0.5f), Tooltip("中心沿每个轴随机偏移的最大格子比例。")]
        public float jitter;
        [Range(0, 1), Tooltip("随机缩小，最大随机量时尺寸为原尺寸的 10%–100%。")]
        public float sizeRandom;
        [Range(0, 180)] public float rotationRandom;
        [Range(0, 1)] public float valueRandom;
        [Range(0, 1), Tooltip("每格随机隐藏的概率，1 隐藏全部格子。")]
        public float missing;
        public Vector2 offset;
        public static TileGeneratorSettings Default => new()
        {
            width = 1024, height = 1024, columns = 8, rows = 8, shape = ImageShapeType.Rectangle,
            size = new Vector2(0.85f, 0.85f), rowOffset = 0.5f, valueRandom = 0.15f
        };
    }

    internal static class ImagePatternValidation
    {
        internal static void Shape(ImageShapeType shape, Vector2 size, float feather)
        {
            ImageFilterShader.ValidEnum(shape, "Shape");
            ImageFilterShader.Range(size.x, 0.01f, 1, "Size X"); ImageFilterShader.Range(size.y, 0.01f, 1, "Size Y");
            ImageFilterShader.UnitRange(feather, "Feather");
        }
        internal static void Offset(Vector2 offset)
        {
            ImageFilterShader.Range(offset.x, -100, 100, "Offset X"); ImageFilterShader.Range(offset.y, -100, 100, "Offset Y");
        }
    }

    [Serializable]
    [Node("Generators/Gradients", "", "Gradient Linear")]
    public sealed class GradientLinearNode : ComputeImageNode
    {
        public GradientLinearSettings Settings => GraphOptionAccess.Read<GradientLinearSettings>(this, "Gradient");
        protected override void OnDefineImageInputs(IPortDefinitionContext context) { }
        protected override void OnDefineComputeOptions(IOptionDefinitionContext context)
            => context.AddOption<GradientLinearSettings>("Gradient").WithDefaultValue(GradientLinearSettings.Default).Build();
        public override ComputeImageOperation CreateOperation()
        {
            var s = Settings;
            ImageFilterShader.Size(s.width, s.height, "Gradient Linear");
            ImageFilterShader.ValidEnum(s.direction, "Direction");
            ImageFilterShader.Range(s.cycles, 1, 64, "Cycles"); ImageFilterShader.UnitRange(s.phase, "Phase");
            return new ComputeImageOperation(ImageFilterShader.LoadProcedural(), "GradientLinearImage", Array.Empty<ComputeImageInput>(), new[]
            {
                ComputeImageParameter.Int("_Direction", (int)s.direction), ComputeImageParameter.Int("_Cycles", s.cycles),
                ComputeImageParameter.Float("_Phase", s.phase), ComputeImageParameter.Int("_Mirror", s.mirror ? 1 : 0),
                ComputeImageParameter.Int("_Invert", s.invert ? 1 : 0)
            }, s.width, s.height, ImageValueKind.Data);
        }
        public override void OnEnable() { DefaultColor = new Color(0.75f, 0.48f, 0.35f); }
    }

    [Serializable]
    [Node("Generators/Patterns", "", "Shape")]
    public sealed class ShapeNode : ComputeImageNode
    {
        public ShapeSettings Settings => GraphOptionAccess.Read<ShapeSettings>(this, "Shape");
        protected override void OnDefineImageInputs(IPortDefinitionContext context) { }
        protected override void OnDefineComputeOptions(IOptionDefinitionContext context)
            => context.AddOption<ShapeSettings>("Shape").WithDefaultValue(ShapeSettings.Default).Build();
        public override ComputeImageOperation CreateOperation()
        {
            var s = Settings;
            ImageFilterShader.Size(s.width, s.height, "Shape"); ImagePatternValidation.Shape(s.shape, s.size, s.feather);
            return new ComputeImageOperation(ImageFilterShader.LoadProcedural(), "ShapeImage", Array.Empty<ComputeImageInput>(), new[]
            {
                ComputeImageParameter.Int("_Shape", (int)s.shape), ComputeImageParameter.Float("_Feather", s.feather),
                ComputeImageParameter.Vector("_PatternSize", new Vector4(s.size.x, s.size.y, 0, 0))
            }, s.width, s.height, ImageValueKind.Data);
        }
        public override void OnEnable() { DefaultColor = new Color(0.75f, 0.48f, 0.35f); }
    }

    [Serializable]
    [Node("Generators/Noises", "", "Tileable Noise")]
    public sealed class TileableNoiseNode : ComputeImageNode
    {
        public TileableNoiseSettings Settings => GraphOptionAccess.Read<TileableNoiseSettings>(this, "Noise");
        protected override void OnDefineImageInputs(IPortDefinitionContext context) { }
        protected override void OnDefineComputeOptions(IOptionDefinitionContext context)
            => context.AddOption<TileableNoiseSettings>("Noise").WithDefaultValue(TileableNoiseSettings.Default).Build();
        public override ComputeImageOperation CreateOperation()
        {
            var s = Settings;
            ImageFilterShader.Size(s.width, s.height, "Tileable Noise"); ImagePatternValidation.Offset(s.offset);
            ImageFilterShader.Range(s.scale, 1, 128, "Scale"); ImageFilterShader.Range(s.octaves, 1, 8, "Octaves");
            ImageFilterShader.UnitRange(s.roughness, "Roughness");
            return new ComputeImageOperation(ImageFilterShader.LoadProcedural(), "TileableNoiseImage", Array.Empty<ComputeImageInput>(), new[]
            {
                ComputeImageParameter.Int("_NoiseScale", s.scale), ComputeImageParameter.Int("_Octaves", s.octaves),
                ComputeImageParameter.Float("_Roughness", s.roughness), ComputeImageParameter.Int("_Seed", s.seed),
                ComputeImageParameter.Vector("_Offset", new Vector4(s.offset.x, s.offset.y, 0, 0))
            }, s.width, s.height, ImageValueKind.Data);
        }
        public override void OnEnable() { DefaultColor = new Color(0.75f, 0.48f, 0.35f); }
    }

    [Serializable]
    [Node("Generators/Patterns", "", "Tile Generator")]
    public sealed class TileGeneratorNode : ComputeImageNode
    {
        public TileGeneratorSettings Settings => GraphOptionAccess.Read<TileGeneratorSettings>(this, "Tiles");
        protected override void OnDefineImageInputs(IPortDefinitionContext context) { }
        protected override void OnDefineComputeOptions(IOptionDefinitionContext context)
            => context.AddOption<TileGeneratorSettings>("Tiles").WithDefaultValue(TileGeneratorSettings.Default).Build();
        public override ComputeImageOperation CreateOperation()
        {
            var s = Settings;
            ImageFilterShader.Size(s.width, s.height, "Tile Generator"); ImagePatternValidation.Shape(s.shape, s.size, s.feather);
            ImagePatternValidation.Offset(s.offset);
            ImageFilterShader.Range(s.columns, 1, 64, "Columns"); ImageFilterShader.Range(s.rows, 1, 64, "Rows");
            ImageFilterShader.UnitRange(s.rowOffset, "Row Offset"); ImageFilterShader.Range(s.rotation, -360, 360, "Rotation");
            ImageFilterShader.Range(s.jitter, 0, 0.5f, "Jitter"); ImageFilterShader.UnitRange(s.sizeRandom, "Size Random");
            ImageFilterShader.Range(s.rotationRandom, 0, 180, "Rotation Random");
            ImageFilterShader.UnitRange(s.valueRandom, "Value Random"); ImageFilterShader.UnitRange(s.missing, "Missing");
            return new ComputeImageOperation(ImageFilterShader.LoadProcedural(), "TileGeneratorImage", Array.Empty<ComputeImageInput>(), new[]
            {
                ComputeImageParameter.Vector("_TileCount", new Vector4(s.columns, s.rows, 0, 0)),
                ComputeImageParameter.Vector("_PatternSize", new Vector4(s.size.x, s.size.y, 0, 0)),
                ComputeImageParameter.Int("_Shape", (int)s.shape), ComputeImageParameter.Float("_Feather", s.feather),
                ComputeImageParameter.Float("_RowOffset", s.rowOffset), ComputeImageParameter.Int("_Seed", s.seed),
                ComputeImageParameter.Float("_Jitter", s.jitter), ComputeImageParameter.Float("_SizeRandom", s.sizeRandom),
                ComputeImageParameter.Float("_TileRotation", s.rotation * Mathf.Deg2Rad),
                ComputeImageParameter.Float("_RotationRandom", s.rotationRandom * Mathf.Deg2Rad),
                ComputeImageParameter.Float("_ValueRandom", s.valueRandom), ComputeImageParameter.Float("_Missing", s.missing),
                ComputeImageParameter.Vector("_Offset", new Vector4(s.offset.x, s.offset.y, 0, 0))
            }, s.width, s.height, ImageValueKind.Data);
        }
        public override void OnEnable() { DefaultColor = new Color(0.75f, 0.48f, 0.35f); }
    }
}
