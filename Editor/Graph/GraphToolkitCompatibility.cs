using System;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;
#if !UNITY_6000_6_OR_NEWER
using System.Reflection;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
#endif

namespace Tsukuyomi.AiCanvas.Editor
{
    // Unity 6.5 predates these public Graph Toolkit APIs. Keep its internal
    // member access here; 6.6 and newer use the supported APIs directly.
    internal static class GraphToolkitCompatibility
    {
        internal static AiCanvasGraph GetGraph(EditorWindow window)
        {
            if (window == null) return null;
#if UNITY_6000_6_OR_NEWER
            return (window as IGraphWindow)?.Graph as AiCanvasGraph;
#else
            if (!LegacyWindowType.IsInstanceOfType(window)) return null;
            var tool = WindowTool.GetValue(window);
            var state = tool == null ? null : ToolState.GetValue(tool);
            var model = state == null ? null : StateGraph.GetValue(state);
            return model != null && LegacyGraphType.IsInstanceOfType(model)
                ? ModelGraph.GetValue(model) as AiCanvasGraph : null;
#endif
        }

        internal static Hash128 GetStableId(this INode node)
        {
#if UNITY_6000_6_OR_NEWER
            return node.ID;
#else
            var model = NodeModel.GetValue(node);
            return model == null ? default : (Hash128)ModelGuid.GetValue(model);
#endif
        }

        internal static Hash128 GetStableId(this IPort port)
        {
#if UNITY_6000_6_OR_NEWER
            return port.ID;
#else
            return (Hash128)ModelGuid.GetValue(port);
#endif
        }

        internal static IPort BuildWithMultiCapacity<T>(this IPortBuilder<T> builder)
            where T : IPortBuilder<T>
        {
#if UNITY_6000_6_OR_NEWER
            return builder.WithCapacity(PortCapacity.Multi).Build();
#else
            var port = builder.Build();
            PortCapacityProperty.SetValue(port, MultiCapacity);
            return port;
#endif
        }

#if !UNITY_6000_6_OR_NEWER
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static Type LegacyType(string name) => typeof(Graph).Assembly.GetType("Unity.GraphToolkit.Editor." + name, true);
        private static PropertyInfo Property(Type type, string name) => type.GetProperty(name, Members)
            ?? throw new MissingMemberException(type.FullName, name);

        private static readonly Type LegacyWindowType = LegacyType("GraphViewEditorWindow");
        private static readonly Type LegacyGraphType = LegacyType("Implementation.GraphModelImp");
        private static readonly PropertyInfo WindowTool = Property(LegacyWindowType, "GraphTool");
        private static readonly PropertyInfo ToolState = Property(LegacyType("GraphTool"), "ToolState");
        private static readonly PropertyInfo StateGraph = Property(LegacyType("ToolStateComponent"), "GraphModel");
        private static readonly PropertyInfo ModelGraph = Property(LegacyGraphType, "Graph");
        private static readonly PropertyInfo NodeModel = Property(typeof(INode), "NodeModel");
        private static readonly PropertyInfo ModelGuid = Property(LegacyType("Model"), "Guid");
        private static readonly PropertyInfo OptionPort = Property(LegacyType("NodeOption"), "PortModel");
        private static readonly PropertyInfo EmbeddedValue = Property(LegacyType("PortModel"), "EmbeddedValue");
        private static readonly PropertyInfo ConstantSetter = Property(LegacyType("Constant"), "SetterMethod");
        private static readonly PropertyInfo PortCapacityProperty = Property(LegacyType("PortModel"), "Capacity");
        private static readonly object MultiCapacity = Enum.Parse(PortCapacityProperty.PropertyType, "Multi");

        internal static bool TrySetValue<T>(this INodeOption option, T value)
        {
            if (option == null || OptionPort.GetValue(option) is not IPort port || !port.TrySetValue(value)) return false;
            // Option setters can redefine dynamic ports (for example Output Count).
            var constant = EmbeddedValue.GetValue(port);
            (ConstantSetter.GetValue(constant) as Action<object>)?.Invoke(value);
            return true;
        }

        private const string ToolbarName = "ai-canvas-compatibility-toolbar";

        internal static void AttachToolbar(EditorWindow window)
        {
            var root = window.rootVisualElement;
            if (root.Q(ToolbarName) != null) return;
            var toolbar = new Toolbar { name = ToolbarName };
            toolbar.style.flexShrink = 0;
            toolbar.Add(new GraphSettingsToolbar { containerWindow = window });
            toolbar.Add(new PreviewImageToolbar { containerWindow = window });
            toolbar.Add(new RunImageToolbar { containerWindow = window });
            toolbar.Add(new CancelImageToolbar());
            toolbar.Add(new ImageStatusToolbar { containerWindow = window });
            toolbar.Add(new OpenAISettingsToolbar());
            root.Insert(0, toolbar);
        }

        internal static void RemoveToolbar(EditorWindow window)
        {
            if (window != null) window.rootVisualElement.Q(ToolbarName)?.RemoveFromHierarchy();
        }
#else
        internal static void AttachToolbar(EditorWindow window) { }
        internal static void RemoveToolbar(EditorWindow window) { }
#endif
    }
}
