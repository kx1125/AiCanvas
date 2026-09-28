using System;
using System.Collections.Generic;
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
            string keyDraft = "";
            string baseUrl = OpenAIImageSettings.BaseUrl;
            int timeout = OpenAIImageSettings.TimeoutSeconds;
            string error = "";
            return new SettingsProvider(OpenAIImageSettings.PreferencesPath, SettingsScope.User)
            {
                label = "AiCanvas",
                keywords = new HashSet<string> { "OpenAI", "GPT", "Image", "API", "Key", "ChatGPT", "Codex", "登录", "运行组件" },
                activateHandler = (_, __) => CodexConnection.Changed += RepaintSettings,
                guiHandler = _ =>
                {
                    EditorGUILayout.LabelField("GPT Image 2", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Model", OpenAIImageProvider.Model);
                    using (new EditorGUI.DisabledScope(ImageGraphRunner.IsRunning || CodexConnection.Busy))
                    {
                        var mode = (ImageConnectionMode)EditorGUILayout.Popup("连接方式", (int)OpenAIImageSettings.ConnectionMode,
                            new[] { "API Key", "ChatGPT 账号（插件管理组件）" });
                        if (mode != OpenAIImageSettings.ConnectionMode)
                        {
                            OpenAIImageSettings.ConnectionMode = mode;
                            keyDraft = "";
                            if (mode == ImageConnectionMode.ApiKey) CodexConnection.Stop();
                        }
                    }
                    if (OpenAIImageSettings.ConnectionMode == ImageConnectionMode.ChatGpt)
                    {
                        DrawAccount();
                        return;
                    }
                    EditorGUILayout.HelpBox("Run 会向配置的服务提交提示词和输入图片，并产生 API 费用。API Key 仅保存在当前 Unity 会话中；重启后需重新填写，或使用 OPENAI_API_KEY 环境变量。", MessageType.Info);
                    using (new EditorGUI.DisabledScope(ImageGraphRunner.IsRunning))
                    {
                        baseUrl = EditorGUILayout.TextField("Base URL", baseUrl);
                        timeout = EditorGUILayout.IntField("Timeout (seconds)", timeout);
                        keyDraft = EditorGUILayout.PasswordField("API Key", keyDraft);
                        EditorGUILayout.LabelField("Credential", OpenAIImageSettings.HasSessionKey ? "Session key configured" :
                            OpenAIImageSettings.HasEnvironmentKey ? "OPENAI_API_KEY configured" : "Not configured");
                        if (GUILayout.Button("Apply Settings"))
                        {
                            try
                            {
                                var options = new OpenAIImageOptions(keyDraft, baseUrl, TimeSpan.FromSeconds(Mathf.Clamp(timeout, 30, 1800)));
                                if (!string.IsNullOrWhiteSpace(keyDraft)) options.ValidateCredentials();
                                OpenAIImageSettings.BaseUrl = baseUrl;
                                OpenAIImageSettings.TimeoutSeconds = timeout;
                                if (!string.IsNullOrWhiteSpace(keyDraft)) OpenAIImageSettings.SetSessionKey(keyDraft);
                                keyDraft = "";
                                baseUrl = OpenAIImageSettings.BaseUrl;
                                timeout = OpenAIImageSettings.TimeoutSeconds;
                                error = "";
                            }
                            catch (Exception exception) { error = exception.Message; }
                        }
                        if (GUILayout.Button("Clear Session Key"))
                        {
                            OpenAIImageSettings.ClearSessionKey();
                            keyDraft = "";
                        }
                    }
                    if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
                    EditorGUILayout.HelpBox("默认地址：https://api.openai.com/v1。兼容服务需同时支持 Images 生成与编辑接口。超时范围 30–1800 秒；失败或超时后不会自动重试。", MessageType.None);
                },
                deactivateHandler = () => { keyDraft = ""; CodexConnection.Changed -= RepaintSettings; }
            };
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
