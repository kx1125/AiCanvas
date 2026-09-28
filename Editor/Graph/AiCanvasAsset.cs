using UnityEditor;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    public sealed class AiCanvasAsset : ScriptableObject
    {
        public string GraphPath => AssetDatabase.GetAssetPath(this);
    }

}
