using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tsukuyomi.AiCanvas.Editor
{
    internal sealed class ImageAssetPreview : IDisposable
    {
        private Texture2D asset;
        private string version;
        internal GpuImage Lease { get; private set; }
        internal Texture Texture { get; private set; }
        internal string Error { get; private set; }

        internal void Set(Texture2D value)
        {
            if (value == null)
            {
                Dispose();
                return;
            }

            string next = ImageInputSnapshot.GetVersion(value);
            if (asset == value && version == next && Texture != null)
                return;

            Dispose();
            asset = value;
            version = next;
            try
            {
                var source = new ImageSourcePixels(value);
                // A mipmapped in-memory texture can lose its resident mip after the window opens.
                if (source.NeedsCopy || value.mipmapCount > 1)
                    Lease = ImageComputeDevice.ImportSource(value, next, source);

                Texture = Lease?.Texture != null ? Lease.Texture : value;
            }
            catch (Exception exception)
            {
                Error = exception.Message;
            }
        }

        public void Dispose()
        {
            Texture = null;
            Lease?.Dispose();
            Lease = null;
            asset = null;
            version = null;
            Error = null;
        }

        internal static Action<Texture> BindThumbnail(Image view)
        {
            var preview = new ImageAssetPreview();
            Texture selected = null;

            void Refresh()
            {
                var asset = selected as Texture2D;
                preview.Set(asset);
                view.image = asset != null ? preview.Texture : selected;
                view.tooltip = preview.Error;
            }

            view.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                ImagePreviewAssetChanges.SourceFilesChanged += Refresh;
                Refresh();
            });
            view.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                ImagePreviewAssetChanges.SourceFilesChanged -= Refresh;
                view.image = null;
                preview.Dispose();
            });

            return value =>
            {
                selected = value;
                if (view.panel != null)
                    Refresh();
            };
        }
    }
}
