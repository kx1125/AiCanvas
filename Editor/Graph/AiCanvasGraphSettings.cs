using System;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    public enum ImageWorkingSpace { Linear, [InspectorName("sRGB")] SRgb }

    [Serializable]
    public struct AiCanvasGraphSettings
    {
        public ImageWorkingSpace workingSpace;
        public static AiCanvasGraphSettings Default => new() { workingSpace = ImageWorkingSpace.SRgb };

        internal void Validate()
        {
            if (!Enum.IsDefined(typeof(ImageWorkingSpace), workingSpace))
                throw new ArgumentOutOfRangeException(nameof(workingSpace));
        }
    }

    /// <summary>Immutable graph settings captured while building an execution plan.</summary>
    public readonly struct ImageProcessingContext
    {
        public ImageWorkingSpace WorkingSpace { get; }
        public ImageProcessingContext(ImageWorkingSpace workingSpace)
        {
            if (!Enum.IsDefined(typeof(ImageWorkingSpace), workingSpace))
                throw new ArgumentOutOfRangeException(nameof(workingSpace));
            WorkingSpace = workingSpace;
        }
    }
}
