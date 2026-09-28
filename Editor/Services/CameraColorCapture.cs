using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.GraphToolkit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Tsukuyomi.AiCanvas.Editor
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, sourceNamespace: "Tsukuyomi.ImageCanvas.Editor",
        sourceAssembly: "Tsukuyomi.ImageCanvas.Editor", sourceClassName: "CameraColorSettings")]
    [Serializable]
    public struct CameraColorSettings
    {
        public bool useMainCamera;
        public string cameraId;
        public bool useCameraSize;
        [Delayed] public int width, height;
        public static CameraColorSettings Default => new CameraColorSettings
            { useMainCamera = true, useCameraSize = true, width = 1920, height = 1080 };
    }

    public static class CameraColorCapture
    {
        private static readonly Dictionary<string, Camera> SessionCameras = new Dictionary<string, Camera>();
        private static bool capturing;

        // Graph assets cannot serialize scene object references. Saved scenes use
        // GlobalObjectId; unsaved scenes have an explicitly session-only reference.
        public static string Reference(Camera camera)
        {
            if (camera == null) return "";
            if (!camera.gameObject.scene.IsValid() || EditorUtility.IsPersistent(camera))
                throw new InvalidOperationException("请选择已加载场景中的 Camera。");
            if (!string.IsNullOrEmpty(camera.gameObject.scene.path))
                return GlobalObjectId.GetGlobalObjectIdSlow(camera).ToString();
            var existing = SessionCameras.FirstOrDefault(pair => pair.Value == camera);
            if (existing.Key != null) return existing.Key;
            string key = "session:" + Guid.NewGuid().ToString("N");
            SessionCameras[key] = camera;
            return key;
        }

        public static Camera ResolveReference(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (id.StartsWith("session:", StringComparison.Ordinal))
                return SessionCameras.TryGetValue(id, out var camera) ? camera : null;
            return GlobalObjectId.TryParse(id, out var globalId)
                ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalId) as Camera : null;
        }

        public static Camera ResolveCamera(CameraColorSettings settings)
        {
            var camera = settings.useMainCamera ? Camera.main : ResolveReference(settings.cameraId);
            if (camera == null)
                throw new InvalidOperationException(settings.useMainCamera
                    ? "没有可用的 MainCamera，请启用并标记主相机，或关闭 Main Camera 后指定相机。"
                    : "指定相机不可用。请加载相机场景；未保存场景的引用在重载后需要重新指定。");
            if (!camera.gameObject.scene.IsValid() || camera.cameraType != CameraType.Game)
                throw new InvalidOperationException("CameraColor 需要场景中的 Game Camera。");
            if (!camera.gameObject.activeInHierarchy)
                throw new InvalidOperationException("请先激活相机所在的 GameObject。");
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset &&
                camera.TryGetComponent<UniversalAdditionalCameraData>(out var data) && data.renderType == CameraRenderType.Overlay)
                throw new InvalidOperationException("请选择 Base Camera；其 Camera Stack 中的 Overlay 相机会一起渲染。");
            if (camera.stereoEnabled)
                throw new InvalidOperationException("CameraColor 暂不支持立体相机，请使用普通 Game Camera。");
            return camera;
        }

        // Caller owns the returned CPU snapshot. All GPU resources and camera
        // target state are restored even if rendering or readback fails.
        public static Texture2D Render(CameraColorSettings settings)
        {
            if (capturing) throw new InvalidOperationException("CameraColor 正在捕获，请稍后重试。");
            var camera = ResolveCamera(settings);
            int width = settings.useCameraSize ? camera.pixelWidth : settings.width;
            int height = settings.useCameraSize ? camera.pixelHeight : settings.height;
            int limit = Mathf.Min(4096, SystemInfo.maxTextureSize);
            if (width < 1 || height < 1 || width > limit || height > limit)
                throw new InvalidOperationException($"捕获宽高必须在 1–{limit} 之间；可关闭 Camera Size 后自定义尺寸。");
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var previousType = camera.cameraType;
            RenderTexture target = null;
            Texture2D image = null;
            capturing = true;
            try
            {
                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                    { name = "AiCanvas CameraColor RT", hideFlags = HideFlags.HideAndDontSave, antiAliasing = 1 };
                if (!target.Create()) throw new InvalidOperationException("无法创建 CameraColor RenderTexture。");
                if (GraphicsSettings.currentRenderPipeline == null)
                {
                    camera.targetTexture = target;
                    camera.Render();
                }
                else
                {
                    // StandardRequest runs the complete URP base-camera stack,
                    // including post processing and final resolve to this RT.
                    var request = new RenderPipeline.StandardRequest { destination = target };
                    if (!RenderPipeline.SupportsRenderRequest(camera, request))
                        throw new InvalidOperationException("当前渲染管线不支持 StandardRequest，无法捕获相机最终颜色。");
                    RenderPipeline.SubmitRenderRequest(camera, request);
                }
                RenderTexture.active = target;
                image = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
                    { name = camera.name + " Color", hideFlags = HideFlags.HideAndDontSave };
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                image.Apply(false, false);
                return image;
            }
            catch
            {
                if (image != null) Object.DestroyImmediate(image);
                throw;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.cameraType = previousType;
                RenderTexture.active = previousActive;
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                capturing = false;
            }
        }

        public static Texture2D ReadOutput(CameraColorNode node)
        {
            var frame = node.Frame;
            if (frame.texture == null) throw new InvalidOperationException("请先点击 CameraColor 的 Capture 获取相机画面。");
            if (frame.settings != JsonUtility.ToJson(node.Settings))
                throw new InvalidOperationException("CameraColor 设置已修改，请再次 Capture 更新画面。");
            // Undo changes graph metadata, but cannot restore overwritten PNG
            // bytes. Never treat an old option value as the old captured image.
            if (!string.IsNullOrEmpty(frame.contentHash) && frame.contentHash !=
                AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(frame.texture)).ToString())
                throw new InvalidOperationException("CameraColor 的图片已被覆盖，请再次 Capture 更新节点。Undo 不会恢复旧图片内容。");
            return frame.texture;
        }

        internal static string CapturePath(AiCanvasGraph graph, CameraColorNode node, string folder)
        {
            string graphGuid = AssetDatabase.AssetPathToGUID(GraphDatabase.GetGraphAssetPath(graph));
            if (string.IsNullOrEmpty(graphGuid)) throw new InvalidOperationException("请先保存图资源，再 Capture。");
            // A copied graph can retain all its node IDs, so both identities are
            // required. Node copies must never inherit the source file to write.
            string filename = $"CameraColor_{graphGuid}_{node.GetStableId()}.png";
            string current = AssetDatabase.GetAssetPath(node.Frame.texture);
            if (current.StartsWith(folder.TrimEnd('/') + "/", StringComparison.Ordinal) && Path.GetFileName(current) == filename) return current;
            return $"{folder.TrimEnd('/')}/{filename}";
        }

        public static Texture2D Capture(AiCanvasGraph graph, CameraColorNode node,
            string folder = AiCanvasResourcePaths.Captures)
        {
            if (graph == null || node == null || !graph.GetNodes().Contains(node))
                throw new InvalidOperationException("CameraColor 节点已不存在。");
            if (ImageGraphRunner.IsRunning) throw new InvalidOperationException("请等待当前生成任务结束后再 Capture。");
            folder = ImageAssetStore.ValidateFolder(folder);
            var settings = node.Settings;
            string path = CapturePath(graph, node, folder);
            var camera = ResolveCamera(settings);
            var snapshot = Render(settings);
            Texture2D texture;
            try { texture = ImageAssetStore.SavePng(path, snapshot.EncodeToPNG()); }
            finally { Object.DestroyImmediate(snapshot); }
            var consumers = new List<IPort>();
            node.GetOutputPortByName("Image").GetConnectedPorts(consumers);
            graph.UndoBeginRecordGraph("Capture camera color");
            try
            {
                GraphOptionAccess.Set(node, "Frame", new CameraColorFrame
                {
                    texture = texture, settings = JsonUtility.ToJson(settings),
                    contentHash = AssetDatabase.GetAssetDependencyHash(path).ToString(),
                    caption = $"{camera.name} | {texture.width} × {texture.height} | {DateTime.Now:HH:mm:ss.fff}"
                });
                foreach (var output in graph.GetNodes().OfType<ImageOutputNode>())
                    if (consumers.Any(port => port.GetStableId() == output.GetInputPortByName("Image").GetStableId()))
                        GraphOptionAccess.Set(output, "Preview", new ImageCard(texture, "CameraColor | " + camera.name));
            }
            finally { graph.UndoEndRecordGraph(); }
            GraphDatabase.SaveGraph(graph);
            return texture;
        }
    }
}
