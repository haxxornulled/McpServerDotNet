using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace McpServer.IntegrationTests.Infrastructure;

public sealed class FakeOllamaServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _runTask;
    private readonly IReadOnlyList<string> _availableModels;
    private readonly string _chatResponseText;
    private readonly ConcurrentBag<string> _requestMethods = new();
    private readonly ConcurrentBag<string> _requestPaths = new();
    private readonly ConcurrentBag<string> _requestBodies = new();

    private FakeOllamaServer(
        TcpListener listener,
        Uri baseAddress,
        IReadOnlyList<string> availableModels,
        string chatResponseText)
    {
        _listener = listener;
        BaseAddress = baseAddress;
        _availableModels = availableModels;
        _chatResponseText = chatResponseText;
        _runTask = Task.Run(() => RunAsync(_cts.Token));
    }

    public Uri BaseAddress { get; }

    public IReadOnlyCollection<string> RequestMethods => _requestMethods.ToArray();

    public IReadOnlyCollection<string> RequestPaths => _requestPaths.ToArray();

    public IReadOnlyCollection<string> RequestBodies => _requestBodies.ToArray();

    public static Task<FakeOllamaServer> StartAsync(
        IReadOnlyList<string>? availableModels = null,
        string chatResponseText = "ok")
    {
        var port = GetFreeTcpPort();
        var listener = new TcpListener(System.Net.IPAddress.Loopback, port);
        listener.Start();

        var server = new FakeOllamaServer(
            listener,
            new Uri($"http://127.0.0.1:{port}/"),
            availableModels ?? new[] { "qwen3-coder:30b" },
            chatResponseText);

        return Task.FromResult(server);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _cts.Cancel();
            _listener.Stop();
            await _runTask.ConfigureAwait(false);
        }
        catch
        {
        }
        finally
        {
            _cts.Dispose();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var handlers = new List<Task>();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                handlers.Add(HandleClientAsync(client, cancellationToken));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }

        try
        {
            await Task.WhenAll(handlers).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = client.GetStream();

            var requestLine = await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false);
            if (requestLine is null)
            {
                return;
            }

            var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                _requestMethods.Add(parts[0]);
                _requestPaths.Add(parts[1]);

                var method = parts[0];
                var path = parts[1];

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                while (true)
                {
                    var line = await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false);
                    if (line is null || line.Length == 0)
                    {
                        break;
                    }

                    var colonIndex = line.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        headers[line[..colonIndex].Trim()] = line[(colonIndex + 1)..].Trim();
                    }
                }

                var requestBody = string.Empty;
                if (headers.TryGetValue("Content-Length", out var contentLengthText) &&
                    int.TryParse(contentLengthText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var contentLength) &&
                    contentLength > 0)
                {
                    var bodyBytes = await ReadExactBytesAsync(stream, contentLength, cancellationToken).ConfigureAwait(false);
                    requestBody = Encoding.UTF8.GetString(bodyBytes);
                    _requestBodies.Add(requestBody);
                }

                if (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(path, "/api/tags", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteJsonResponseAsync(stream, new
                    {
                        models = _availableModels.Select(model => new { name = model }).ToArray()
                    }, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(path, "/api/chat", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteJsonResponseAsync(stream, new
                    {
                        message = new
                        {
                            role = "assistant",
                            content = _chatResponseText
                        }
                    }, cancellationToken).ConfigureAwait(false);
                    return;
                }

                await WriteJsonResponseAsync(stream, new { error = "not found" }, cancellationToken, statusCode: 404)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
        finally
        {
            client.Dispose();
        }
    }

    private static async Task WriteJsonResponseAsync(
        Stream stream,
        object payload,
        CancellationToken cancellationToken,
        int statusCode = 200)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var bodyBytes = Encoding.UTF8.GetBytes(json);
        var headerBytes = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {statusCode} {(statusCode == 200 ? "OK" : "Not Found")}\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Connection: close\r\n\r\n");

        await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bodyBytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var oneByte = new byte[1];

        while (true)
        {
            var read = await stream.ReadAsync(oneByte.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
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

        var lineBytes = buffer.ToArray();
        if (lineBytes.Length > 0 && lineBytes[^1] == '\r')
        {
            Array.Resize(ref lineBytes, lineBytes.Length - 1);
        }

        return Encoding.UTF8.GetString(lineBytes);
    }

    private static async Task<byte[]> ReadExactBytesAsync(
        Stream stream,
        int length,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;

        while (offset < length)
        {
            var read = await stream.ReadAsync(
                    buffer.AsMemory(offset, length - offset),
                    cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                throw new EndOfStreamException("Ollama server closed the connection while reading the request body.");
            }

            offset += read;
        }

        return buffer;
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
