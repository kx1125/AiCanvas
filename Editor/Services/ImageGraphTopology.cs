using System;
using System.Collections.Generic;
using System.Linq;
using Unity.GraphToolkit.Editor;

namespace Tsukuyomi.AiCanvas.Editor
{
    // Connectivity only. Discovering an execution scope must not validate an unrelated operation.
    internal sealed class ImageGraphTopology
    {
        internal readonly IReadOnlyList<INode> Nodes;
        private readonly Dictionary<string, INode> owners = new();

        internal ImageGraphTopology(AiCanvasGraph graph)
        {
            Nodes = graph.GetNodes().ToList();
            foreach (var node in Nodes)
            foreach (var port in node.GetInputPorts().Concat(node.GetOutputPorts())) owners[port.GetStableId().ToString()] = node;
        }

        internal INode Owner(IPort port)
        {
            if (port == null) return null;
            if (!owners.TryGetValue(port.GetStableId().ToString(), out var node))
                throw new InvalidOperationException("Unsupported connection or subgraph.");
            return node;
        }

        internal List<IPort> Upstreams(INode node, string portName)
        {
            var ports = new List<IPort>();
            node.GetInputPortByName(portName)?.GetConnectedPorts(ports);
            foreach (var port in ports) Owner(port);
            return ports;
        }

        internal IPort Upstream(INode node, string portName)
        {
            var ports = Upstreams(node, portName);
            if (ports.Count > 1) throw new InvalidOperationException($"{node.Title}.{portName} accepts only one connection.");
            return ports.Count == 0 ? null : ports[0];
        }

        internal IEnumerable<INode> LocalConsumers(INode target)
        {
            var seen = new HashSet<string> { target.GetStableId().ToString() };
            var pending = new Queue<INode>();
            pending.Enqueue(target);
            while (pending.Count > 0)
            {
                var node = pending.Dequeue();
                foreach (var port in node.GetOutputPorts())
                {
                    var destinations = new List<IPort>();
                    port.GetConnectedPorts(destinations);
                    foreach (var destination in destinations)
                    {
                        var consumer = Owner(destination);
                        // Image Input / AiGenerate form another generation boundary.
                        if (consumer is not ComputeImageNode && consumer is not ImageOutputNode) continue;
                        if (!seen.Add(consumer.GetStableId().ToString())) continue;
                        yield return consumer;
                        if (consumer is ComputeImageNode) pending.Enqueue(consumer);
                    }
                }
            }
        }
    }
}
