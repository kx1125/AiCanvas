using System;
using System.Linq;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    [Serializable]
    internal sealed class ImageRunRecord
    {
        public string provider, prompt, taskPrompt, graph, nodeId, scope, referencePath, maskPath, outputPath, createdUtc;
        public string batchId, size, quality;
        public int count, outputIndex, width, height;
        public string[] referencePaths;
        public InputRecord[] inputs;

        internal static ImageRunRecord Create(ImageRequest request, string provider, string graph, string nodeId,
            string scope, string batchId, int index, string path, Texture2D texture)
            => new ImageRunRecord
            {
                provider = provider, prompt = request.Prompt, taskPrompt = request.TaskPrompt, graph = graph,
                nodeId = nodeId, scope = scope, batchId = batchId, outputIndex = index + 1,
                size = request.Size, quality = request.Quality, count = request.Count,
                width = texture.width, height = texture.height,
                referencePath = request.References.FirstOrDefault()?.AssetPath ?? "",
                referencePaths = request.References.Select(image => image.AssetPath).ToArray(),
                maskPath = request.Mask?.AssetPath ?? "", outputPath = path, createdUtc = DateTime.UtcNow.ToString("O"),
                inputs = request.Inputs.Select(input => new InputRecord
                {
                    nodeId = input.NodeId, role = input.Role.ToString(), prompt = input.Prompt,
                    imagePath = input.Image.AssetPath, assetVersion = input.Image.AssetVersion,
                    contentHash = input.Image.ContentHash
                }).ToArray()
            };

        [Serializable]
        internal sealed class InputRecord
        {
            public string nodeId, role, prompt, imagePath, assetVersion, contentHash;
        }
    }
}
