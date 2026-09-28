using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    public enum ImageValueKind { Inherit, Color, Data }

    [Serializable]
    public struct ComputeImageInput
    {
        public string PortName, ShaderName;
        public bool Optional;
        public ComputeImageInput(string portName, string shaderName, bool optional = false)
        { PortName = portName; ShaderName = shaderName; Optional = optional; }
    }

    public enum ComputeParameterKind { Float, Int, Vector }

    [Serializable]
    public struct ComputeImageParameter
    {
        public string Name;
        public ComputeParameterKind Kind;
        public Vector4 Value;
        public int IntValue;
        public static ComputeImageParameter Float(string name, float value)
            => new() { Name = name, Kind = ComputeParameterKind.Float, Value = new Vector4(value, 0, 0, 0) };
        public static ComputeImageParameter Int(string name, int value)
            => new() { Name = name, Kind = ComputeParameterKind.Int, IntValue = value };
        public static ComputeImageParameter Vector(string name, Vector4 value)
            => new() { Name = name, Kind = ComputeParameterKind.Vector, Value = value };

        internal void Apply(ComputeShader shader)
        {
            switch (Kind)
            {
                case ComputeParameterKind.Float: shader.SetFloat(Name, Value.x); break;
                case ComputeParameterKind.Int: shader.SetInt(Name, IntValue); break;
                case ComputeParameterKind.Vector: shader.SetVector(Name, Value); break;
                default: throw new ArgumentOutOfRangeException(nameof(Kind));
            }
        }
    }

    /// <summary>Immutable parameter snapshot. Kernels write every pixel of _Output, obey
    /// _Width/_Height bounds and treat Color RGB as linear, Alpha/Data as unmodified values.</summary>
    public sealed class ComputeImageOperation
    {
        public ComputeShader Shader { get; }
        public string Kernel { get; }
        // Optional second pass reads the first pass through the sole input binding.
        // Both passes use the same dimensions, parameters and linear Color / raw Data convention.
        public string FollowUpKernel { get; }
        public int Width { get; }
        public int Height { get; }
        public ImageValueKind ValueKind { get; }
        public IReadOnlyList<ComputeImageInput> Inputs { get; }
        public IReadOnlyList<ComputeImageParameter> Parameters { get; }
        public string Fingerprint { get; }
        internal int SplitChannel { get; private set; } = -1;

        // Only built-in split operations opt into batching. Custom kernels keep their
        // existing one-output protocol, while every split output retains its cache key.
        internal static ComputeImageOperation CreateChannelSplit(ComputeShader shader, int channel)
            => new ComputeImageOperation(shader, "RgbaSplitImage",
                new[] { new ComputeImageInput("Input", "_Input") },
                new[] { ComputeImageParameter.Int("_SplitChannel", channel) }, valueKind: ImageValueKind.Data)
                { SplitChannel = channel };

        public ComputeImageOperation(ComputeShader shader, string kernel,
            IEnumerable<ComputeImageInput> inputs, IEnumerable<ComputeImageParameter> parameters = null,
            int width = 0, int height = 0, ImageValueKind valueKind = ImageValueKind.Inherit,
            string followUpKernel = null)
        {
            if (shader == null) throw new ArgumentNullException(nameof(shader));
            if (string.IsNullOrWhiteSpace(kernel)) throw new ArgumentException("Compute kernel is required.");
            if (width < 0 || height < 0 || (width == 0) != (height == 0))
                throw new ArgumentException("Specify both output dimensions, or use 0, 0 to inherit the first input size.");
            Shader = shader; Kernel = kernel; Width = width; Height = height; ValueKind = valueKind;
            var inputArray = (inputs ?? Array.Empty<ComputeImageInput>()).ToArray();
            var parameterArray = (parameters ?? Array.Empty<ComputeImageParameter>()).ToArray();
            if (followUpKernel != null && (string.IsNullOrWhiteSpace(followUpKernel) || inputArray.Length != 1))
                throw new ArgumentException("A two-pass operation requires one input and a non-empty follow-up kernel.");
            FollowUpKernel = followUpKernel;
            if (inputArray.Length == 0 && (width == 0 || valueKind == ImageValueKind.Inherit))
                throw new ArgumentException("A generator must specify dimensions and Color/Data output kind.");
            var names = new HashSet<string> { "_Output", "_Width", "_Height" };
            var ports = new HashSet<string>();
            foreach (var input in inputArray)
                if (string.IsNullOrEmpty(input.PortName) || string.IsNullOrEmpty(input.ShaderName) ||
                    !ports.Add(input.PortName) || !names.Add(input.ShaderName) || !names.Add(input.ShaderName + "IsColor"))
                    throw new ArgumentException("Compute input names must be non-empty and unique.");
            foreach (var parameter in parameterArray)
                if (string.IsNullOrEmpty(parameter.Name) || !names.Add(parameter.Name) ||
                    !Enum.IsDefined(typeof(ComputeParameterKind), parameter.Kind) ||
                    !float.IsFinite(parameter.Value.x) || !float.IsFinite(parameter.Value.y) ||
                    !float.IsFinite(parameter.Value.z) || !float.IsFinite(parameter.Value.w))
                    throw new ArgumentException("Compute parameters must be finite and have unique, non-reserved names.");
            if (!Enum.IsDefined(typeof(ImageValueKind), valueKind)) throw new ArgumentOutOfRangeException(nameof(valueKind));
            Inputs = Array.AsReadOnly(inputArray);
            Parameters = Array.AsReadOnly(parameterArray);
            Fingerprint = JsonUtility.ToJson(new OperationRecord
            {
                shader = ImageAssetQueryScope.ShaderVersion(shader),
                kernel = kernel, followUpKernel = followUpKernel, width = width, height = height, kind = valueKind,
                inputs = inputArray, parameters = parameterArray
            });
        }

        [Serializable] private sealed class OperationRecord
        {
            public string shader, kernel, followUpKernel;
            public int width, height;
            public ImageValueKind kind;
            public ComputeImageInput[] inputs;
            public ComputeImageParameter[] parameters;
        }
    }
}
