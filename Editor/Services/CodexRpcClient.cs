using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Tsukuyomi.AiCanvas.Editor
{
    // One reader owns stdout. Responses can arrive out of order; notifications may
    // arrive before turn/start returns. Never block Unity's synchronization context.
    internal sealed class CodexRpcClient : IDisposable
    {
        private readonly Process process;
        private readonly WindowsProcessJob job;
        private readonly SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JToken>> pending = new ConcurrentDictionary<int, TaskCompletionSource<JToken>>();
        private int nextId;
        private int disposed;
        internal bool IsAlive => disposed == 0 && !process.HasExited;
        public event Action<string, JObject> Notification;
        public event Action<Exception> Disconnected;

        private CodexRpcClient(Process process, WindowsProcessJob job) { this.process = process; this.job = job; }

        internal static async Task<CodexRpcClient> StartAsync(string executable, string home, string workingDirectory, CancellationToken token)
        {
            Directory.CreateDirectory(home);
            Directory.CreateDirectory(workingDirectory);
            var info = new ProcessStartInfo(executable, "app-server --stdio")
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false),
                WorkingDirectory = workingDirectory
            };
            // Do not inherit desktop session IDs, credentials, endpoints, or state paths.
            foreach (string name in info.EnvironmentVariables.Keys.Cast<string>().ToArray())
                if ((name.StartsWith("CODEX_", StringComparison.OrdinalIgnoreCase) && name != "CODEX_CA_CERTIFICATE") ||
                    name == "OPENAI_API_KEY" || name == "OPENAI_BASE_URL" || name == "OPENAI_FEDERATION_RULE_ID" ||
                    name == "OPENAI_IDENTITY_TOKEN_FILE" || name == "OPENAI_WORKLOAD_IDENTITY_CONTEXT")
                    info.EnvironmentVariables.Remove(name);
            info.EnvironmentVariables["CODEX_HOME"] = home;
            info.EnvironmentVariables["RUST_LOG"] = "error";
            var process = new Process { StartInfo = info };
            WindowsProcessJob job = null;
            CodexRpcClient client = null;
            try
            {
                token.ThrowIfCancellationRequested();
                process.Start();
                job = new WindowsProcessJob(process);
                client = new CodexRpcClient(process, job);
                _ = Task.Run(client.ReadAsync);
                _ = Task.Run(async () => { try { while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) != null) { } } catch { } });
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(30));
                    await client.RequestAsync("initialize", new
                    {
                        clientInfo = new { name = "ai_canvas", title = "Unity AiCanvas", version = "1.0.0" },
                        capabilities = new { experimentalApi = false }
                    }, timeout.Token);
                    await client.WriteAsync(new { method = "initialized" }, timeout.Token);
                }
                return client;
            }
            catch
            {
                if (client != null) client.Dispose();
                else { job?.Dispose(); try { if (!process.HasExited) process.Kill(); } catch { } process.Dispose(); }
                throw;
            }
        }

        public async Task<JToken> RequestAsync(string method, object parameters, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (disposed != 0) throw new IOException("运行组件已停止，请重新连接。");
            int id = Interlocked.Increment(ref nextId);
            var completion = new TaskCompletionSource<JToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[id] = completion;
            using (token.Register(() => completion.TrySetCanceled()))
            {
                try
                {
                    await WriteAsync(new { id, method, @params = parameters }, token).ConfigureAwait(false);
                    return await completion.Task.ConfigureAwait(false);
                }
                finally { pending.TryRemove(id, out _); }
            }
        }

        private async Task WriteAsync(object message, CancellationToken token)
        {
            await writeLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                if (disposed != 0) throw new IOException("运行组件已停止。");
                await process.StandardInput.WriteLineAsync(JsonConvert.SerializeObject(message, Formatting.None)).ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
            }
            finally { writeLock.Release(); }
        }

        private async Task ReadAsync()
        {
            Exception failure = new IOException("运行组件意外退出，请重新连接。");
            try
            {
                string line;
                while ((line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    if (line.Length > 128 * 1024 * 1024) throw new IOException("运行组件响应超过大小上限。");
                    var message = JObject.Parse(line);
                    string method = (string)message["method"];
                    JToken id = message["id"];
                    if (method != null && id != null)
                    {
                        // Image generation requires no shell/patch approvals or external credentials.
                        // Unsupported server requests fail closed instead of hanging indefinitely.
                        await WriteAsync(new { id, error = new { code = -32601, message = "AiCanvas does not support this operation." } }, CancellationToken.None).ConfigureAwait(false);
                    }
                    else if (id != null && id.Type == JTokenType.Integer && pending.TryRemove((int)id, out var completion))
                    {
                        if (message["error"] != null) completion.TrySetException(new IOException("Codex: " + SafeMessage((string)message["error"]["message"])));
                        else completion.TrySetResult(message["result"] ?? JValue.CreateNull());
                    }
                    else if (method != null) Notification?.Invoke(method, message["params"] as JObject ?? new JObject());
                }
            }
            catch (Exception exception) { failure = new IOException("运行组件连接中断：" + SafeMessage(exception.Message)); }
            finally
            {
                foreach (var entry in pending) entry.Value.TrySetException(failure);
                pending.Clear();
                if (disposed == 0) Disconnected?.Invoke(failure);
            }
        }

        internal static string SafeMessage(string message)
        {
            string result = System.Text.RegularExpressions.Regex.Replace(message ?? "未知错误", @"(?i)(?:sk-[\w-]+|eyJ[\w.-]{30,}|bearer\s+\S+)", "[redacted]");
            return result.Length > 1200 ? result.Substring(0, 1200) : result;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            foreach (var entry in pending) entry.Value.TrySetException(new IOException("运行组件已停止。"));
            pending.Clear();
            try { process.StandardInput.Close(); } catch { }
            job.Dispose(); // Kill-on-close covers native child processes on reload/quit/crash.
            process.Dispose();
        }

        private sealed class WindowsProcessJob : IDisposable
        {
            private IntPtr handle;
            internal WindowsProcessJob(Process process)
            {
                handle = CreateJobObject(IntPtr.Zero, null);
                var limits = new ExtendedLimitInformation();
                limits.BasicLimitInformation.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                int size = Marshal.SizeOf<ExtendedLimitInformation>();
                IntPtr memory = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(limits, memory, false);
                    if (handle == IntPtr.Zero || !SetInformationJobObject(handle, 9, memory, (uint)size) ||
                        !AssignProcessToJobObject(handle, process.Handle))
                        throw new IOException("无法管理运行组件进程（Windows 错误 " + Marshal.GetLastWin32Error() + "）。");
                }
                catch { Dispose(); throw; }
                finally { Marshal.FreeHGlobal(memory); }
            }
            public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
            [StructLayout(LayoutKind.Sequential)] private struct BasicLimitInformation
            {
                public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
                public uint LimitFlags;
                public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
                public uint ActiveProcessLimit;
                public UIntPtr Affinity;
                public uint PriorityClass, SchedulingClass;
            }
            [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong A, B, C, D, E, F; }
            [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimitInformation
            {
                public BasicLimitInformation BasicLimitInformation;
                public IoCounters IoInfo;
                public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
            }
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
            [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr information, uint size);
            [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
            [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        }
    }
}
