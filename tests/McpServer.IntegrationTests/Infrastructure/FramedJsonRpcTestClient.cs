using System.Text;
using System.Text.Json;

namespace McpServer.IntegrationTests.Infrastructure;

public sealed class FramedJsonRpcTestClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Stream _input;
    private readonly Stream _output;

    public FramedJsonRpcTestClient(Stream input, Stream output)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    public async Task<JsonDocument?> SendRequestAsync(object payload, CancellationToken ct = default)
    {
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        await WriteFrameAsync(json, ct).ConfigureAwait(false);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

        while (true)
        {
            string? message = await ReadMessageAsync(timeoutCts.Token).ConfigureAwait(false);
            if (message is null)
            {
                return null;
            }

            JsonDocument document = JsonDocument.Parse(message);
            if (document.RootElement.TryGetProperty("id", out _))
            {
                return document;
            }

            document.Dispose();
        }
    }

    public async Task SendNotificationAsync(object payload, CancellationToken ct = default)
    {
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        await WriteFrameAsync(json, ct).ConfigureAwait(false);
        await Task.Delay(25, ct).ConfigureAwait(false);
    }

    private async Task WriteFrameAsync(string json, CancellationToken ct)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");

        await _input.WriteAsync(header, ct).ConfigureAwait(false);
        await _input.WriteAsync(body, ct).ConfigureAwait(false);
        await _input.FlushAsync(ct).ConfigureAwait(false);
    }

    private async Task<string?> ReadMessageAsync(CancellationToken ct)
    {
        string? firstLine = await ReadLineAsync(ct).ConfigureAwait(false);
        if (firstLine is null)
        {
            return null;
        }

        if (firstLine.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
        {
            int contentLength = int.Parse(firstLine["Content-Length:".Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture);

            while (true)
            {
                string? line = await ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null || line.Length == 0)
                {
                    break;
                }
            }

            byte[] body = await ReadExactBytesAsync(contentLength, ct).ConfigureAwait(false);
            return Encoding.UTF8.GetString(body);
        }

        return firstLine;
    }

    private async Task<string?> ReadLineAsync(CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var oneByte = new byte[1];

        while (true)
        {
            int read = await _output.ReadAsync(oneByte.AsMemory(0, 1), ct).ConfigureAwait(false);
            if (read == 0)
            {
                return buffer.Length == 0 ? null : Encoding.UTF8.GetString(buffer.ToArray());
            }

            if (oneByte[0] == '\n')
            {
                break;
            }

            buffer.WriteByte(oneByte[0]);
        }

        byte[] lineBytes = buffer.ToArray();
        if (lineBytes.Length > 0 && lineBytes[^1] == '\r')
        {
            Array.Resize(ref lineBytes, lineBytes.Length - 1);
        }

        return Encoding.UTF8.GetString(lineBytes);
    }

    private async Task<byte[]> ReadExactBytesAsync(int length, CancellationToken ct)
    {
        byte[] buffer = new byte[length];
        int offset = 0;

        while (offset < length)
        {
            int read = await _output.ReadAsync(buffer.AsMemory(offset, length - offset), ct).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Server closed stdout while reading framed response body.");
            }

            offset += read;
        }

        return buffer;
    }
}
