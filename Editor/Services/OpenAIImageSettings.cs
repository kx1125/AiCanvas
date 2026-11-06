using System;
using UnityEditor;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    internal enum ImageConnectionMode { ApiKey, ChatGpt }

    // Credentials never become graph options, project assets, or EditorPrefs entries.
    internal static class OpenAIImageSettings
    {
        internal const string PreferencesPath = "Preferences/AiCanvas";
        internal const string DefaultBaseUrl = "https://api.openai.com/v1";
        private static string Prefix => "Tsukuyomi.AiCanvas." + Hash128.Compute(Application.dataPath) + ".";
        internal static event Action Changed;

        internal static ImageConnectionMode ConnectionMode
        {
            get => EditorPrefs.GetInt(Prefix + "ConnectionMode", 0) == 1 ? ImageConnectionMode.ChatGpt : ImageConnectionMode.ApiKey;
            set { EditorPrefs.SetInt(Prefix + "ConnectionMode", (int)value); EditorEvent.Invoke(Changed); }
        }

        internal static IImageProvider CreateProvider() => ConnectionMode == ImageConnectionMode.ChatGpt
            ? (IImageProvider)new CodexImageProvider() : new OpenAIImageProvider();

        internal static string BaseUrl
        {
            get => EditorPrefs.GetString(Prefix + "BaseUrl", DefaultBaseUrl);
            set { EditorPrefs.SetString(Prefix + "BaseUrl", OpenAIImageOptions.NormalizeBaseUrl(value).AbsoluteUri.TrimEnd('/')); EditorEvent.Invoke(Changed); }
        }

        internal static string ApiModel
        {
            get => EditorPrefs.GetString(Prefix + "ApiModel", OpenAIImageProvider.DefaultModel);
            set
            {
                EditorPrefs.SetString(Prefix + "ApiModel", OpenAIImageOptions.NormalizeModel(value));
                EditorEvent.Invoke(Changed);
            }
        }

        // An empty value lets the signed-in Codex account choose its default session model.
        internal static string CodexModel
        {
            get => EditorPrefs.GetString(Prefix + "CodexModel", "");
            set
            {
                EditorPrefs.SetString(Prefix + "CodexModel", value);
                EditorEvent.Invoke(Changed);
            }
        }

        internal static int TimeoutSeconds
        {
            get => Mathf.Clamp(EditorPrefs.GetInt(Prefix + "TimeoutSeconds", 600), 30, 1800);
            set => EditorPrefs.SetInt(Prefix + "TimeoutSeconds", Mathf.Clamp(value, 30, 1800));
        }

        // Bind the session credential to its endpoint, including across script reloads.
        private static string CredentialKey => Prefix + "ApiKey." + Hash128.Compute(BaseUrl);
        internal static bool HasSessionKey => !string.IsNullOrWhiteSpace(SessionState.GetString(CredentialKey, ""));
        internal static bool HasEnvironmentKey => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
        internal static void SetSessionKey(string value) { SessionState.SetString(CredentialKey, value.Trim()); EditorEvent.Invoke(Changed); }
        internal static void ClearSessionKey() { SessionState.EraseString(CredentialKey); EditorEvent.Invoke(Changed); }

        internal static string ConnectionLabel => ConnectionMode == ImageConnectionMode.ChatGpt
            ? CodexConnection.Busy ? "ChatGPT：连接处理中" : CodexConnection.LoggedIn
                ? CodexConnection.IsConnected ? "ChatGPT：已连接" : "ChatGPT：已登录"
                : CodexConnection.AccountChecked ? "ChatGPT：未登录" : "ChatGPT：待连接"
            : HasSessionKey || HasEnvironmentKey ? "API Key：已配置" : "API Key：未配置";

        internal static OpenAIImageOptions Capture()
        {
            string key = SessionState.GetString(CredentialKey, "");
            if (string.IsNullOrWhiteSpace(key)) key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            var options = new OpenAIImageOptions(key, BaseUrl, TimeSpan.FromSeconds(TimeoutSeconds), ApiModel);
            options.ValidateCredentials();
            return options;
        }
    }

    internal sealed class OpenAIImageOptions
    {
        internal string ApiKey { get; }
        internal Uri BaseUri { get; }
        internal TimeSpan Timeout { get; }
        internal string Model { get; }

        internal OpenAIImageOptions(string apiKey, string baseUrl, TimeSpan timeout, string model)
        {
            ApiKey = (apiKey ?? "").Trim();
            BaseUri = NormalizeBaseUrl(baseUrl);
            Model = NormalizeModel(model);
            if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(timeout));
            Timeout = timeout;
        }

        internal void ValidateCredentials()
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
                throw new InvalidOperationException("请在 Tools > AiCanvas > Connection Settings 设置 API Key，或为 Unity 配置 OPENAI_API_KEY 环境变量。");
            foreach (char character in ApiKey)
                if (char.IsWhiteSpace(character) || char.IsControl(character))
                    throw new InvalidOperationException("API Key 不能包含空白或控制字符，请重新粘贴。");
        }

        internal static Uri NormalizeBaseUrl(string value)
        {
            if (!Uri.TryCreate((value ?? "").Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new InvalidOperationException("Base URL 必须为 HTTPS API 根地址（通常以 /v1 结尾），不能含凭证、查询参数或片段。本机调试可使用 HTTP。");
            return uri;
        }

        internal static string NormalizeModel(string value)
        {
            string model = value?.Trim();
            if (string.IsNullOrEmpty(model))
                throw new InvalidOperationException("请输入图像模型 ID，或从可用模型中选择。");

            foreach (char character in model)
                if (char.IsWhiteSpace(character) || char.IsControl(character))
                    throw new InvalidOperationException("图像模型 ID 不能包含空白或控制字符。");

            return model;
        }
    }
}
