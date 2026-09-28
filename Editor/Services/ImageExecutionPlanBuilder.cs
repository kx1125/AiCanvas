using System;
using System.Collections.Generic;
using System.Linq;
using Unity.GraphToolkit.Editor;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    internal static class ImageExecutionPlanBuilder
    {
        internal static ExecutionPlan Build(AiCanvasGraph graph, string targetNodeId = null,
            ImageExecutionMode mode = ImageExecutionMode.Generate)
        {
            using var assetQueries = new ImageAssetQueryScope();
            var context = new ImageProcessingContext(graph.Settings.workingSpace);
            var topology = new ImageGraphTopology(graph);
            var nodes = topology.Nodes;
            var plan = new ExecutionPlan();
            var visited = new Dictionary<string, ImageSource>();
            var visiting = new HashSet<string>();
            var failures = new Dictionary<string, string>();

            var completed = new HashSet<string>();
            List<IPort> Upstreams(INode node, string portName) => topology.Upstreams(node, portName);
            IPort Upstream(INode node, string portName) => topology.Upstream(node, portName);
            INode Owner(IPort port) => topology.Owner(port);

            ImageSource VisitPort(IPort port)
            {
                var node = Owner(port);
                int index = 0;
                if (node is GenerateImageNode generate)
                {
                    index = Enumerable.Range(0, generate.Settings.OutputCount)
                        .Where(i => GenerateImageNode.OutputName(i) == port.Name).DefaultIfEmpty(-1).First();
                    if (index < 0) throw new InvalidOperationException("The connected AiGenerate output no longer exists.");
                }
                else if (node is ComputeImageNode compute)
                {
                    index = Enumerable.Range(0, compute.OutputCount)
                        .Where(i => compute.OutputName(i) == port.Name).DefaultIfEmpty(-1).First();
                    if (index < 0) throw new InvalidOperationException("The connected image output no longer exists.");
                }
                return Visit(node, index);
            }

            string ReadPrompt(INode node)
            {
                var promptNode = Owner(Upstream(node, "Prompt"));
                if (promptNode is PromptNode) return GraphOptionAccess.Read<string>(promptNode, "Prompt");
                if (promptNode != null) throw new InvalidOperationException("Connect a Prompt node to the Prompt input.");
                return node.GetInputPortByName("Prompt").TryGetValue<string>(out var value) ? value : "";
            }

            PlanInput ReadInput(INode node)
            {
                if (node is not ImageInputNode)
                    throw new InvalidOperationException("AiGenerate 的 Inputs 需要连接 Image Input 节点。");
                var role = GraphOptionAccess.Read<ImageRoleOption>(node, "Role").role;
                var imageNode = Upstream(node, "Image");
                ImageSource source;
                if (imageNode != null) source = VisitPort(imageNode);
                else
                {
                    node.GetInputPortByName("Image").TryGetValue<Texture2D>(out var texture);
                    if (texture == null) throw new InvalidOperationException("Image Input has no texture. 请连接或指定 Image 图片。");
                    source = new ImageSource { Reference = texture };
                }
                return new PlanInput { Source = source, Role = role, Prompt = ReadPrompt(node), NodeId = node.GetStableId().ToString() };
            }

            ImageSource Visit(INode node, int outputIndex = 0)
            {
                if (node == null) return new ImageSource();
                var id = node.GetStableId().ToString();
                var key = ExecutionPlan.ResultKey(id, outputIndex);
                if (visited.TryGetValue(key, out var cached)) return cached;
                if (failures.TryGetValue(key, out var error)) throw new InvalidOperationException(error);
                if (completed.Contains(id)) return new ImageSource { ReferenceId = key };
                if (!visiting.Add(id)) throw new InvalidOperationException("Cycle detected in image dependencies.");
                try
                {
                    var value = Evaluate(node, outputIndex, id, key);
                    visited.Add(key, value);
                    return value;
                }
                catch (Exception exception) { failures[key] = exception.Message; throw; }
                finally { visiting.Remove(id); }
            }

            ImageSource Evaluate(INode node, int outputIndex, string id, string key)
            {
                ImageSource source;
                if (node is ImageNode)
                {
                    source = new ImageSource { Reference = GraphOptionAccess.Read<ImageCard>(node, "Image").texture };
                    if (source.Reference == null)
                        throw new InvalidOperationException("A connected Image node has no texture. Assign an image or disconnect it.");
                }
                else if (node is CameraColorNode cameraColor)
                {
                    source = new ImageSource { Reference = CameraColorCapture.ReadOutput(cameraColor) };
                }
                else if (node is ComputeImageNode compute)
                {
                    var operation = compute.CreateOperation(context, outputIndex) ?? throw new InvalidOperationException($"{node.Title} has no compute operation.");
                    var step = new PlanStep { Id = id, OutputIndex = outputIndex, Kind = ImageStepKind.Compute, Operation = operation };
                    foreach (var binding in operation.Inputs)
                    {
                        var input = node.GetInputPortByName(binding.PortName);
                        if (input == null) throw new InvalidOperationException($"{node.Title} has no input port {binding.PortName}.");
                        var connected = Upstream(node, binding.PortName);
                        if (connected != null) step.Images.Add(VisitPort(connected));
                        else
                        {
                            input.TryGetValue<Texture2D>(out var texture);
                            if (texture == null && binding.Optional) texture = Texture2D.whiteTexture;
                            if (texture == null) throw new InvalidOperationException($"Connect {node.Title}.{binding.PortName} before processing.");
                            step.Images.Add(new ImageSource { Reference = texture });
                        }
                    }
                    plan.Steps.Add(step);
                    source = new ImageSource { ReferenceId = key };
                }
                else if (node is GenerateImageNode)
                {
                    if (mode == ImageExecutionMode.LocalPreview || (targetNodeId != null && id != targetNodeId))
                    {
                        var published = GraphOptionAccess.Read<ImageCard>(node, GenerateImageNode.ResultName(outputIndex)).texture;
                        if (published == null)
                            throw new InvalidOperationException($"请先运行上游节点 {node.Title} ({id.Substring(0, 8)})，它还没有可用的结果图片。");
                        source = new ImageSource { Reference = published };
                        return source;
                    }
                    var settings = ((GenerateImageNode)node).Settings;
                    settings.Validate();
                    string prompt = ReadPrompt(node);
                    var inputs = Upstreams(node, "Reference").Select(port => ReadInput(Owner(port))).OrderBy(input => input.Role).ToList();
                    ImageInputRules.ValidateRoles(inputs.Select(input => input.Role));
                    ImageInputRules.ValidatePrompts(prompt, inputs.Select(input => input.Prompt));
                    var main = inputs.FirstOrDefault(input => input.Role == ImageInputRole.Main)?.Source;
                    var mask = inputs.FirstOrDefault(input => input.Role == ImageInputRole.Mask)?.Source;
                    if (mask != null && main?.Reference != null && mask.Reference != null)
                        ImageInputRules.ValidateMask(main.Reference, mask.Reference);
                    plan.Steps.Add(new PlanStep
                    {
                        Id = id, Kind = ImageStepKind.Generate, Prompt = prompt, Settings = settings,
                        Inputs = inputs
                    });
                    completed.Add(id);
                    source = new ImageSource { ReferenceId = key };
                }
                else throw new InvalidOperationException($"Unsupported image node: {node.Title}");
                return source;
            }

            void VisitRoot(INode node)
            {
                if (node is ComputeImageNode compute)
                {
                    for (int index = 0; index < compute.OutputCount; index++) Visit(node, index);
                    return;
                }
                if (node is not ImageOutputNode) { Visit(node); return; }
                var source = Upstream(node, "Image");
                if (source == null) throw new InvalidOperationException("Connect the Image Output input before running.");
                plan.Outputs.Add(node.GetStableId().ToString(), VisitPort(source));
            }

            var outputs = nodes.OfType<ImageOutputNode>().ToList();
            if (mode == ImageExecutionMode.LocalPreview)
            {
                // Every visible local node is a preview root, including unfinished/disconnected branches.
                // Failed roots retain their own error without hiding valid roots in the same graph.
                foreach (var node in nodes.Where(node => node is ComputeImageNode || node is ImageOutputNode))
                {
                    int count = node is ComputeImageNode compute ? compute.OutputCount : 1;
                    for (int index = 0; index < count; index++)
                    {
                        try
                        {
                            if (node is ComputeImageNode) Visit(node, index);
                            else VisitRoot(node);
                        }
                        catch (Exception exception)
                        { plan.PreviewErrors[ImagePreviewStore.OutputKey(node.GetStableId().ToString(), index)] = exception.Message; }
                    }
                }
                return plan;
            }
            if (targetNodeId != null)
            {
                var target = nodes.FirstOrDefault(node => node.GetStableId().ToString() == targetNodeId &&
                    (node is GenerateImageNode || node is ComputeImageNode));
                if (target == null) throw new InvalidOperationException("The selected image processing node no longer exists.");
                VisitRoot(target);
                foreach (var consumer in topology.LocalConsumers(target)) VisitRoot(consumer);
                return plan;
            }
            foreach (var output in outputs) VisitRoot(output);
            if (outputs.Count == 0)
            {
                if (mode == ImageExecutionMode.Generate)
                    foreach (var generation in nodes.OfType<GenerateImageNode>()) Visit(generation);
                foreach (var local in nodes.OfType<ComputeImageNode>()) VisitRoot(local);
            }
            if (plan.Steps.Count == 0 && plan.Outputs.Count == 0)
                throw new InvalidOperationException("Add an AiGenerate or Image Output node.");
            return plan;
        }

    }
}
