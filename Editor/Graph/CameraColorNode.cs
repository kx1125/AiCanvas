using System;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Tsukuyomi.ImageCanvas.Editor",
        sourceAssembly: "Tsukuyomi.ImageCanvas.Editor", sourceClassName: "CameraColorNode")]
    [Serializable]
    [Node("Unity", "", "CameraColor")]
    public sealed class CameraColorNode : Node
    {
        public CameraColorSettings Settings => GraphOptionAccess.Read<CameraColorSettings>(this, "Camera");
        public CameraColorFrame Frame => GraphOptionAccess.Read<CameraColorFrame>(this, "Frame");

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<CameraColorSettings>("Camera").WithDefaultValue(CameraColorSettings.Default).Build();
            context.AddOption<CameraColorControls>("Capture " + this.GetStableId()).WithDisplayName("Capture")
                .WithDefaultValue(new CameraColorControls { nodeId = this.GetStableId().ToString() }).Build();
            context.AddOption<CameraColorFrame>("Frame").WithDefaultValue(new CameraColorFrame
                { caption = "点击 Capture 获取相机画面。" }).Build();
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddOutputPort<Texture2D>("Image").WithTooltip("最近一次 Capture 的相机最终颜色，可连接 Image Input 或 Image Output。").Build();
        }

        public override void OnEnable()
        {
            DefaultColor = new Color(0.35f, 0.70f, 0.88f);
            Subtitle = "Camera final color";
        }
    }

    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Tsukuyomi.ImageCanvas.Editor",
        sourceAssembly: "Tsukuyomi.ImageCanvas.Editor", sourceClassName: "CameraColorControls")]
    [Serializable]
    public struct CameraColorControls { public string nodeId; }

    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Tsukuyomi.ImageCanvas.Editor",
        sourceAssembly: "Tsukuyomi.ImageCanvas.Editor", sourceClassName: "CameraColorFrame")]
    [Serializable]
    public struct CameraColorFrame
    {
        public Texture2D texture;
        public string caption;
        public string settings;
        public string contentHash;
    }
}
