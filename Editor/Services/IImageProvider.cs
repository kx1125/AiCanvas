using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Tsukuyomi.AiCanvas.Editor
{
    public sealed class ImageRequest
    {
        public string Prompt { get; }
        public string TaskPrompt { get; }
        public IReadOnlyList<ImageInputData> Inputs { get; }
        public ImageInputSnapshot MainImage { get; }
        public ImageInputSnapshot Mask { get; }
        // Ordered API images: Main first, then References. Mask is always separate.
        public IReadOnlyList<ImageInputSnapshot> References { get; }
        public GenerationSettings Settings { get; }
        public string Size => Settings.Size;
        public string Quality => Settings.Quality;
        public int Count => Settings.OutputCount;

        public ImageRequest(string prompt, IReadOnlyList<ImageInputData> inputs, GenerationSettings settings)
        {
            settings.Validate();
            Settings = settings;
            var snapshot = (inputs ?? Array.Empty<ImageInputData>()).ToList();
            if (snapshot.Any(input => input == null || input.Image == null))
                throw new ArgumentException("Image Input must not contain an empty texture.", nameof(inputs));
            ImageInputRules.ValidateRoles(snapshot.Select(input => input.Role));
            snapshot = snapshot.OrderBy(input => input.Role).ToList();
            Inputs = snapshot.AsReadOnly();
            MainImage = snapshot.FirstOrDefault(input => input.Role == ImageInputRole.Main)?.Image;
            Mask = snapshot.FirstOrDefault(input => input.Role == ImageInputRole.Mask)?.Image;
            ImageInputRules.ValidateMask(MainImage, Mask);
            TaskPrompt = prompt ?? "";
            ImageInputRules.ValidatePrompts(TaskPrompt, Inputs.Select(input => input.Prompt));
            Prompt = ImageInputRules.ComposePrompt(TaskPrompt, Inputs);
            References = snapshot.Where(input => input.Role != ImageInputRole.Mask).Select(input => input.Image).ToList().AsReadOnly();
        }
    }

    public interface IImageProvider
    {
        string Name { get; }
        Task<IReadOnlyList<byte[]>> GenerateAsync(ImageRequest request, CancellationToken cancellationToken);
    }

    /// <summary>Optional incremental delivery. Deliver zero-based indices in order and await
    /// each callback before starting the next image. Existing IImageProvider implementations remain supported.</summary>
    public interface IIncrementalImageProvider : IImageProvider
    {
        Task GenerateAsync(ImageRequest request, Func<int, byte[], Task> onImageCompleted,
            CancellationToken cancellationToken);
    }

}
