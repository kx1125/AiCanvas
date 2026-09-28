using System;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    internal static class EditorEvent
    {
        internal static void Invoke(Action handlers)
        {
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
                try { handler(); } catch (Exception exception) { Debug.LogException(exception); }
        }

        internal static void Invoke<T>(Action<T> handlers, T value)
        {
            if (handlers == null) return;
            foreach (Action<T> handler in handlers.GetInvocationList())
                try { handler(value); } catch (Exception exception) { Debug.LogException(exception); }
        }
    }
}
