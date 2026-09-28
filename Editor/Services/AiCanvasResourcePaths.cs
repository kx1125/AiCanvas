using System.IO;
using UnityEditor;

namespace Tsukuyomi.AiCanvas.Editor
{
    internal static class AiCanvasResourcePaths
    {
        internal const string Root = "Assets/AiCanvasResource";
        internal const string Graphs = Root + "/Graphs";
        internal const string Generated = Root + "/Generated";
        internal const string Captures = Root + "/Captures";

        internal static void EnsureFolder(string folder)
        {
            folder = ImageAssetStore.ValidateFolder(folder);
            if (AssetDatabase.IsValidFolder(folder)) return;

            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            string guid = AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
            if (string.IsNullOrEmpty(guid))
                throw new IOException("Could not create AiCanvas resource folder: " + folder);
        }
    }
}
