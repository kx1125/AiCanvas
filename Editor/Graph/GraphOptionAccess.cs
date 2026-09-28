using System;
using Unity.GraphToolkit.Editor;

namespace Tsukuyomi.AiCanvas.Editor
{
    internal static class GraphOptionAccess
    {
        internal static T Read<T>(INode node, string name)
        {
            var option = node.GetNodeOptionByName(name);
            if (option != null && option.TryGetValue<T>(out var value)) return value;
            throw new InvalidOperationException($"Cannot read {node.Title}.{name} as {typeof(T).Name}.");
        }

        internal static void Set<T>(INode node, string name, T value)
        {
            var option = node.GetNodeOptionByName(name);
            if (option == null || !option.TrySetValue(value))
                throw new InvalidOperationException($"Cannot write {node.Title}.{name}.");
        }

    }
}
