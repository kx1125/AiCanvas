using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    // Complete native distribution: no dependency on npm, Node, PATH, or a desktop app.
    internal static class CodexRuntime
    {
        internal const string Version = "0.154.0";
        internal const string Target = "x86_64-pc-windows-msvc";
        internal const string DownloadUrl = "https://registry.npmjs.org/@openai/codex/-/codex-0.154.0-win32-x64.tgz";
        internal const string ArchiveSha512 = "Stg2KEJPIKVqPPR1wCverGOR4ey3RR3cvakR07w7FNKQUMzmHaOZomRsP2bR1qOT/67yHsks9rB+MCMfIWXcRA==";
        internal static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/AiCanvas/Codex"));
        // System credential stores may key account entries by the canonical home path.
        // Keep an existing account's identity stable when upgrading the package name.
        internal static string Home
        {
            get
            {
                string previous = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/ImageCanvas/Codex/home"));
                return Directory.Exists(previous) ? previous : Path.Combine(Root, "home");
            }
        }
        internal static string InstallDirectory => Path.Combine(Root, "runtime", Version);
        internal static string Executable => Path.Combine(InstallDirectory, "package/vendor/" + Target + "/bin/codex.exe");
        internal static bool Supported => Application.platform == RuntimePlatform.WindowsEditor &&
            System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.X64;
        internal static bool Installed => File.Exists(Executable) && File.Exists(Path.Combine(InstallDirectory, "verified.sha512")) &&
            File.ReadAllText(Path.Combine(InstallDirectory, "verified.sha512")) == ArchiveSha512 &&
            File.Exists(Path.Combine(Path.GetDirectoryName(Executable), "codex-code-mode-host.exe"));

        internal static async Task InstallAsync(IProgress<float> progress, CancellationToken cancellationToken)
        {
            if (!Supported) throw new PlatformNotSupportedException("账号运行组件目前支持 Windows x64 Editor。其他平台可使用 API Key 模式。");
            string root = Root;
            string destination = InstallDirectory;
            string stage = Path.Combine(root, "runtime", "install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            string archive = Path.Combine(stage, "runtime.tgz");
            try
            {
                using (var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }))
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(TimeSpan.FromMinutes(15));
                    using (var response = await client.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                    {
                        if (response.StatusCode != System.Net.HttpStatusCode.OK)
                            throw new IOException("运行组件下载失败：HTTP " + (int)response.StatusCode);
                        long? length = response.Content.Headers.ContentLength;
                        using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                        {
                            var buffer = new byte[81920];
                            long total = 0;
                            int read;
                            while ((read = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) > 0)
                            {
                                total += read;
                                if (total > 512L * 1024 * 1024) throw new IOException("运行组件下载超过大小上限。");
                                await output.WriteAsync(buffer, 0, read, timeout.Token).ConfigureAwait(false);
                                progress?.Report(length > 0 ? (float)total / length.Value * .8f : .1f);
                            }
                        }
                    }
                }
                await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using (var sha = SHA512.Create())
                    using (var input = File.OpenRead(archive))
                        if (Convert.ToBase64String(sha.ComputeHash(input)) != ArchiveSha512)
                            throw new IOException("运行组件完整性校验失败，请重新下载。");
                    progress?.Report(.85f);
                    using (var input = File.OpenRead(archive))
                    using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                        ExtractTar(gzip, stage, cancellationToken);
                    string vendor = Path.Combine(stage, "package/vendor/" + Target);
                    foreach (string required in new[] { "bin/codex.exe", "bin/codex-code-mode-host.exe", "codex-path/rg.exe",
                        "codex-resources/codex-command-runner.exe", "codex-resources/codex-windows-sandbox-setup.exe", "codex-package.json" })
                        if (!File.Exists(Path.Combine(vendor, required))) throw new IOException("运行组件不完整：" + required);
                    File.Delete(archive);
                    File.WriteAllText(Path.Combine(stage, "verified.sha512"), ArchiveSha512);
                    cancellationToken.ThrowIfCancellationRequested();
                    DeleteContainedDirectory(root, destination);
                    // Both paths were constructed under the managed runtime cache.
                    RequireChild(root, stage);
                    RequireChild(root, destination);
                    Directory.Move(stage, destination);
                    progress?.Report(1);
                }, cancellationToken).ConfigureAwait(false);
            }
            finally { DeleteContainedDirectory(root, stage); }
        }

        internal static string RequireChild(string root, string path)
        {
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("路径超出 AiCanvas 缓存目录。");
            // Never follow junctions/symlinks while extracting, reading results, or deleting.
            for (var entry = new DirectoryInfo(Path.GetDirectoryName(full)); entry != null; entry = entry.Parent)
            {
                if (entry.Exists && (entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("缓存路径不能包含符号链接。");
            }
            if ((File.Exists(full) || Directory.Exists(full)) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("缓存路径不能包含符号链接。");
            return full;
        }

        internal static void DeleteContainedDirectory(string root, string path)
        {
            string full = RequireChild(root, path);
            if (!Directory.Exists(full)) return;
            // Avoid recursive deletion following a link introduced after installation.
            foreach (string child in Directory.GetFileSystemEntries(full))
            {
                RequireChild(root, child);
                if (Directory.Exists(child)) DeleteContainedDirectory(root, child);
                else File.Delete(child);
            }
            Directory.Delete(full);
        }

        // npm's pinned native archive contains regular files. Reject links and special entries.
        internal static void ExtractTar(Stream input, string destination, CancellationToken token)
        {
            var header = new byte[512];
            var buffer = new byte[81920];
            long expanded = 0;
            while (true)
            {
                ReadExactly(input, header, 512);
                if (header[0] == 0) return;
                token.ThrowIfCancellationRequested();
                long checksum = ParseOctal(header, 148, 8);
                long actual = 0;
                for (int i = 0; i < 512; i++) actual += i >= 148 && i < 156 ? 32 : header[i];
                if (checksum != actual) throw new IOException("运行组件 TAR 校验失败。");
                string name = ReadText(header, 0, 100);
                string prefix = ReadText(header, 345, 155);
                if (prefix.Length > 0) name = prefix + "/" + name;
                if (!name.StartsWith("package/", StringComparison.Ordinal) || name.Contains("\\") || name.Contains(":") || name.Contains(".."))
                    throw new IOException("运行组件包含无效路径。");
                string target = RequireChild(destination, Path.Combine(destination, name));
                long length = ParseOctal(header, 124, 12);
                expanded += length;
                if (length < 0 || expanded > 1024L * 1024 * 1024) throw new IOException("运行组件解压超过大小上限。");
                byte type = header[156];
                if (type == (byte)'5' && length == 0) { Directory.CreateDirectory(target); continue; }
                if (type != 0 && type != (byte)'0') throw new IOException("运行组件包含不支持的 TAR 项。");
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
                {
                    long remaining = length;
                    while (remaining > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        int count = (int)Math.Min(buffer.Length, remaining);
                        ReadExactly(input, buffer, count);
                        output.Write(buffer, 0, count);
                        remaining -= count;
                    }
                }
                int padding = (int)((512 - length % 512) % 512);
                if (padding > 0) ReadExactly(input, buffer, padding);
            }
        }

        private static string ReadText(byte[] bytes, int offset, int length) => Encoding.UTF8.GetString(bytes, offset, length).TrimEnd('\0', ' ');
        private static long ParseOctal(byte[] bytes, int offset, int length) => Convert.ToInt64(ReadText(bytes, offset, length).Trim(), 8);
        private static void ReadExactly(Stream input, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = input.Read(buffer, offset, count - offset);
                if (read == 0) throw new EndOfStreamException("运行组件压缩包不完整。");
                offset += read;
            }
        }
    }
}
