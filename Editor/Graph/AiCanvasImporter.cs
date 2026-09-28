using UnityEditor.AssetImporters;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    // A ScriptedImporter needs its own matching MonoScript asset so .meta can
    // persist the script GUID instead of a transient instanceID: 0 reference.
    [ScriptedImporter(2, AiCanvasGraph.Extension)]
    public sealed class AiCanvasImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            var asset = ScriptableObject.CreateInstance<AiCanvasAsset>();
            asset.name = System.IO.Path.GetFileNameWithoutExtension(context.assetPath);
            // Preserve the main asset's local ID and all existing graph references.
            context.AddObjectToAsset("ImageCanvas", asset);
            context.SetMainObject(asset);
        }
    }
}
