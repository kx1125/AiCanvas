using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Tsukuyomi.AiCanvas.Editor
{
    /// <summary>Images API adapter. Receives frozen PNGs and returns ordered PNG buffers.</summary>
    public sealed class OpenAIImageProvider : IImageProvider
    {
        public const string Model = "gpt-image-2";
        private static readonly HttpClient SharedClient = CreateClient();
        private readonly OpenAIImageOptions options;
        public string Name => "OpenAI / " + Model;

        public OpenAIImageProvider() { options = OpenAIImageSettings.Capture(); }

        private static HttpClient CreateClient()
        {
            var result = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                Timeout = Timeout.InfiniteTimeSpan,
                MaxResponseContentBufferSize = 512L * 1024 * 1024
            };
            AssemblyReloadEvents.beforeAssemblyReload += result.Dispose;
            EditorApplication.quitting += result.Dispose;
            return result;
        }

        public async Task<IReadOnlyList<byte[]>> GenerateAsync(ImageRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request == null) throw new ArgumentNullException(nameof(request));
            options.ValidateCredentials();
            request.Settings.Validate();
            if (string.IsNullOrWhiteSpace(request.Prompt) || request.Prompt.Length > 32000)
                throw new InvalidOperationException("最终 Prompt 必须为 1–32000 个字符（包含每张输入图片的说明）。");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.Timeout);
            using var message = new HttpRequestMessage(HttpMethod.Post,
                new Uri(options.BaseUri, request.References.Count == 0 ? "images/generations" : "images/edits"));
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
            message.Content = BuildContent(request);
            try
            {
                // Buffering is covered by the cancellation/timeout token as well as the upload.
                using var response = await SharedClient.SendAsync(message, HttpCompletionOption.ResponseContentRead, timeout.Token).ConfigureAwait(false);
                string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                timeout.Token.ThrowIfCancellationRequested();
                string requestId = response.Headers.TryGetValues("x-request-id", out var ids) ? ids.FirstOrDefault() : "";
                if (!response.IsSuccessStatusCode) throw CreateApiError(response, body, requestId);
                return DecodeResponse(body, request.Count, requestId, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"GPT Image 2 请求超过 {options.Timeout.TotalSeconds:0} 秒或连接超时。服务端可能仍在生成；本次未自动重试，请确认后再运行。");
            }
            catch (HttpRequestException)
            {
                // Do not retain transport exceptions that could contain request headers/body.
                throw new InvalidOperationException("无法连接图片服务。请检查 Base URL、网络或代理设置；本次未自动重试。");
            }
        }

        private static HttpContent BuildContent(ImageRequest request)
        {
            if (request.References.Count == 0)
                return new StringContent(JsonUtility.ToJson(new GenerationBody
                {
                    model = Model, prompt = request.Prompt, size = request.Size,
                    quality = request.Quality, n = request.Count, output_format = "png"
                }), Encoding.UTF8, "application/json");

            var form = new MultipartFormDataContent();
            try
            {
                form.Add(new StringContent(Model), "model");
                form.Add(new StringContent(request.Prompt, Encoding.UTF8), "prompt");
                form.Add(new StringContent(request.Size), "size");
                form.Add(new StringContent(request.Quality), "quality");
                form.Add(new StringContent(request.Count.ToString(CultureInfo.InvariantCulture)), "n");
                form.Add(new StringContent("png"), "output_format");
                for (int index = 0; index < request.References.Count; index++)
                    AddPng(form, request.References[index], "image[]", "image_" + (index + 1) + ".png", 50 * 1024 * 1024);
                if (request.Mask != null) AddPng(form, request.Mask, "mask", "mask.png", 4 * 1024 * 1024);
                // GPT Image 2 always uses high input fidelity; input_fidelity and response_format are omitted.
                return form;
            }
            catch { form.Dispose(); throw; }
        }

        private static void AddPng(MultipartFormDataContent form, ImageInputSnapshot image, string field, string filename, int limit)
        {
            byte[] png = image.CopyPngBytes();
            if (png.Length >= limit)
                throw new InvalidOperationException($"{filename} 的 PNG 快照必须小于 {limit / (1024 * 1024)} MB。");
            var content = new ByteArrayContent(png);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(content, field, filename);
        }

        private IReadOnlyList<byte[]> DecodeResponse(string body, int count, string requestId, CancellationToken token)
        {
            ImageResponse response;
            try { response = JsonUtility.FromJson<ImageResponse>(body); }
            catch (Exception) { throw InvalidResponse("响应不是有效的 JSON", requestId); }
            if (response?.data == null || response.data.Length != count)
                throw InvalidResponse($"预期 {count} 张图片，实际收到 {response?.data?.Length ?? 0} 张", requestId);
            var images = new List<byte[]>(count);
            foreach (var item in response.data)
            {
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(item?.b64_json)) throw InvalidResponse("缺少 b64_json 图片数据", requestId);
                byte[] png;
                try { png = Convert.FromBase64String(item.b64_json); }
                catch (FormatException) { throw InvalidResponse("图片 Base64 数据无效", requestId); }
                if (png.Length < 33 || png[0] != 137 || png[1] != 80 || png[2] != 78 || png[3] != 71 ||
                    png[4] != 13 || png[5] != 10 || png[6] != 26 || png[7] != 10)
                    throw InvalidResponse("返回的图片不是 PNG", requestId);
                images.Add(png);
            }
            return images.AsReadOnly();
        }

        private Exception InvalidResponse(string detail, string requestId)
            => new InvalidOperationException("GPT Image 2：" + detail + RequestIdSuffix(requestId));

        private Exception CreateApiError(HttpResponseMessage response, string body, string requestId)
        {
            ApiError error = null;
            try { error = JsonUtility.FromJson<ErrorResponse>(body)?.error; } catch (Exception) { }
            int status = (int)response.StatusCode;
            string hint = status switch
            {
                401 => "API Key 无效或已过期，请检查凭证。",
                403 => "账号或项目没有访问权限，请检查模型权限及组织验证状态。",
                429 => "达到速率限制或额度不足，请检查用量后再运行。",
                >= 500 => "图片服务暂时不可用，请稍后再运行。",
                _ => "图片请求失败，请检查参数和服务地址。"
            };
            string retry = response.Headers.RetryAfter?.ToString();
            return new InvalidOperationException($"GPT Image 2 HTTP {status}：{hint}" +
                (string.IsNullOrWhiteSpace(error?.message) ? "" : " " + Sanitize(error.message)) +
                (string.IsNullOrWhiteSpace(retry) ? "" : " Retry-After: " + Sanitize(retry)) +
                RequestIdSuffix(requestId));
        }

        private string RequestIdSuffix(string requestId)
            => string.IsNullOrWhiteSpace(requestId) ? "" : " (Request ID: " + Sanitize(requestId) + ")";

        private string Sanitize(string value)
        {
            string safe = value.Replace(options.ApiKey, "[REDACTED]");
            safe = Regex.Replace(safe, @"sk-[A-Za-z0-9_-]+", "[REDACTED]");
            safe = Regex.Replace(safe, @"[\r\n\t]+", " ");
            return safe.Length > 600 ? safe.Substring(0, 600) + "…" : safe;
        }

        [Serializable] private sealed class GenerationBody
        {
            public string model, prompt, size, quality, output_format;
            public int n;
        }

        #pragma warning disable CS0649 // Populated by the API JSON response.
        [Serializable] private sealed class ImageResponse { public ImageData[] data; }
        [Serializable] private sealed class ImageData { public string b64_json; }
        [Serializable] private sealed class ErrorResponse { public ApiError error; }
        [Serializable] private sealed class ApiError { public string message; }
        #pragma warning restore CS0649
    }
}
