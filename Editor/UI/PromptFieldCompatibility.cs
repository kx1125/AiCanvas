using System.Collections.Generic;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tsukuyomi.AiCanvas.Editor
{
    // Unity 6000.6.0b3 rewrites ConstantField's TextField during graph refresh,
    // including while IME owns its composition/caret (see UUM-130669).
    // Keep the original field as the graph's command/Undo adapter and edit an
    // IMGUI sibling. UITK's unbound TextField also misplaced the native IME caret
    // on this editor version. No internal APIs or manual caret correction.
    internal static class PromptFieldCompatibility
    {
        private static readonly Dictionary<TextField, PromptDraftField> Fields = new();

        internal static void RefreshDrafts()
        {
            var detached = new List<TextField>();
            foreach (var pair in Fields)
            {
                if (pair.Key.panel == null || pair.Value.panel == null)
                    detached.Add(pair.Key);
                else
                    pair.Value.RefreshFromSource();
            }
            foreach (var field in detached) Fields.Remove(field);

        }

        internal static void Attach(EditorWindow window, AiCanvasGraph graph)
        {
            foreach (var source in window.rootVisualElement.Query<TextField>().ToList())
            {
                if (!IsPromptField(source))
                    continue;
                if (source.parent == null || source.panel == null) continue;
                if (Fields.TryGetValue(source, out var registered))
                {
                    registered.Graph = graph;
                    continue;
                }

                // Graph Toolkit can detach/reuse its visual tree when switching
                // graphs. Reuse the adapter instead of adding another textbox.
                PromptDraftField draft = null;
                foreach (var child in source.parent.Children())
                    if (child is PromptDraftField existing && ReferenceEquals(existing.Source, source))
                        draft = existing;
                if (draft != null)
                {
                    draft.Graph = graph;
                    Fields.Add(source, draft);
                    continue;
                }

                draft = new PromptDraftField(source, graph);
                source.parent.Insert(source.parent.IndexOf(source) + 1, draft);
                source.style.display = DisplayStyle.None;
                Fields.Add(source, draft);
            }
        }

        private static bool IsPromptField(TextField source)
        {
            if (source.label == "Prompt") return true;
            // Embedded port values have no TextField label. Match the public
            // visual hierarchy's port label, without depending on internal types.
            var container = source.parent?.parent;
            return container?.name == "constant-editor" && container.parent?.Q<Label>("label")?.text == "Prompt";
        }

        internal static bool CommitPendingEdits(AiCanvasGraph graph)
        {
            // A commit may rebuild a field; iterate a snapshot of the adapters.
            foreach (var draft in new List<PromptDraftField>(Fields.Values))
                if (ReferenceEquals(draft.Graph, graph) && draft.panel != null && !draft.Commit())
                    return false;
            return true;
        }
    }

    internal sealed class PromptDraftField : VisualElement
    {
        private readonly TextField source;
        private readonly IMGUIContainer input;
        private readonly string controlName = "AiCanvasPrompt_" + System.Guid.NewGuid().ToString("N");
        private GUIStyle textStyle;
        private string draft;
        private bool editing;
        private bool dirty;

        internal AiCanvasGraph Graph { get; set; }
        internal TextField Source => source;

        internal PromptDraftField(TextField source, AiCanvasGraph graph)
        {
            this.source = source;
            Graph = graph;
            name = "ai-canvas-prompt-editor";
            draft = source.value ?? string.Empty;
            tooltip = "输入完成后点击画布空白处提交；Ctrl+S、节点 Run 和 Run Graph 也会提交已确认的文字。";
            style.minWidth = 260;
            style.maxWidth = 380;
            if (!string.IsNullOrEmpty(source.label)) Add(new Label(source.label));
            input = new IMGUIContainer(DrawInput) { name = "ai-canvas-prompt-imgui", focusable = true };
            input.style.minHeight = 80;
            Add(input);
            SetEnabled(source.enabledSelf);

            RegisterCallback<FocusInEvent>(_ => editing = true);
            RegisterCallback<FocusOutEvent>(_ =>
            {
                editing = false;
                Commit();
                // Let the native text editor finish its focus/IME event first.
                schedule.Execute(() =>
                {
                    var focused = panel?.focusController?.focusedElement as VisualElement;
                    editing = focused == this || (focused != null && Contains(focused));
                    if (!editing) Commit();
                });
            });
        }

        private void DrawInput()
        {
            var evt = Event.current;
            bool save = evt.type == EventType.KeyDown && (evt.control || evt.command) && evt.keyCode == KeyCode.S;
            if (textStyle == null) textStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
            GUI.SetNextControlName(controlName);
            string newValue = EditorGUILayout.TextArea(draft, textStyle, GUILayout.MinHeight(76), GUILayout.ExpandWidth(true));
            if (newValue != draft)
            {
                draft = newValue;
                dirty = true;
            }

            bool focused = GUI.GetNameOfFocusedControl() == controlName;
            if (focused) editing = true;
            else if (editing && evt.type == EventType.Repaint)
            {
                editing = false;
                Commit();
            }
            if (save && !Commit()) evt.Use();
        }

        internal void RefreshFromSource()
        {
            SetEnabled(source.enabledSelf);
            if (editing) return;
            if (dirty)
            {
                // Focus loss can happen before the final native IME event.
                Commit();
                return;
            }
            var modelValue = source.value ?? string.Empty;
            if (draft != modelValue)
            {
                draft = modelValue;
                input.MarkDirtyRepaint();
            }
        }

        internal bool Commit()
        {
            if (!dirty) return true;
            if (!string.IsNullOrEmpty(Input.compositionString)) return false;
            if (source.panel == null) return false;

            // Leave IMGUI's native TextEditor selection and composition untouched.
            dirty = false;
            source.value = draft;
            return true;
        }
    }
}
