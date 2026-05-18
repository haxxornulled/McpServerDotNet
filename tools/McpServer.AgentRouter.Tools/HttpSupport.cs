using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace McpServer.AgentRouter.Tools;

internal static class HttpClientFactory
{
    public static HttpClient Create(int timeoutSeconds)
    {
        return new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };
    }
}

internal static class HttpJson
{
    private static readonly JsonSerializerOptions SerializerOptions = JsonOptions.CreateCompact();

    public static async Task<HttpJsonResponse> GetAsync(HttpClient httpClient, Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        return await SendAsync(httpClient, request, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<HttpJsonResponse> PostAsync(HttpClient httpClient, Uri uri, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);
        var json = JsonSerializer.Serialize(body ?? new { }, SerializerOptions);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return await SendAsync(httpClient, request, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<HttpJsonResponse> SendAsync(HttpClient httpClient, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var json = TryParseJson(raw);
            return new HttpJsonResponse((int)response.StatusCode, response.IsSuccessStatusCode, raw, json, response.ReasonPhrase);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HttpJsonResponse.TransportFailure("Request timed out before the AgentRouter response was received.");
        }
        catch (HttpRequestException exception)
        {
            return HttpJsonResponse.TransportFailure(exception.Message);
        }
    }

    private static JsonNode? TryParseJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(raw);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal static class JsonFieldReader
{
    public static string? GetString(JsonNode? node, params object[] path)
    {
        var current = Traverse(node, path);
        return current?.GetValue<string>();
    }

    public static int? GetInt32(JsonNode? node, params object[] path)
    {
        var current = Traverse(node, path);
        if (current is null)
        {
            return null;
        }

        if (current.GetValueKind() == JsonValueKind.Number && current.GetValue<int>() is var number)
        {
            return number;
        }

        return int.TryParse(current.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    public static int? GetArrayCount(JsonNode? node, params object[] path)
    {
        var current = Traverse(node, path);
        return current is JsonArray array ? array.Count : null;
    }

    public static bool? GetBoolean(JsonNode? node, params object[] path)
    {
        var current = Traverse(node, path);
        if (current is null)
        {
            return null;
        }

        if (current.GetValueKind() == JsonValueKind.True)
        {
            return true;
        }

        if (current.GetValueKind() == JsonValueKind.False)
        {
            return false;
        }

        return bool.TryParse(current.ToString(), out var parsed)
            ? parsed
            : null;
    }

    private static JsonNode? Traverse(JsonNode? node, IReadOnlyList<object> path)
    {
        var current = node;

        foreach (var segment in path)
        {
            if (current is null)
            {
                return null;
            }

            current = segment switch
            {
                string propertyName when current is JsonObject jsonObject => jsonObject[propertyName],
                int index when current is JsonArray jsonArray && index >= 0 && index < jsonArray.Count => jsonArray[index],
                _ => null
            };
        }

        return current;
    }
}

internal sealed class HttpJsonResponse
{
    public HttpJsonResponse(int statusCode, bool success, string rawBody, JsonNode? json, string? reasonPhrase)
    {
        StatusCode = statusCode;
        Success = success;
        RawBody = rawBody;
        Json = json;
        ReasonPhrase = reasonPhrase;
    }

    public int StatusCode { get; }

    public bool Success { get; }

    public string RawBody { get; }

    public JsonNode? Json { get; }

    public string? ReasonPhrase { get; }

    public string ErrorMessage => string.IsNullOrWhiteSpace(RawBody)
        ? ReasonPhrase ?? $"HTTP {StatusCode.ToString(CultureInfo.InvariantCulture)}"
        : RawBody;

    public static HttpJsonResponse TransportFailure(string message)
    {
        return new HttpJsonResponse(0, false, string.Empty, null, message);
    }
}
