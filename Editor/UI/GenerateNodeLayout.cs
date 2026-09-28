using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tsukuyomi.AiCanvas.Editor
{
    // Graph Toolkit 6000.6 groups options and ports separately. Keep the native
    // port hierarchy intact so its update, picking and wire lifecycle still work.
    // Reserve rows in the option layout, then position the original ports in them.
    // Only this adapter depends on the Toolkit's USS classes and element names.
    internal static class GenerateNodeLayout
    {
        private static readonly ConditionalWeakTable<VisualElement, LayoutBinding> Bindings = new();

        private sealed class LayoutBinding
        {
            private readonly VisualElement node;
            private VisualElement inputs;
            private bool pending;

            internal LayoutBinding(VisualElement node)
            {
                this.node = node;
                node.RegisterCallback<GeometryChangedEvent>(OnGeometry);
            }

            internal void ObserveInputs(VisualElement current)
            {
                if (ReferenceEquals(inputs, current)) return;
                inputs?.UnregisterCallback<GeometryChangedEvent>(OnGeometry);
                inputs = current;
                inputs?.RegisterCallback<GeometryChangedEvent>(OnGeometry);
            }

            private void OnGeometry(GeometryChangedEvent evt)
            {
                if (pending) return;
                pending = true;
                node.schedule.Execute(() =>
                {
                    pending = false;
                    var current = node.Q("ai-canvas-generation-settings");
                    if (current != null && node.panel != null) Arrange(current);
                });
            }
        }

        internal static void Attach(VisualElement settings)
        {
            bool pending = false;
            void Schedule()
            {
                if (pending) return;
                pending = true;
                settings.schedule.Execute(() =>
                {
                    pending = false;
                    if (settings.panel != null) Arrange(settings);
                });
            }
            settings.RegisterCallback<AttachToPanelEvent>(_ => Schedule());
            settings.RegisterCallback<GeometryChangedEvent>(_ => Schedule());
        }

        internal static void Arrange(VisualElement settings)
        {
            var options = Ancestor(settings, "node-options");
            var node = options?.parent;
            if (node == null || !node.ClassListContains("ge-node")) return;
            var ports = node.Children().FirstOrDefault(child => child.name == "port-container");
            var inputs = ports?.Q("inputs");
            // Absolute input height can change after the IMGUI prompt adapter attaches
            // without changing the node's bounds. Observe it directly as well.
            Bindings.GetValue(node, element => new LayoutBinding(element)).ObserveInputs(inputs);
            var outputs = ports?.Q("outputs");
            var run = options.Q("ai-canvas-generate-controls");
            var settingsField = OptionField(settings, options);
            var runField = OptionField(run, options);
            var cards = options.Query<VisualElement>("ai-canvas-card").ToList();
            var outputPorts = outputs?.Children().Where(child => child.ClassListContains("ge-port--direction-output")).ToList();
            // Fail closed if a Toolkit version changes the expected hierarchy.
            if (inputs == null || outputs == null || settingsField == null || runField == null ||
                outputPorts == null || cards.Count != outputPorts.Count) return;
            var fields = cards.Select(card => OptionField(card, options)).ToList();
            if (fields.Any(field => field == null)) return;

            if (node.ClassListContains("ge-node--collapsed"))
            {
                Reset(ports, inputs, outputs, outputPorts, runField, fields);
                return;
            }

            ports.AddToClassList("ai-canvas-arranged-ports");
            // The native port area's USS background is opaque. After stretching
            // it over the node, it would paint over the settings, Run and cards.
            // Pointer transparency alone does not prevent this visual occlusion.
            foreach (var overlay in new[] { ports, inputs, inputs.contentContainer, outputs, outputs.contentContainer })
            {
                overlay.style.backgroundColor = Color.clear;
                overlay.style.backgroundImage = StyleKeyword.None;
            }
            ports.pickingMode = PickingMode.Ignore;
            ports.style.position = Position.Absolute;
            ports.style.left = 0; ports.style.right = 0;
            ports.style.top = 0; ports.style.bottom = 0;
            ports.style.borderTopWidth = 0;
            inputs.pickingMode = PickingMode.Ignore;
            inputs.contentContainer.pickingMode = PickingMode.Ignore;
            inputs.style.position = Position.Absolute;
            inputs.style.left = 0; inputs.style.right = 0;
            inputs.style.bottom = StyleKeyword.Auto;
            inputs.style.flexGrow = 0;
            outputs.pickingMode = PickingMode.Ignore;
            // PortHierarchyContainer owns an extra content container. Its empty
            // area must also let pointer events through to settings and buttons.
            outputs.contentContainer.pickingMode = PickingMode.Ignore;
            outputs.style.position = Position.Absolute;
            outputs.style.left = 0; outputs.style.right = 0;
            outputs.style.top = 0; outputs.style.bottom = 0;

            // LocalToWorld + WorldToLocal cancels canvas pan/zoom and node position.
            // Position inside each field's padding so the connector is a heading
            // above the image picker and preview, never over the image itself.
            float inputTop = ports.WorldToLocal(settingsField.LocalToWorld(new Vector2(0, settingsField.layout.height))).y + 4;
            SetTop(inputs, inputTop);
            float inputHeight = inputs.layout.height;
            if (!float.IsNaN(inputHeight) && inputHeight > 0)
                SetMarginTop(runField, inputHeight + 8);
            for (int index = 0; index < fields.Count; index++)
            {
                var field = fields[index];
                if (field.style.paddingTop.value.value != 30) field.style.paddingTop = 30;
                var port = outputPorts[index];
                port.style.position = Position.Absolute;
                port.style.left = 0; port.style.right = 0;
                float top = port.parent.WorldToLocal(field.LocalToWorld(Vector2.zero)).y + 2;
                SetTop(port, top);
            }
        }

        private static VisualElement Ancestor(VisualElement element, string name)
        {
            for (var current = element; current != null; current = current.parent)
                if (current.name == name) return current;
            return null;
        }

        private static VisualElement OptionField(VisualElement element, VisualElement options)
        {
            for (var current = element; current != null; current = current.parent)
                if (current.parent == options) return current;
            return null;
        }

        private static void SetTop(VisualElement element, float top)
        {
            if (float.IsNaN(top) || float.IsInfinity(top)) return;
            if (element.style.top.keyword != StyleKeyword.Undefined || Mathf.Abs(element.style.top.value.value - top) > 0.1f)
                element.style.top = top;
        }

        private static void SetMarginTop(VisualElement element, float top)
        {
            if (element.style.marginTop.keyword != StyleKeyword.Undefined || Mathf.Abs(element.style.marginTop.value.value - top) > 0.1f)
                element.style.marginTop = top;
        }

        private static void Reset(VisualElement ports, VisualElement inputs, VisualElement outputs,
            List<VisualElement> outputPorts, VisualElement runField, List<VisualElement> fields)
        {
            if (!ports.ClassListContains("ai-canvas-arranged-ports")) return;
            ports.RemoveFromClassList("ai-canvas-arranged-ports");
            foreach (var overlay in new[] { ports, inputs, inputs.contentContainer, outputs, outputs.contentContainer })
            {
                overlay.style.backgroundColor = StyleKeyword.Null;
                overlay.style.backgroundImage = StyleKeyword.Null;
            }
            foreach (var element in new[] { ports, inputs, outputs }.Concat(outputPorts))
            {
                element.style.position = StyleKeyword.Null;
                element.style.top = StyleKeyword.Null; element.style.bottom = StyleKeyword.Null;
                element.style.left = StyleKeyword.Null; element.style.right = StyleKeyword.Null;
            }
            ports.style.borderTopWidth = StyleKeyword.Null;
            inputs.style.flexGrow = StyleKeyword.Null;
            runField.style.marginTop = StyleKeyword.Null;
            foreach (var field in fields) field.style.paddingTop = StyleKeyword.Null;
        }
    }
}
