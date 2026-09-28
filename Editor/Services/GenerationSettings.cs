using System;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    public enum ImageSizePreset
    {
        Auto,
        [InspectorName("1024 × 1024")] Square,
        [InspectorName("1536 × 1024")] Landscape,
        [InspectorName("1024 × 1536")] Portrait,
        [InspectorName("2048 × 2048")] Square2K,
        [InspectorName("2048 × 1152")] Landscape2K,
        [InspectorName("1152 × 2048")] Portrait2K,
        [InspectorName("3840 × 2160")] Landscape4K,
        [InspectorName("2160 × 3840")] Portrait4K,
        Custom
    }

    public enum ImageQuality { Auto, Low, Medium, High }

    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Tsukuyomi.ImageCanvas.Editor",
        sourceAssembly: "Tsukuyomi.ImageCanvas.Editor", sourceClassName: "GenerationSettings")]
    [Serializable]
    public struct GenerationSettings
    {
        public ImageSizePreset size;
        public int width;
        public int height;
        public ImageQuality quality;
        [Range(1, 10)] public int count;

        public static GenerationSettings Default => new GenerationSettings
        {
            size = ImageSizePreset.Square, width = 1024, height = 1024,
            quality = ImageQuality.Auto, count = 1
        };

        public int OutputCount => Mathf.Clamp(count, 1, 10);
        public string Quality => quality.ToString().ToLowerInvariant();
        public string Size => size == ImageSizePreset.Auto ? "auto" : $"{Dimensions.x}x{Dimensions.y}";
        // Auto delegates dimensions to the provider; fixed dimensions apply only to explicit sizes.
        public Vector2Int Dimensions => size switch
        {
            ImageSizePreset.Auto => Vector2Int.zero,
            ImageSizePreset.Square => new Vector2Int(1024, 1024),
            ImageSizePreset.Landscape => new Vector2Int(1536, 1024),
            ImageSizePreset.Portrait => new Vector2Int(1024, 1536),
            ImageSizePreset.Square2K => new Vector2Int(2048, 2048),
            ImageSizePreset.Landscape2K => new Vector2Int(2048, 1152),
            ImageSizePreset.Portrait2K => new Vector2Int(1152, 2048),
            ImageSizePreset.Landscape4K => new Vector2Int(3840, 2160),
            ImageSizePreset.Portrait4K => new Vector2Int(2160, 3840),
            ImageSizePreset.Custom => new Vector2Int(width, height),
            _ => throw new InvalidOperationException("Unsupported image size.")
        };

        public void Validate()
        {
            if (!Enum.IsDefined(typeof(ImageSizePreset), size) || !Enum.IsDefined(typeof(ImageQuality), quality))
                throw new InvalidOperationException("Unsupported image size or quality.");
            if (count < 1 || count > 10)
                throw new InvalidOperationException("Output Count must be between 1 and 10.");
            if (size == ImageSizePreset.Auto) return;
            var dimensions = Dimensions;
            long pixels = (long)dimensions.x * dimensions.y;
            if (dimensions.x <= 0 || dimensions.y <= 0 || dimensions.x > 3840 || dimensions.y > 3840 ||
                dimensions.x % 16 != 0 || dimensions.y % 16 != 0 ||
                pixels < 655360 || pixels > 8294400 ||
                dimensions.x > dimensions.y * 3L || dimensions.y > dimensions.x * 3L)
                throw new InvalidOperationException("图片尺寸必须为 16 的倍数，长宽比在 1:3–3:1 之间，边长不超过 3840，总像素在 655360–8294400 之间。");
        }
    }
}
