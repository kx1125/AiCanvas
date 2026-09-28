using System;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Tsukuyomi.ImageCanvas.Editor",
        sourceAssembly: "Tsukuyomi.ImageCanvas.Editor", sourceClassName: "ImageCanvasGraph")]
    [Serializable]
    [Graph(Extension)]
    public sealed class AiCanvasGraph : Graph
    {
        public const string Extension = "aicanvas";

        // Missing settings on older graphs deserialize as Linear, preserving Resize/Blend.
        [SerializeField] private AiCanvasGraphSettings settings;
        public AiCanvasGraphSettings Settings => settings;

        public void SetSettings(AiCanvasGraphSettings value)
        {
            value.Validate();
            if (settings.Equals(value)) return;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            UndoBeginRecordGraph("Change Graph Settings");
            try { settings = value; }
            finally
            {
                UndoEndRecordGraph();
                Undo.CollapseUndoOperations(undoGroup);
                Undo.IncrementCurrentGroup();
            }
            EditorEvent.Invoke(Changed, this);
        }

        internal static event Action<AiCanvasGraph> Changed;

        public override void OnGraphChanged(GraphLogger logger)
        {
            // Authoring, importing and saving must never dispatch a generation request.
            base.OnGraphChanged(logger);
            EditorEvent.Invoke(Changed, this);
        }
    }

    // A serializable option + public PropertyDrawer provides image UI without
    // reflecting into Graph Toolkit's internal NodeView implementation.
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Tsukuyomi.ImageCanvas.Editor",
        sourceAssembly: "Tsukuyomi.ImageCanvas.Editor", sourceClassName: "ImageCard")]
    [Serializable]
    public struct ImageCard
    {
        public Texture2D texture;
        public string caption;
        public string nodeId;
        public string[] outputNames;
        public int outputIndex;

        public ImageCard(Texture2D texture, string caption, string nodeId = null)
        {
            this.texture = texture;
            this.caption = caption;
            this.nodeId = nodeId;
            outputNames = null;
            outputIndex = 0;
        }
    }

    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Tsukuyomi.ImageCanvas.Editor",
        sourceAssembly: "Tsukuyomi.ImageCanvas.Editor", sourceClassName: "PromptNode")]
    [Serializable]
    [Node("Inputs", "", "Prompt")]
    public sealed class PromptNode : Node
    {
        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<string>("Prompt").WithDefaultValue("")
                .Delayed().AsTextArea(3, 8).Build();
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddOutputPort<string>("Text").Build();
        }

        public override void OnEnable() { DefaultColor = new Color(0.47f, 0.63f, 0.95f); }
    }

    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Tsukuyomi.ImageCanvas.Editor",
        sourceAssembly: "Tsukuyomi.ImageCanvas.Editor", sourceClassName: "ImageNode")]
    [Serializable]
    [Node("Inputs", "", "Image")]
    public sealed class ImageNode : Node
    {
        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<ImageCard>("Image").WithDefaultValue(new ImageCard(null, "Drop a Texture2D here")).Build();
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddOutputPort<Texture2D>("Image").Build();
        }

        public override void OnEnable()
        {
            DefaultColor = new Color(0.35f, 0.76f, 0.62f);
            if (Title == "Reference Image") Title = "Image";
        }
    }

    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Tsukuyomi.ImageCanvas.Editor",
        sourceAssembly: "Tsukuyomi.ImageCanvas.Editor", sourceClassName: "ImageInputNode")]
    [Serializable]
    [Node("AI", "", "Image Input")]
    public sealed class ImageInputNode : Node
    {
        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            context.AddOption<ImageRoleOption>("Role").WithDefaultValue(new ImageRoleOption(ImageInputRole.Reference))
                .WithTooltip("Main: 编辑主图；Reference: 参考图；Mask: 主图的编辑蒙版。").Build();
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<Texture2D>("Image").Build();
            context.AddInputPort<string>("Prompt").WithDefaultValue("").Delayed().AsTextArea(3, 6)
                .WithTooltip("说明这张图片的用途；连接 Prompt 节点或直接填写。").Build();
            context.AddOutputPort<ImageInputData>("Input").Build();
        }

        public override void OnEnable()
        {
            DefaultColor = new Color(0.36f, 0.70f, 0.80f);
            Subtitle = "Image + Prompt + Role";
        }
    }

    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Tsukuyomi.ImageCanvas.Editor",
        sourceAssembly: "Tsukuyomi.ImageCanvas.Editor", sourceClassName: "GenerateImageNode")]
    [Serializable]
    [Node("AI", "", "AiGenerate")]
    public sealed class GenerateImageNode : Node
    {
        public GenerationSettings Settings => GraphOptionAccess.Read<GenerationSettings>(this, "Generation");
        // The first slot retains its original serialized names for existing graphs.
        public static string OutputName(int index) => index == 0 ? "Image" : "Image " + (index + 1);
        public static string ResultName(int index) => index == 0 ? "Result" : "Result " + (index + 1);

        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            var settings = context.AddOption<GenerationSettings>("Generation")
                .WithDefaultValue(GenerationSettings.Default).Build();
            settings.TryGetValue<GenerationSettings>(out var value);
            // Include the node ID in the option key. Duplicated nodes then receive
            // their own control value rather than inheriting the source's ID.
            context.AddOption<GenerateControls>("Run " + this.GetStableId()).WithDisplayName("Run")
                .WithDefaultValue(new GenerateControls { nodeId = this.GetStableId().ToString() }).Build();
            for (int index = 0; index < value.OutputCount; index++)
                context.AddOption<ImageCard>(ResultName(index)).WithDisplayName("Result " + (index + 1))
                    .WithDefaultValue(new ImageCard(null, "Click Run to generate this node")).Build();
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<string>("Prompt").Delayed()
                .WithTooltip("整体生成要求；连接到 Inputs 的 Image Input 已填写 Prompt 时可留空。").Build();
            // Keep the serialized name so legacy wires can be migrated on opening.
            context.AddInputPort<ImageInputData>("Reference").WithDisplayName("Inputs")
                .WithTooltip("连接 Image Input 节点：Main、Reference 或 Mask。").BuildWithMultiCapacity();
            for (int index = 0; index < Settings.OutputCount; index++)
                context.AddOutputPort<Texture2D>(OutputName(index)).WithDisplayName("Image " + (index + 1)).Build();
        }

        public override void OnEnable()
        {
            DefaultColor = new Color(0.93f, 0.60f, 0.37f);
            if (Title == "Generate / Edit" || Title == "Generate") Title = "AiGenerate";
            Subtitle = "GPT Image 2";
        }
    }

    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Tsukuyomi.ImageCanvas.Editor",
        sourceAssembly: "Tsukuyomi.ImageCanvas.Editor", sourceClassName: "GenerateControls")]
    [Serializable]
    public struct GenerateControls
    {
        public string nodeId;
    }

    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Tsukuyomi.ImageCanvas.Editor",
        sourceAssembly: "Tsukuyomi.ImageCanvas.Editor", sourceClassName: "ImageOutputNode")]
    [Serializable]
    [Node("Outputs", "", "Image Output")]
    public sealed class ImageOutputNode : Node
    {
        protected override void OnDefineOptions(IOptionDefinitionContext context)
        {
            ImagePreviewOptions.Define(context, this.GetStableId().ToString());
        }

        protected override void OnDefinePorts(IPortDefinitionContext context)
        {
            context.AddInputPort<Texture2D>("Image").Build();
        }

        public override void OnEnable() { DefaultColor = new Color(0.70f, 0.50f, 0.85f); }
    }
}
