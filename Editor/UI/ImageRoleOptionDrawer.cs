using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Tsukuyomi.AiCanvas.Editor
{
    [CustomPropertyDrawer(typeof(ImageRoleOption))]
    public sealed class ImageRoleOptionDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
            => new PropertyField(property.FindPropertyRelative("role"), "Role") { name = "ai-canvas-input-role" };
    }
}
