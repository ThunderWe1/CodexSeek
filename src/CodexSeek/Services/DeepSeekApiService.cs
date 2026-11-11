using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CodexSeek.Services;

public sealed record ApiTestResult(bool Success, int? StatusCode, int ModelCount, string Message);

/// <summary>
/// Optional connectivity check for a DeepSeek API key.
/// </summary>
public static class DeepSeekApiService
{
    public static async Task<ApiTestResult> TestAsync(
        string apiKey,
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ApiTestResult(false, null, 0, "API Key 为空。");
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
            !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return new ApiTestResult(false, null, 0, "DeepSeek API 地址不是 HTTPS，已取消测试。");
        }

        var url = baseUrl.TrimEnd('/') + "/models";

        using var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
            AutomaticDecompression = DecompressionMethods.All
        };
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        try
        {
            using var response = await client
                .GetAsync(url, cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var text = await response.Content
                    .ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);

                var count = 0;
                try
                {
                    using var json = JsonDocument.Parse(text);
                    if (json.RootElement.TryGetProperty("data", out var data) &&
                        data.ValueKind == JsonValueKind.Array)
                    {
                        count = data.GetArrayLength();
                    }
                }
                catch
                {
                    // A successful HTTP status is enough; the payload is only used for a friendlier message.
                }

                return new ApiTestResult(
                    true,
                    (int)response.StatusCode,
                    count,
                    count > 0
                        ? $"连接成功，API Key 有效（发现 {count} 个模型）。"
                        : "连接成功，API Key 有效。");
            }

            var statusCode = (int)response.StatusCode;
            var message = response.StatusCode == HttpStatusCode.Unauthorized
                ? "API Key 无效或已过期（HTTP 401）。"
                : $"请求失败（HTTP {statusCode}）。";
            return new ApiTestResult(false, statusCode, 0, message);
        }
        catch (TaskCanceledException)
        {
            return new ApiTestResult(false, null, 0, "连接 DeepSeek API 超时（10 秒）。");
        }
        catch (HttpRequestException exception)
        {
            return new ApiTestResult(false, null, 0, "无法连接 DeepSeek API：" + exception.Message);
        }
        catch (Exception exception)
        {
            return new ApiTestResult(false, null, 0, "测试连接失败：" + exception.Message);
        }
    }
}
