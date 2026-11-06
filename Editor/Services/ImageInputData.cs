using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    public enum ImageInputRole { Main, Reference, Mask }

    // Graph Toolkit 6000.6 stores plain enum options in an internal wrapper that
    // INodeOption.TrySetValue<TEnum> cannot write. A serializable option keeps
    // the dropdown and programmatic edits on the supported public API.
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Tsukuyomi.ImageCanvas.Editor",
        sourceAssembly: "Tsukuyomi.ImageCanvas.Editor", sourceClassName: "ImageRoleOption")]
    [Serializable]
    public struct ImageRoleOption
    {
        public ImageInputRole role;
        public ImageRoleOption(ImageInputRole role) { this.role = role; }
    }

    /// <summary>One image's role and instructions, captured when Run is clicked.</summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Tsukuyomi.ImageCanvas.Editor",
        sourceAssembly: "Tsukuyomi.ImageCanvas.Editor", sourceClassName: "ImageInputData")]
    [Serializable]
    public sealed class ImageInputData
    {
        public ImageInputSnapshot Image { get; }
        public ImageInputRole Role { get; }
        public string Prompt { get; }
        public string NodeId { get; }

        public ImageInputData(ImageInputSnapshot image, ImageInputRole role, string prompt = "", string nodeId = "")
        {
            Image = image;
            Role = role;
            Prompt = prompt ?? "";
            NodeId = nodeId ?? "";
        }
    }

    internal static class ImageInputRules
    {
        internal static void ValidatePrompts(string taskPrompt, IEnumerable<string> inputPrompts)
        {
            if (string.IsNullOrWhiteSpace(taskPrompt) && inputPrompts.All(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException("请填写 AiGenerate 的 Prompt，或在连接到 Inputs 的 Image Input 中填写 Prompt。");
        }

        internal static void ValidateRoles(IEnumerable<ImageInputRole> roles)
        {
            var values = roles.ToList();
            if (values.Any(role => !Enum.IsDefined(typeof(ImageInputRole), role)))
                throw new InvalidOperationException("Image Input 的 Role 无效。");
            if (values.Count(role => role == ImageInputRole.Main) > 1)
                throw new InvalidOperationException("每个 AiGenerate 最多连接一个 Main。");
            if (values.Count(role => role == ImageInputRole.Mask) > 1)
                throw new InvalidOperationException("每个 AiGenerate 最多连接一个 Mask。");
            if (values.Contains(ImageInputRole.Mask) && !values.Contains(ImageInputRole.Main))
                throw new InvalidOperationException("Mask 必须同时连接一个 Main，指定要编辑的主图。");
            if (values.Count(role => role != ImageInputRole.Mask) > 16)
                throw new InvalidOperationException("Main 和 Reference 合计最多 16 张图片。");
        }

        internal static void ValidateMask(Texture2D main, Texture2D mask)
        {
            if (mask == null) return;
            if (main == null) throw new InvalidOperationException("Mask 缺少 Main 图片。");
            var mainSize = new ImageSourcePixels(main).Size;
            var maskSize = new ImageSourcePixels(mask).Size;
            if (mainSize != maskSize)
                throw new InvalidOperationException($"Mask 尺寸必须与 Main 相同：Main {mainSize.x}×{mainSize.y}，Mask {maskSize.x}×{maskSize.y}。");

            bool hasAlpha = ImageInputSnapshot.HasSourceAlpha(mask);
            if (!hasAlpha)
                throw new InvalidOperationException("Mask 图片必须包含 Alpha 通道；黑白 RGB 图片需要先转换为 Alpha 蒙版。");
        }

        internal static void ValidateMask(ImageInputSnapshot main, ImageInputSnapshot mask)
        {
            if (mask == null) return;
            if (main == null) throw new InvalidOperationException("Mask 缺少 Main 图片。");
            if (main.Width != mask.Width || main.Height != mask.Height)
                throw new InvalidOperationException("Mask 尺寸必须与 Main 相同。");
            if (!mask.HasAlpha) throw new InvalidOperationException("Mask 图片必须包含 Alpha 通道。");
        }

        internal static string ComposePrompt(string task, IReadOnlyList<ImageInputData> inputs)
        {
            if (inputs.Count == 0) return task;
            var text = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(task)) text.Append("整体任务：\n").Append(task).Append("\n\n");
            text.Append("输入图片说明：\n");
            int imageIndex = 0;
            foreach (var input in inputs)
            {
                if (input.Role == ImageInputRole.Mask)
                    text.Append("Mask：作用于 Main 的编辑蒙版。");
                else
                {
                    imageIndex++;
                    text.Append("图").Append(imageIndex).Append("（").Append(input.Role).Append("）：")
                        .Append(input.Role == ImageInputRole.Main
                            ? "以此图为编辑主图；按提示词要求修改，保留未要求修改的内容。"
                            : "参考图，按下述说明使用。");
                }
                if (!string.IsNullOrWhiteSpace(input.Prompt)) text.Append(' ').Append(input.Prompt);
                text.AppendLine();
            }
            return text.ToString();
        }
    }
}
