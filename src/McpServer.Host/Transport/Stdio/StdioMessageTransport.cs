using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using McpServer.Protocol.JsonRpc;

namespace McpServer.Host.Transport.Stdio;

public sealed class StdioMessageTransport : IAsyncDisposable
{
    private const int MaxHeaderBytes = 64 * 1024;
    private const int MaxContentLengthBytes = 64 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly Stream _inputStream;
    private readonly Stream _outputStream;
    private readonly ILogger<StdioMessageTransport> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private int _nextRequestId = 1000;
    private int _writeFramingMode;

    public StdioMessageTransport(
        Stream inputStream,
        Stream outputStream,
        ILogger<StdioMessageTransport> logger)
    {
        _inputStream = inputStream ?? throw new ArgumentNullException(nameof(inputStream));
        _outputStream = outputStream ?? throw new ArgumentNullException(nameof(outputStream));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private enum FramingMode
    {
        Unknown = 0,
        NewlineDelimited = 1,
        ContentLength = 2
    }

    public async ValueTask<ReadRequestResult> ReadRequestAsync(CancellationToken cancellationToken)
    {
        ReadMessageTextResult readResult = await ReadMessageTextAsync(cancellationToken).ConfigureAwait(false);
        if (readResult.ReachedEndOfStream)
        {
            return ReadRequestResult.EndOfStreamResult;
        }

        if (string.IsNullOrWhiteSpace(readResult.MessageText))
        {
            return ReadRequestResult.EmptyMessage;
        }

        try
        {
            return new ReadRequestResult(
                JsonSerializer.Deserialize<JsonRpcRequest>(readResult.MessageText, JsonOptions),
                EndOfStream: false);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Failed to deserialize JSON-RPC request message");
            return ReadRequestResult.EmptyMessage;
        }
    }

    public ValueTask WriteResponseAsync(JsonRpcResponse response, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        string json = JsonSerializer.Serialize(response, JsonOptions);
        return WriteJsonMessageAsync(json, cancellationToken);
    }

    public ValueTask WriteNotificationAsync(JsonRpcNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        string json = JsonSerializer.Serialize(notification, JsonOptions);
        return WriteJsonMessageAsync(json, cancellationToken);
    }

    public async ValueTask<JsonRpcResponse?> SendRequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        int requestId = Interlocked.Increment(ref _nextRequestId);
        var request = new JsonRpcRequest(
            "2.0",
            JsonSerializer.SerializeToElement(requestId, JsonOptions),
            method,
            parameters is null ? null : JsonSerializer.SerializeToElement(parameters, JsonOptions));

        string json = JsonSerializer.Serialize(request, JsonOptions);
        await WriteJsonMessageAsync(json, cancellationToken).ConfigureAwait(false);

        while (true)
        {
            ReadMessageTextResult readResult = await ReadMessageTextAsync(cancellationToken).ConfigureAwait(false);
            if (readResult.ReachedEndOfStream || string.IsNullOrWhiteSpace(readResult.MessageText))
            {
                return null;
            }

            try
            {
                JsonRpcResponse? response = JsonSerializer.Deserialize<JsonRpcResponse>(readResult.MessageText, JsonOptions);
                if (response?.Id is not { } responseId || responseId.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                if (responseId.GetInt32() == requestId)
                {
                    return response;
                }
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                _logger.LogWarning(ex, "Failed to deserialize JSON-RPC response message");
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _writeLock.Dispose();
        return ValueTask.CompletedTask;
    }

    private async ValueTask<ReadMessageTextResult> ReadMessageTextAsync(CancellationToken cancellationToken)
    {
        string? firstLine = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (firstLine is null)
        {
            return ReadMessageTextResult.EndOfStreamResult;
        }

        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return ReadMessageTextResult.Empty;
        }

        if (LooksLikeJson(firstLine))
        {
            SetWriteFramingMode(FramingMode.NewlineDelimited);
            return new ReadMessageTextResult(firstLine, ReachedEndOfStream: false);
        }

        if (LooksLikeHeaderLine(firstLine))
        {
            return await ReadContentLengthFramedMessageAsync(firstLine, cancellationToken).ConfigureAwait(false);
        }

        SetWriteFramingMode(FramingMode.NewlineDelimited);
        return new ReadMessageTextResult(firstLine, ReachedEndOfStream: false);
    }

    private async ValueTask<ReadMessageTextResult> ReadContentLengthFramedMessageAsync(
        string firstHeaderLine,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> headers = await ReadHeadersAsync(firstHeaderLine, cancellationToken).ConfigureAwait(false);
        if (!headers.TryGetValue("Content-Length", out string? contentLengthValue))
        {
            _logger.LogWarning("Received framed stdio headers without Content-Length");
            return ReadMessageTextResult.Empty;
        }

        if (!int.TryParse(contentLengthValue, NumberStyles.None, CultureInfo.InvariantCulture, out int contentLength) ||
            contentLength <= 0 ||
            contentLength > MaxContentLengthBytes)
        {
            _logger.LogWarning(
                "Received invalid stdio Content-Length value {ContentLength}",
                contentLengthValue);
            return ReadMessageTextResult.Empty;
        }

        byte[] body = await ReadExactBytesAsync(contentLength, cancellationToken).ConfigureAwait(false);
        if (body.Length != contentLength)
        {
            _logger.LogWarning(
                "Unexpected end of stdio stream while reading framed message body. Expected {ExpectedBytes} bytes, read {ActualBytes} bytes",
                contentLength,
                body.Length);
            return ReadMessageTextResult.EndOfStreamResult;
        }

        SetWriteFramingMode(FramingMode.ContentLength);
        return new ReadMessageTextResult(Encoding.UTF8.GetString(body), ReachedEndOfStream: false);
    }

    private async ValueTask<Dictionary<string, string>> ReadHeadersAsync(
        string firstHeaderLine,
        CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int totalHeaderBytes = Encoding.UTF8.GetByteCount(firstHeaderLine);

        AddHeader(headers, firstHeaderLine);

        while (true)
        {
            string? line = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            totalHeaderBytes += Encoding.UTF8.GetByteCount(line);
            if (totalHeaderBytes > MaxHeaderBytes)
            {
                _logger.LogWarning("Received stdio header block larger than {MaxHeaderBytes} bytes", MaxHeaderBytes);
                break;
            }

            if (line.Length == 0)
            {
                break;
            }

            AddHeader(headers, line);
        }

        return headers;
    }

    private async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream(capacity: 256);
        var oneByte = new byte[1];
        int bytesReadForLine = 0;

        while (true)
        {
            int read = await _inputStream.ReadAsync(oneByte.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (buffer.Length == 0)
                {
                    return null;
                }

                break;
            }

            byte value = oneByte[0];
            if (value == '\n')
            {
                break;
            }

            bytesReadForLine++;
            if (bytesReadForLine > MaxHeaderBytes)
            {
                _logger.LogWarning("Received stdio line larger than {MaxHeaderBytes} bytes", MaxHeaderBytes);
                return null;
            }

            buffer.WriteByte(value);
        }

        byte[] lineBytes = buffer.ToArray();
        if (lineBytes.Length > 0 && lineBytes[^1] == '\r')
        {
            Array.Resize(ref lineBytes, lineBytes.Length - 1);
        }

        return Encoding.UTF8.GetString(lineBytes);
    }

    private async ValueTask<byte[]> ReadExactBytesAsync(int length, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[length];
        int offset = 0;

        while (offset < length)
        {
            int read = await _inputStream.ReadAsync(
                buffer.AsMemory(offset, length - offset),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        if (offset == length)
        {
            return buffer;
        }

        Array.Resize(ref buffer, offset);
        return buffer;
    }

    private async ValueTask WriteJsonMessageAsync(string json, CancellationToken cancellationToken)
    {
        if (GetWriteFramingMode() == FramingMode.ContentLength)
        {
            await WriteContentLengthFramedMessageAsync(json, cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteNewlineDelimitedMessageAsync(json, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteNewlineDelimitedMessageAsync(string json, CancellationToken cancellationToken)
    {
        if (json.Contains("\n", StringComparison.Ordinal) || json.Contains("\r", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Serialized stdio MCP message must not contain embedded newlines.");
        }

        byte[] payload = Encoding.UTF8.GetBytes(json + "\n");

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _outputStream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await _outputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async ValueTask WriteContentLengthFramedMessageAsync(string json, CancellationToken cancellationToken)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);
        string header = FormattableString.Invariant($"Content-Length: {body.Length}\r\n\r\n");
        byte[] headerBytes = Encoding.ASCII.GetBytes(header);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _outputStream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
            await _outputStream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
            await _outputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static void AddHeader(IDictionary<string, string> headers, string line)
    {
        int separatorIndex = line.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex <= 0)
        {
            return;
        }

        string name = line[..separatorIndex].Trim();
        string value = line[(separatorIndex + 1)..].Trim();
        if (name.Length == 0)
        {
            return;
        }

        headers[name] = value;
    }

    private static bool LooksLikeJson(string line)
    {
        string trimmed = line.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }

    private static bool LooksLikeHeaderLine(string line)
    {
        return line.IndexOf(':', StringComparison.Ordinal) > 0;
    }

    private void SetWriteFramingMode(FramingMode framingMode)
    {
        int requestedMode = (int)framingMode;
        int existingMode = Interlocked.CompareExchange(
            ref _writeFramingMode,
            requestedMode,
            (int)FramingMode.Unknown);

        if (existingMode != (int)FramingMode.Unknown && existingMode != requestedMode)
        {
            _logger.LogDebug(
                "Received stdio message with {RequestedFramingMode} framing while output framing is already {ExistingFramingMode}",
                framingMode,
                (FramingMode)existingMode);
        }
    }

    private FramingMode GetWriteFramingMode()
    {
        return (FramingMode)Volatile.Read(ref _writeFramingMode);
    }

    public readonly record struct ReadRequestResult(JsonRpcRequest? Request, bool EndOfStream)
    {
        public static ReadRequestResult EmptyMessage => new(null, EndOfStream: false);
        public static ReadRequestResult EndOfStreamResult => new(null, EndOfStream: true);
    }

    private readonly record struct ReadMessageTextResult(string? MessageText, bool ReachedEndOfStream)
    {
        public static ReadMessageTextResult Empty => new(null, ReachedEndOfStream: false);
        public static ReadMessageTextResult EndOfStreamResult => new(null, ReachedEndOfStream: true);
    }
}
