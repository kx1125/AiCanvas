using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    /// <summary>A lease on an immutable GPU result. Every retained lease must be disposed.</summary>
    internal sealed class GpuImage : IDisposable
    {
        private sealed class Storage
        {
            internal RenderTexture Texture;
            internal int References = 1;
        }

        private static readonly HashSet<Storage> Live = new();
        private Storage storage;
        internal RenderTexture Texture => storage?.Texture;
        internal string Version { get; }
        internal ImageValueKind Kind { get; }
        internal bool HasAlpha { get; }
        internal int Width => Texture.width;
        internal int Height => Texture.height;
        internal long Bytes => (long)Width * Height * 8;

        internal GpuImage(RenderTexture texture, string version, ImageValueKind kind, bool hasAlpha = true)
        {
            storage = new Storage { Texture = texture };
            Live.Add(storage);
            Version = version; Kind = kind; HasAlpha = hasAlpha;
        }

        private GpuImage(GpuImage source)
        {
            if (source.Texture == null) throw new ObjectDisposedException(nameof(GpuImage));
            storage = source.storage;
            storage.References++;
            Version = source.Version; Kind = source.Kind; HasAlpha = source.HasAlpha;
        }

        internal GpuImage Retain() => new(this);

        public void Dispose()
        {
            if (storage == null) return;
            if (--storage.References == 0) Destroy(storage);
            storage = null;
        }

        private static void Destroy(Storage value)
        {
            if (value.Texture != null)
            {
                value.Texture.Release();
                UnityEngine.Object.DestroyImmediate(value.Texture);
                value.Texture = null;
            }
            Live.Remove(value);
        }

        internal static void ReleaseAll()
        {
            foreach (var value in new List<Storage>(Live)) Destroy(value);
        }
    }

    internal sealed class GpuImageCache : IDisposable
    {
        private sealed class Entry { internal string Key; internal GpuImage Image; }
        private readonly Dictionary<string, LinkedListNode<Entry>> entries = new();
        private readonly LinkedList<Entry> lru = new();
        private readonly long budget;
        private long bytes;
        internal int Count => entries.Count;

        internal GpuImageCache(long budgetBytes = 256L * 1024 * 1024) { budget = budgetBytes; }

        internal bool TryGet(string key, out GpuImage image)
        {
            if (entries.TryGetValue(key, out var node) && node.Value.Image.Texture != null)
            {
                lru.Remove(node); lru.AddLast(node);
                image = node.Value.Image.Retain();
                return true;
            }
            image = null;
            return false;
        }

        internal void Add(string key, GpuImage image)
        {
            if (entries.ContainsKey(key) || image.Bytes > budget) return;
            while (bytes + image.Bytes > budget && lru.First != null) RemoveFirst();
            var entry = new Entry { Key = key, Image = image.Retain() };
            entries.Add(key, lru.AddLast(entry));
            bytes += image.Bytes;
        }

        private void RemoveFirst()
        {
            var entry = lru.First.Value;
            bytes -= entry.Image.Bytes;
            entries.Remove(entry.Key); lru.RemoveFirst(); entry.Image.Dispose();
        }

        public void Dispose() { while (lru.First != null) RemoveFirst(); }
    }
}
