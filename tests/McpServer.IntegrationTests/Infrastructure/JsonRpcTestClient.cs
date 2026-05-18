using System.Text;
using System.Text.Json;

namespace McpServer.IntegrationTests.Infrastructure;

public sealed class JsonRpcTestClient(StreamWriter input, StreamReader output)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<JsonDocument?> SendRequestAsync(object payload, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);

        await WriteLineAsync(json, "message").ConfigureAwait(false);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

        while (true)
        {
            var line = await output.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
            if (line is null)
            {
                return null;
            }

            var document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("id", out _))
            {
                return document;
            }
        }
    }

    public async Task SendNotificationAsync(object payload, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        await WriteLineAsync(json, "notification").ConfigureAwait(false);
        await Task.Delay(25, ct).ConfigureAwait(false);
    }

    public async Task<JsonDocument?> ReadMessageAsync(CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

        var line = await output.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
        return line is null ? null : JsonDocument.Parse(line);
    }

    public async Task SendSuccessResponseAsync(JsonElement id, object result, CancellationToken ct = default)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("jsonrpc", "2.0");
            writer.WritePropertyName("id");
            id.WriteTo(writer);
            writer.WritePropertyName("result");
            JsonSerializer.Serialize(writer, result, JsonOptions);
            writer.WriteEndObject();
        }

        var json = Encoding.UTF8.GetString(stream.ToArray());
        await WriteLineAsync(json, "response").ConfigureAwait(false);
    }

    private async Task WriteLineAsync(string json, string kind)
    {
        if (json.Contains('\n') || json.Contains('\r'))
        {
            throw new InvalidOperationException($"Test client emitted an invalid stdio MCP {kind}.");
        }

        await input.WriteLineAsync(json).ConfigureAwait(false);
        await input.FlushAsync().ConfigureAwait(false);
    }
}
