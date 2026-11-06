using System;
using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    internal static class OpenAIImageSettingsProvider
    {
        [MenuItem("Tools/AiCanvas/Connection Settings")]
        internal static void Open() => SettingsService.OpenUserPreferences(OpenAIImageSettings.PreferencesPath);

        [SettingsProvider]
        internal static SettingsProvider Create()
        {
            ApiConnectionForm api = null;
            return new SettingsProvider(OpenAIImageSettings.PreferencesPath, SettingsScope.User)
            {
                label = "AiCanvas",
                keywords = new HashSet<string> { "OpenAI", "GPT", "Image", "API", "Key", "Model", "ChatGPT", "Codex", "模型", "登录", "运行组件" },
                activateHandler = (_, __) =>
                {
                    api = new ApiConnectionForm();
                    CodexConnection.Changed += RepaintSettings;
                },
                guiHandler = _ =>
                {
                    using (new EditorGUI.DisabledScope(ImageGraphRunner.IsRunning || CodexConnection.Busy))
                    {
                        var mode = (ImageConnectionMode)EditorGUILayout.Popup("连接方式", (int)OpenAIImageSettings.ConnectionMode,
                            new[] { "API Key", "ChatGPT 账号（插件管理组件）" });
                        if (mode != OpenAIImageSettings.ConnectionMode)
                        {
                            OpenAIImageSettings.ConnectionMode = mode;
                            api.Close();
                            api = new ApiConnectionForm();
                            if (mode == ImageConnectionMode.ApiKey)
                                CodexConnection.Stop();
                        }
                    }
                    if (OpenAIImageSettings.ConnectionMode == ImageConnectionMode.ChatGpt)
                        DrawAccount();
                    else
                        api.Draw();
                },
                deactivateHandler = () =>
                {
                    api?.Close();
                    CodexConnection.Changed -= RepaintSettings;
                }
            };
        }

        private sealed class ApiConnectionForm
        {
            private string keyDraft = "";
            private string baseUrl = OpenAIImageSettings.BaseUrl;
            private string model = OpenAIImageSettings.ApiModel;
            private int timeout = OpenAIImageSettings.TimeoutSeconds;
            private string error = "";
            private string[] availableModels;
            private CancellationTokenSource lookup;

            internal void Close()
            {
                lookup?.Cancel();
                keyDraft = "";
            }

            internal void Draw()
            {
                EditorGUILayout.HelpBox("Run 会向配置的服务提交提示词和输入图片，并产生 API 费用。API Key 仅保存在当前 Unity 会话中；重启后需重新填写，或使用 OPENAI_API_KEY 环境变量。", MessageType.Info);
                using (new EditorGUI.DisabledScope(ImageGraphRunner.IsRunning || lookup != null))
                {
                    baseUrl = EditorGUILayout.TextField("Base URL", baseUrl);
                    keyDraft = EditorGUILayout.PasswordField("API Key", keyDraft);
                    bool hasKey = OpenAIImageSettings.HasSessionKey || OpenAIImageSettings.HasEnvironmentKey;
                    bool credentialsChanged = baseUrl.Trim().TrimEnd('/') != OpenAIImageSettings.BaseUrl ||
                        !string.IsNullOrWhiteSpace(keyDraft);
                    EditorGUILayout.LabelField("Credential", OpenAIImageSettings.HasSessionKey ? "Session key configured" :
                        OpenAIImageSettings.HasEnvironmentKey ? "OPENAI_API_KEY configured" : "Not configured");
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        model = EditorGUILayout.TextField("图像模型 ID", model);
                        using (new EditorGUI.DisabledScope(credentialsChanged || availableModels == null || availableModels.Length == 0))
                        {
                            if (GUILayout.Button("选择模型", EditorStyles.popup, GUILayout.Width(100)))
                                ShowModelMenu(availableModels, model, value => model = value);
                        }
                    }
                    using (new EditorGUI.DisabledScope(credentialsChanged || !hasKey))
                    {
                        if (GUILayout.Button(lookup == null ? "获取可用图像模型" : "正在获取图像模型…"))
                            RefreshModels();
                    }
                    if (credentialsChanged || !hasKey)
                        EditorGUILayout.HelpBox("保存服务地址和凭证后即可获取该 Key 的图像模型。", MessageType.None);
                    else if (availableModels?.Length == 0)
                        EditorGUILayout.HelpBox("服务没有列出 GPT 图像模型。也可以手动填写服务支持的图像模型 ID。", MessageType.Info);

                    timeout = EditorGUILayout.IntField("Timeout (seconds)", timeout);
                    if (GUILayout.Button("Apply Settings"))
                    {
                        try
                        {
                            var options = new OpenAIImageOptions(keyDraft, baseUrl, TimeSpan.FromSeconds(Mathf.Clamp(timeout, 30, 1800)), model);
                            if (!string.IsNullOrWhiteSpace(keyDraft))
                                options.ValidateCredentials();

                            OpenAIImageSettings.BaseUrl = baseUrl;
                            OpenAIImageSettings.ApiModel = options.Model;
                            OpenAIImageSettings.TimeoutSeconds = timeout;
                            if (!string.IsNullOrWhiteSpace(keyDraft))
                                OpenAIImageSettings.SetSessionKey(keyDraft);
                            if (credentialsChanged)
                                availableModels = null;

                            keyDraft = "";
                            baseUrl = OpenAIImageSettings.BaseUrl;
                            model = options.Model;
                            timeout = OpenAIImageSettings.TimeoutSeconds;
                            error = "";
                        }
                        catch (Exception exception)
                        {
                            error = exception.Message;
                        }
                    }
                    if (GUILayout.Button("Clear Session Key"))
                    {
                        OpenAIImageSettings.ClearSessionKey();
                        keyDraft = "";
                        availableModels = null;
                        error = "";
                    }
                }
                if (!string.IsNullOrEmpty(error))
                    EditorGUILayout.HelpBox(error, MessageType.Error);
                EditorGUILayout.HelpBox("默认地址：https://api.openai.com/v1。兼容服务需同时支持 Images 生成与编辑接口。超时范围 30–1800 秒；失败或超时后不会自动重试。", MessageType.None);
            }

            private async void RefreshModels()
            {
                using var request = new CancellationTokenSource();
                lookup = request;
                availableModels = null;
                error = "";
                try
                {
                    var models = await new OpenAIImageProvider().ListImageModelsAsync(request.Token);
                    if (!request.IsCancellationRequested)
                        availableModels = models;
                }
                catch (OperationCanceledException) when (request.IsCancellationRequested)
                {
                    // Closing the form cancels an in-flight catalog request.
                }
                catch (Exception exception)
                {
                    error = exception.Message;
                }
                finally
                {
                    lookup = null;
                    if (!request.IsCancellationRequested)
                        RepaintSettings();
                }
            }
        }

        private static void ShowModelMenu(string[] models, string current, Action<string> select, string defaultLabel = null)
        {
            var menu = new GenericMenu();
            if (defaultLabel != null)
                menu.AddItem(new GUIContent(defaultLabel), string.IsNullOrEmpty(current), () => select(""));
            foreach (string model in models)
                menu.AddItem(new GUIContent(model), model == current, () => select(model));

            menu.ShowAsContext();
        }

        private static void DrawAccount()
        {
            EditorGUILayout.HelpBox("插件会下载并管理官方 Codex 组件，无需另装 Codex CLI、Node.js 或 ChatGPT App。浏览器完成 ChatGPT 登录后，图片生成使用该账号的 Codex 用量。", MessageType.Info);
            EditorGUILayout.LabelField("运行组件", "Codex " + CodexRuntime.Version + " · Windows x64");
            bool installed = CodexRuntime.Installed;
            EditorGUILayout.LabelField("安装状态", installed ? "已安装并校验" : "未安装");
            EditorGUILayout.LabelField("账号", CodexConnection.AccountLabel);
            EditorGUILayout.LabelField("状态", CodexConnection.Status);
            if (!CodexRuntime.Supported) EditorGUILayout.HelpBox("当前平台暂不支持自动安装账号运行组件，可切换 API Key。", MessageType.Warning);
            using (new EditorGUI.DisabledScope(ImageGraphRunner.IsRunning || CodexConnection.Busy || !CodexRuntime.Supported))
            {
                int seconds = EditorGUILayout.IntField("超时（秒）", OpenAIImageSettings.TimeoutSeconds);
                if (seconds != OpenAIImageSettings.TimeoutSeconds) OpenAIImageSettings.TimeoutSeconds = seconds;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel("Codex 会话模型");
                    string model = OpenAIImageSettings.CodexModel;
                    if (GUILayout.Button(string.IsNullOrEmpty(model) ? "账号默认" : model, EditorStyles.popup))
                        ShowModelMenu(CodexConnection.SessionModels, model, value => OpenAIImageSettings.CodexModel = value, "账号默认");
                }
                using (new EditorGUI.DisabledScope(!installed || !CodexConnection.LoggedIn || CodexConnection.LoginId != null))
                {
                    if (GUILayout.Button("获取账号可用会话模型"))
                        CodexConnection.BeginListModels();
                }
                EditorGUILayout.HelpBox("所选会话模型负责调用 Codex 内置图像工具。具体绘图模型由账号服务决定；指定绘图模型请使用 API Key。", MessageType.None);
                if (GUILayout.Button(installed ? "重新下载并修复组件" : "安装运行组件并连接")) CodexConnection.BeginInstall();
                using (new EditorGUI.DisabledScope(!installed))
                {
                    if (GUILayout.Button("连接 / 刷新账号状态")) CodexConnection.BeginConnect();
                    using (new EditorGUI.DisabledScope(CodexConnection.LoginId != null))
                    {
                        if (GUILayout.Button("登录 ChatGPT")) CodexConnection.BeginLogin(false);
                        if (GUILayout.Button("使用设备验证码登录")) CodexConnection.BeginLogin(true);
                    }
                    if (CodexConnection.LoginId != null)
                    {
                        if (!string.IsNullOrEmpty(CodexConnection.DeviceCode))
                            EditorGUILayout.SelectableLabel("设备验证码：" + CodexConnection.DeviceCode, GUILayout.Height(20));
                        if (GUILayout.Button("重新打开登录页面")) Application.OpenURL(CodexConnection.LoginUrl);
                        if (GUILayout.Button("取消登录")) CodexConnection.BeginCancelLogin();
                    }
                    if (GUILayout.Button("退出此插件的 ChatGPT 账号")) CodexConnection.BeginLogout();
                    if (GUILayout.Button("卸载运行组件（保留账号缓存）")) CodexConnection.BeginUninstall();
                }
            }
            if (CodexConnection.Busy)
            {
                var rect = EditorGUILayout.GetControlRect(false, 18);
                EditorGUI.ProgressBar(rect, CodexConnection.Progress, CodexConnection.Status);
                if (GUILayout.Button("取消当前操作")) CodexConnection.CancelOperation();
            }
            if (!string.IsNullOrEmpty(CodexConnection.Error)) EditorGUILayout.HelpBox(CodexConnection.Error, MessageType.Error);
            EditorGUILayout.HelpBox("支持文生图、主图编辑、多张参考图和按顺序生成多张结果。尺寸与质量作为生成要求交给 Codex，无法保证与 API 参数完全一致；精确 Alpha Mask 请使用 API Key 模式。失败不会自动重试。", MessageType.Info);
            EditorGUILayout.HelpBox("组件、账号状态及运行缓存位于 Library/AiCanvas/Codex，独立于已安装的 Codex。清除 Library 后需要重新安装和检查登录；请勿把账号缓存纳入版本控制。退出插件账号不会退出其他 Codex 客户端。", MessageType.None);
            if (GUILayout.Button("打开组件缓存目录")) { System.IO.Directory.CreateDirectory(CodexRuntime.Root); EditorUtility.RevealInFinder(CodexRuntime.Root); }
        }

        private static void RepaintSettings()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
                if (window.GetType().Name.EndsWith("SettingsWindow", StringComparison.Ordinal)) window.Repaint();
        }
    }
}
