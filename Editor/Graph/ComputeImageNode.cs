using System;
using System.Linq;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    /// <summary>Base for local GPU image nodes. Ports remain Texture2D-compatible;
    /// their evaluated values are GPU textures owned by the execution layer.</summary>
    [Serializable]
    public abstract class ComputeImageNode : Node
    {
        public abstract ComputeImageOperation CreateOperation();

        // Existing custom nodes remain compatible; new color-aware nodes override this overload.
        public virtual ComputeImageOperation CreateOperation(ImageProcessingContext context) => CreateOperation();

        public virtual int OutputCount => 1;
        public virtual string OutputName(int index)
        {
            if (index != 0) throw new ArgumentOutOfRangeException(nameof(index));
            return "Image";
        }

        // Each output owns an immutable operation and independent cache entry.
        public virtual ComputeImageOperation CreateOperation(ImageProcessingContext context, int outputIndex)
        {
            if (outputIndex != 0) throw new ArgumentOutOfRangeException(nameof(outputIndex));
            return CreateOperation(context);
        }

        protected sealed override void OnDefinePorts(IPortDefinitionContext context)
        {
            OnDefineImageInputs(context);
            for (int index = 0; index < OutputCount; index++)
                context.AddOutputPort<Texture2D>(OutputName(index)).Build();
        }

        protected abstract void OnDefineImageInputs(IPortDefinitionContext context);
        protected virtual void OnDefineComputeOptions(IOptionDefinitionContext context) { }

        protected sealed override void OnDefineOptions(IOptionDefinitionContext context)
        {
            OnDefineComputeOptions(context);
            ImagePreviewOptions.Define(context, this.GetStableId().ToString(),
                OutputCount > 1 ? Enumerable.Range(0, OutputCount).Select(OutputName).ToArray() : null);
        }
    }

    internal static class ImagePreviewOptions
    {
        internal static void Define(Node.IOptionDefinitionContext context, string nodeId, string[] outputNames = null)
        {
            var option = context.AddOption<ImageCard>("Preview")
                .WithDefaultValue(new ImageCard(null, "Local GPU preview", nodeId) { outputNames = outputNames }).Build();
            // Upgrade existing Image Output cards without changing the serialized option type.
            if (option.TryGetValue<ImageCard>(out var card) && (card.nodeId != nodeId ||
                !(card.outputNames ?? Array.Empty<string>()).SequenceEqual(outputNames ?? Array.Empty<string>())))
            {
                card.nodeId = nodeId;
                card.outputNames = outputNames;
                card.outputIndex = Mathf.Clamp(card.outputIndex, 0, Math.Max(0, (outputNames?.Length ?? 1) - 1));
                option.TrySetValue(card);
            }
        }
    }
}
