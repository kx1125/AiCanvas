using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    internal sealed class ImageSource
    {
        public Texture2D Reference;
        public string ReferenceId;
        internal string Fingerprint => ReferenceId != null ? "result:" + ReferenceId : "asset:" + ImageInputSnapshot.GetVersion(Reference);
    }
    internal enum ImageExecutionMode { Generate, LocalPreview }
    internal enum ImageStepKind { Generate, Compute }
    internal sealed class PlanInput
    {
        public ImageSource Source;
        public ImageInputRole Role;
        public string Prompt, NodeId;
    }
    internal sealed class PlanStep
    {
        public ImageStepKind Kind;
        public string Id, Prompt;
        public int OutputIndex;
        public GenerationSettings Settings;
        public List<PlanInput> Inputs = new List<PlanInput>();
        public ComputeImageOperation Operation;
        public List<ImageSource> Images = new();
        internal IEnumerable<ImageSource> Sources => Kind == ImageStepKind.Compute ? Images : Inputs.Select(input => input.Source);
    }
    internal sealed class ExecutionPlan
    {
        internal static string ResultKey(string id, int index) => id + ":" + index;
        public readonly List<PlanStep> Steps = new List<PlanStep>();
        public readonly Dictionary<string, ImageSource> Outputs = new Dictionary<string, ImageSource>();
        public readonly Dictionary<string, string> PreviewErrors = new();
        public bool HasGeneration => Steps.Any(step => step.Kind == ImageStepKind.Generate);
        public IEnumerable<ImageSource> Sources => Steps.SelectMany(step => step.Sources).Concat(Outputs.Values);
        public string Fingerprint
        {
            get
            {
                using var assetQueries = new ImageAssetQueryScope();
                return SerializeFingerprint();
            }
        }

        private string SerializeFingerprint() => JsonUtility.ToJson(new FingerprintData
        {
            steps = Steps.Select(s => new FingerprintStep
            {
                id = s.Id, kind = s.Kind, outputIndex = s.OutputIndex, operation = s.Operation?.Fingerprint,
                images = s.Images.Select(image => image.Fingerprint).ToArray(), settings = s.Settings, prompt = s.Prompt,
                inputs = s.Inputs.Select(input => new InputRecord { nodeId = input.NodeId, role = input.Role.ToString(),
                    prompt = input.Prompt, imagePath = input.Source.Fingerprint }).ToArray()
            }).ToArray(),
            outputs = Outputs.OrderBy(p => p.Key).Select(p => $"{p.Key}|{p.Value.ReferenceId}|{ImageInputSnapshot.GetVersion(p.Value.Reference)}").ToArray(),
            errors = PreviewErrors.OrderBy(pair => pair.Key).Select(pair => pair.Key + "|" + pair.Value).ToArray()
        });
    }
    [Serializable] internal sealed class FingerprintData { public FingerprintStep[] steps; public string[] outputs, errors; }
    [Serializable] internal sealed class FingerprintStep
    {
        public string id, prompt, operation;
        public int outputIndex;
        public ImageStepKind kind;
        public string[] images;
        public GenerationSettings settings;
        public InputRecord[] inputs;
    }
    [Serializable] internal sealed class InputRecord { public string nodeId, role, prompt, imagePath; }
}
