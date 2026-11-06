using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    [InitializeOnLoad]
    internal static class CodexConnection
    {
        private static readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private static readonly SemaphoreSlim connectionLock = new SemaphoreSlim(1, 1);
        private static readonly ConcurrentQueue<Action> mainThread = new ConcurrentQueue<Action>();
        private static CodexRpcClient client;
        private static CancellationTokenSource operation;
        internal static bool Busy { get; private set; }
        internal static bool LoggedIn { get; private set; }
        internal static bool AccountChecked { get; private set; }
        internal static bool IsConnected => client != null && client.IsAlive;
        internal static bool ImageGenerationAvailable { get; private set; }
        internal static string[] SessionModels { get; private set; } = Array.Empty<string>();
        internal static string AccountLabel { get; private set; } = "尚未检查";
        internal static string Status { get; private set; } = "等待连接";
        internal static string Error { get; private set; } = "";
        internal static string LoginId { get; private set; }
        internal static string LoginUrl { get; private set; }
        internal static string DeviceCode { get; private set; }
        internal static float Progress { get; private set; }
        internal static event Action Changed;

        static CodexConnection()
        {
            EditorApplication.update += () => { while (mainThread.TryDequeue(out var action)) EditorEvent.Invoke(action); };
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            EditorApplication.quitting += Shutdown;
        }

        internal static void BeginInstall() => RunOperation(async token =>
        {
            Stop();
            Status = "下载并校验运行组件…";
            await CodexRuntime.InstallAsync(new Progress<float>(value => { Progress = value; EditorEvent.Invoke(Changed); }), token);
            Status = "组件已安装";
            await ConnectAsync(token);
        });

        internal static void BeginConnect() => RunOperation(async token => { await ConnectAsync(token); });

        internal static void BeginListModels()
        {
            RunOperation(async token =>
            {
                var rpc = await ConnectAsync(token);
                if (!LoggedIn)
                    throw new InvalidOperationException("请先登录 ChatGPT 账号。");

                Status = "正在获取会话模型…";
                EditorEvent.Invoke(Changed);
                string account = AccountLabel;
                var models = await ReadSessionModelsAsync(rpc, token);
                if (!LoggedIn || AccountLabel != account)
                    throw new InvalidOperationException("ChatGPT 账号已切换，请重新获取会话模型。");

                SessionModels = models;
                Status = $"已获取 {SessionModels.Length} 个会话模型";
            });
        }

        private static async Task<string[]> ReadSessionModelsAsync(CodexRpcClient rpc, CancellationToken token)
        {
            var models = new SortedSet<string>(StringComparer.Ordinal);
            var cursors = new HashSet<string>(StringComparer.Ordinal);
            string cursor = null;
            do
            {
                var page = await rpc.RequestAsync("model/list", new { cursor, includeHidden = false }, token);
                if (!(page["data"] is JArray entries))
                    throw new IOException("Codex 模型列表缺少 data 数组。");

                foreach (var entry in entries)
                {
                    string model = (string)entry["model"];
                    if (!string.IsNullOrWhiteSpace(model) && (bool?)entry["hidden"] != true)
                        models.Add(model);
                }

                cursor = (string)page["nextCursor"];
                if (!string.IsNullOrEmpty(cursor) && !cursors.Add(cursor))
                    throw new IOException("Codex 模型列表返回了重复的分页标记。");
            } while (!string.IsNullOrEmpty(cursor));

            return models.ToArray();
        }

        internal static void BeginUninstall() => RunOperation(token =>
        {
            Stop();
            CodexRuntime.DeleteContainedDirectory(CodexRuntime.Root, CodexRuntime.InstallDirectory);
            Status = "组件已卸载；账号缓存保留，重新安装后可继续使用";
            return Task.CompletedTask;
        });
        internal static void BeginLogin(bool deviceCode) => RunOperation(async token =>
        {
            var rpc = await ConnectAsync(token);
            if (LoginId != null) await CancelLoginAsync(rpc, token);
            Status = "正在开始登录…";
            JToken result = await rpc.RequestAsync("account/login/start", deviceCode ? (object)new { type = "chatgptDeviceCode" } :
                new { type = "chatgpt", useHostedLoginSuccessPage = true, appBrand = "codex" }, token);
            LoginId = (string)result["loginId"];
            LoginUrl = ValidateLoginUrl((string)(result["authUrl"] ?? result["verificationUrl"]));
            DeviceCode = (string)result["userCode"];
            Status = "请在浏览器完成登录，完成后此处会自动更新";
            Application.OpenURL(LoginUrl);
        });

        internal static void BeginCancelLogin() => RunOperation(async token =>
        {
            if (client != null) await CancelLoginAsync(client, token);
            Status = "登录已取消";
        });

        internal static void BeginLogout() => RunOperation(async token =>
        {
            var rpc = await ConnectAsync(token);
            if (LoginId != null) await CancelLoginAsync(rpc, token);
            await rpc.RequestAsync("account/logout", new { }, token);
            SessionModels = Array.Empty<string>();
            LoggedIn = false;
            AccountChecked = true;
            AccountLabel = "未登录";
            Status = "已退出此插件账号";
        });

        internal static void CancelOperation() { operation?.Cancel(); Stop(); }

        internal static async Task<CodexRpcClient> ConnectAsync(CancellationToken token)
        {
            if (!CodexRuntime.Supported) throw new PlatformNotSupportedException("账号运行组件目前支持 Windows x64 Editor。");
            if (!CodexRuntime.Installed) throw new InvalidOperationException("请先在 AiCanvas 设置中安装运行组件。");
            string home = CodexRuntime.Home;
            string cwd = Path.Combine(CodexRuntime.Root, "workspace");
            string executable = CodexRuntime.Executable;
            await connectionLock.WaitAsync(token);
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(45));
                    if (client == null || !client.IsAlive)
                    {
                        Stop();
                        Status = "启动运行组件…";
                        Directory.CreateDirectory(home);
                        File.WriteAllText(Path.Combine(home, "config.toml"), ManagedConfig);
                        var started = await CodexRpcClient.StartAsync(executable, home, cwd, timeout.Token);
                        client = started;
                        started.Notification += (method, data) => mainThread.Enqueue(() =>
                        {
                            if (client == started && !lifetime.IsCancellationRequested) OnNotification(method, data);
                        });
                        started.Disconnected += exception => mainThread.Enqueue(() =>
                        {
                            if (client != started) return;
                            Stop(); Error = exception.Message; Status = "连接已断开"; EditorEvent.Invoke(Changed);
                        });
                    }
                    var capabilities = await client.RequestAsync("modelProvider/capabilities/read", new { }, timeout.Token);
                    ImageGenerationAvailable = (bool?)capabilities["imageGeneration"] == true;
                    await RefreshAccountAsync(client, timeout.Token);
                    Status = LoggedIn ? "已连接" : "组件就绪，请登录 ChatGPT";
                    return client;
                }
            }
            finally { connectionLock.Release(); EditorEvent.Invoke(Changed); }
        }

        private static async Task RefreshAccountAsync(CodexRpcClient rpc, CancellationToken token)
        {
            var account = (await rpc.RequestAsync("account/read", new { refreshToken = false }, token))["account"] as JObject;
            LoggedIn = (string)account?["type"] == "chatgpt";
            AccountChecked = true;
            string label = LoggedIn ? ((string)account["email"] ?? "ChatGPT") + " · " + ((string)account["planType"] ?? "unknown") : "未登录";
            if (label != AccountLabel)
                SessionModels = Array.Empty<string>();

            AccountLabel = label;
        }

        private static async Task CancelLoginAsync(CodexRpcClient rpc, CancellationToken token)
        {
            if (LoginId != null) await rpc.RequestAsync("account/login/cancel", new { loginId = LoginId }, token);
            ClearLogin();
        }

        private static void OnNotification(string method, JObject data)
        {
            if (method == "account/login/completed" && (string)data["loginId"] == LoginId)
            {
                ClearLogin();
                if ((bool?)data["success"] == true) BeginConnect();
                else { Error = CodexRpcClient.SafeMessage((string)data["error"]); Status = "登录未完成"; }
            }
            else if (method == "account/updated" && data["authMode"]?.Type == JTokenType.Null)
            {
                SessionModels = Array.Empty<string>();
                LoggedIn = false; AccountChecked = true; AccountLabel = "未登录";
            }
            EditorEvent.Invoke(Changed);
        }

        internal static string ValidateLoginUrl(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) ||
                (uri.Host != "auth.openai.com" && uri.Host != "chatgpt.com"))
                throw new IOException("运行组件返回了非官方登录地址。");
            return uri.AbsoluteUri;
        }

        private static async void RunOperation(Func<CancellationToken, Task> body)
        {
            if (Busy || ImageGraphRunner.IsRunning) return;
            Busy = true; Error = ""; Progress = 0;
            operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            operation.CancelAfter(TimeSpan.FromMinutes(15));
            try { EditorEvent.Invoke(Changed); await body(operation.Token); }
            catch (OperationCanceledException) { Status = "操作已取消或超时"; }
            catch (Exception exception) { Error = CodexRpcClient.SafeMessage(exception.Message); Status = "操作失败"; }
            finally { operation.Dispose(); operation = null; Busy = false; EditorEvent.Invoke(Changed); }
        }

        internal static void Stop()
        {
            var previous = client; client = null; previous?.Dispose();
            // Stopping the worker does not sign out the persisted ChatGPT account.
            ImageGenerationAvailable = false;
            Status = "组件已停止，连接时自动启动";
            ClearLogin();
            EditorEvent.Invoke(Changed);
        }
        private static void ClearLogin() { LoginId = null; LoginUrl = null; DeviceCode = null; }
        private static void Shutdown() { lifetime.Cancel(); operation?.Cancel(); Stop(); }

        // This is an owned, isolated CODEX_HOME. Never rewrite the user's global config.
        // Keep Code Mode host enabled: code_mode_only models need it to expose image tools,
        // even when shell execution and external plugins are disabled.
        internal const string ManagedConfig = "cli_auth_credentials_store = \"auto\"\n" +
            "forced_login_method = \"chatgpt\"\nweb_search = \"disabled\"\napproval_policy = \"never\"\nsandbox_mode = \"read-only\"\n" +
            "[features]\nimage_generation = true\nshell_tool = false\nunified_exec = false\ncode_mode_host = true\n" +
            "apps = false\nplugins = false\nremote_plugin = false\nmulti_agent = false\nhooks = false\n" +
            "browser_use = false\ncomputer_use = false\nmemories = false\n" +
            "[analytics]\nenabled = false\n";
    }
}
