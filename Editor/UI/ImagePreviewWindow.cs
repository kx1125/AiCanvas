using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tsukuyomi.AiCanvas.Editor
{
    internal enum ImagePreviewChannel { RGBA, R, G, B, A }

    public sealed class ImagePreviewWindow : EditorWindow
    {
        [SerializeField] private Texture2D texture;
        [SerializeField] private ImagePreviewChannel channel;
        private GpuImage gpu;
        private AiCanvasGraph previewGraph;
        private string previewNodeId;
        private Image image;
        private Label description;
        private ToolbarButton saveButton;
        private ToolbarButton locateButton;
        private ToolbarToggle[] channelButtons;
        private RenderTexture channelTexture;
        private bool isSaving;

        private Texture Source => gpu?.Texture != null ? gpu.Texture : texture;

        internal ImagePreviewChannel Channel
        {
            get => channel;
            set
            {
                if (value < ImagePreviewChannel.RGBA || value > ImagePreviewChannel.A)
                    throw new ArgumentOutOfRangeException(nameof(value));
                channel = value;
                RefreshImage();
            }
        }

        private void OnEnable()
        {
            ImageAssetStore.ImageImported += OnImageImported;
            ImagePreviewStore.Changed += OnPreviewChanged;
        }
        private void OnDisable()
        {
            ImageAssetStore.ImageImported -= OnImageImported;
            ImagePreviewStore.Changed -= OnPreviewChanged;
            if (image != null) image.image = null;
            ReleaseChannelTexture();
            ReleaseGpu();
            previewGraph = null; previewNodeId = null;
        }
        private void OnImageImported(Texture2D image)
        {
            if (texture != image) return;
            RefreshImage();
        }

        public static void ShowTexture(Texture2D value)
        {
            var window = GetWindow<ImagePreviewWindow>("Image Preview");
            window.SetSource(value);
            window.minSize = new Vector2(360, 300);
            window.CreateGUI();
            window.Show();
        }

        internal static void ShowGpu(AiCanvasGraph graph, string nodeId)
        {
            var window = GetWindow<ImagePreviewWindow>("Image Preview");
            window.SetSource(graph, nodeId);
            window.minSize = new Vector2(360, 300);
            window.CreateGUI(); window.Show();
        }

        internal void SetSource(AiCanvasGraph graph, string nodeId)
        {
            previewGraph = graph; previewNodeId = nodeId; texture = null;
            RefreshGpu();
        }

        internal static void FollowOutputSelection(AiCanvasGraph graph, string previousKey, string nextKey)
        {
            if (graph == null || previousKey == nextKey) return;
            foreach (var window in Resources.FindObjectsOfTypeAll<ImagePreviewWindow>())
                if (window.previewGraph != null && ImagePreviewStore.Key(window.previewGraph) == ImagePreviewStore.Key(graph) &&
                    window.previewNodeId == previousKey)
                    window.SetSource(graph, nextKey);
        }

        internal void SetSource(Texture2D value)
        {
            previewGraph = null; previewNodeId = null;
            ReleaseGpu();
            texture = value;
            RefreshImage();
        }

        private void RefreshGpu()
        {
            if (previewGraph == null) return;
            var next = ImagePreviewStore.Find(previewGraph, previewNodeId, out _)?.Retain();
            ReleaseGpu(); gpu = next;
            RefreshImage();
        }

        private void OnPreviewChanged(string key)
        {
            if (previewGraph != null && (key == null || key == ImagePreviewStore.Key(previewGraph))) RefreshGpu();
        }

        private void ReleaseGpu()
        {
            if (gpu == null) return;
            if (image != null && image.image == gpu.Texture) image.image = null;
            gpu.Dispose(); gpu = null;
        }

        private void RefreshImage()
        {
            Texture source = Source;
            Texture displayed = source;
            string error = null;
            if (previewGraph != null) ImagePreviewStore.Find(previewGraph, previewNodeId, out error);
            if (source != null && channel != ImagePreviewChannel.RGBA)
            {
                try
                {
                    if (channelTexture == null || channelTexture.width != source.width || channelTexture.height != source.height)
                    {
                        ReleaseChannelTexture();
                        channelTexture = ImageComputeDevice.Allocate(source.width, source.height);
                        channelTexture.name = "AiCanvas channel preview";
                    }
                    ImageComputeDevice.PreviewChannel(source, (int)channel - 1, channelTexture);
                    displayed = channelTexture;
                }
                catch (Exception exception) { ReleaseChannelTexture(); displayed = null; error = exception.Message; }
            }
            else ReleaseChannelTexture();
            if (image != null) { image.image = displayed; image.MarkDirtyRepaint(); }
            if (description != null) description.text = error ?? (source == null ? "No image selected" :
                $"{source.name}  |  {source.width} x {source.height}  |  {channel}");
            RefreshControls();
            Repaint();
        }

        private void ReleaseChannelTexture()
        {
            if (channelTexture == null) return;
            if (image != null && image.image == channelTexture) image.image = null;
            channelTexture.Release();
            DestroyImmediate(channelTexture);
            channelTexture = null;
        }

        private void RefreshControls()
        {
            saveButton?.SetEnabled(Source != null && !isSaving);
            if (saveButton != null) saveButton.text = isSaving ? "Saving..." : "Save";
            locateButton?.SetEnabled(texture != null && !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(texture)));
            if (channelButtons == null) return;
            for (int index = 0; index < channelButtons.Length; index++)
            {
                channelButtons[index].SetValueWithoutNotify(index == (int)channel);
                channelButtons[index].SetEnabled(Source != null);
            }
        }

        internal async Task<Texture2D> SaveAsync()
        {
            if (Source == null || isSaving) return null;
            isSaving = true;
            RefreshControls();
            try
            {
                // Freeze the original image before awaiting; channel display and later edits cannot affect the export.
                using var lease = gpu?.Texture != null ? gpu.Retain() :
                    ImageComputeDevice.Import(texture, ImageInputSnapshot.GetVersion(texture));
                var png = await ImageComputeDevice.ReadPngAsync(lease, CancellationToken.None);
                var path = ImageAssetStore.GeneratedPath(AiCanvasResourcePaths.Generated, Guid.NewGuid().ToString("N"), 0);
                return ImageAssetStore.SavePng(path, png, lease.Kind == ImageValueKind.Color);
            }
            finally
            {
                isSaving = false;
                if (this != null) RefreshControls();
            }
        }

        private async void SaveImage()
        {
            try
            {
                var asset = await SaveAsync();
                if (asset == null) return;
                EditorGUIUtility.PingObject(asset);
                if (this != null) ShowNotification(new GUIContent("Saved: " + AssetDatabase.GetAssetPath(asset)));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (this != null) ShowNotification(new GUIContent("Save failed: " + exception.Message));
            }
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            var bar = new Toolbar();
            bar.style.flexShrink = 0;
            saveButton = new ToolbarButton(SaveImage) { name = "ai-canvas-preview-save", text = "Save",
                tooltip = "将完整 RGBA 原图保存为 PNG 到 Assets/AiCanvasResource/Generated；通道切换仅影响查看。" };
            locateButton = new ToolbarButton(() => { if (texture != null) EditorGUIUtility.PingObject(texture); })
                { text = "Locate Asset" };
            bar.Add(saveButton);
            bar.Add(locateButton);
            bar.Add(new ToolbarSpacer { flex = true });
            channelButtons = new ToolbarToggle[5];
            for (int index = 0; index < channelButtons.Length; index++)
            {
                var value = (ImagePreviewChannel)index;
                var button = new ToolbarToggle { name = "ai-canvas-preview-channel-" + value, text = value.ToString(),
                    tooltip = index == 0 ? "查看完整 RGBA 图片" : "以灰度查看 " + value + " 通道" };
                button.RegisterValueChangedCallback(_ => Channel = value);
                channelButtons[index] = button;
                bar.Add(button);
            }
            rootVisualElement.Add(bar);
            image = new Image { name = "ai-canvas-preview-image", scaleMode = ScaleMode.ScaleToFit };
            image.style.flexGrow = 1;
            image.style.minHeight = 0;
            rootVisualElement.Add(image);
            description = new Label { name = "ai-canvas-preview-description" };
            description.style.whiteSpace = WhiteSpace.Normal;
            description.style.flexShrink = 0;
            rootVisualElement.Add(description);
            RefreshImage();
        }
    }
}
