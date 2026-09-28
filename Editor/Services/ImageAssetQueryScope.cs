using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    // Cache only within one synchronous build/evaluation. Never keep a scope across
    // an await or asset mutation: a subsequent scope must see fresh asset versions.
    internal sealed class ImageAssetQueryScope : IDisposable
    {
        private sealed class Queries
        {
            internal readonly Dictionary<string, Hash128> Hashes = new();
            internal readonly Dictionary<ComputeShader, string> Shaders = new();
            internal readonly Dictionary<Texture2D, string> Textures = new();
            internal int HashQueries;
        }
        [ThreadStatic] private static ImageAssetQueryScope current;
        private readonly ImageAssetQueryScope previous;
        private readonly Queries queries;
        internal int HashQueries => queries.HashQueries;

        internal ImageAssetQueryScope()
        {
            previous = current;
            queries = previous?.queries ?? new Queries();
            current = this;
        }

        public void Dispose() => current = previous;

        internal static Hash128 DependencyHash(string path)
        {
            var cache = current?.queries;
            if (cache != null && cache.Hashes.TryGetValue(path, out var cached)) return cached;
            var hash = AssetDatabase.GetAssetDependencyHash(path);
            if (cache != null) { cache.Hashes.Add(path, hash); cache.HashQueries++; }
            return hash;
        }

        internal static string ShaderVersion(ComputeShader shader)
        {
            var cache = current?.queries;
            if (cache != null && cache.Shaders.TryGetValue(shader, out var cached)) return cached;
            string path = AssetDatabase.GetAssetPath(shader);
            string version = string.IsNullOrEmpty(path) ? shader.GetEntityId().ToString()
                : AssetDatabase.AssetPathToGUID(path) + ":" + DependencyHash(path);
            cache?.Shaders.Add(shader, version);
            return version;
        }

        internal static string TextureVersion(Texture2D texture)
        {
            if (texture == null) return "missing";
            var cache = current?.queries;
            if (cache != null && cache.Textures.TryGetValue(texture, out var cached)) return cached;
            string path = AssetDatabase.GetAssetPath(texture);
            string version;
            if (string.IsNullOrEmpty(path))
                version = $"memory:{texture.GetEntityId()}:{texture.updateCount}:{texture.imageContentsHash}";
            else
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(texture, out string guid, out long localId);
                version = $"{guid}:{localId}:{DependencyHash(path)}";
            }
            cache?.Textures.Add(texture, version);
            return version;
        }
    }
}
