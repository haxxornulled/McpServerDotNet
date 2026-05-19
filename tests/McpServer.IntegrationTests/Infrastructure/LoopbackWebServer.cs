using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace McpServer.IntegrationTests.Infrastructure;

public sealed class LoopbackWebServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _runTask;
    private readonly ConcurrentBag<string> _requestPaths = new();

    private LoopbackWebServer(TcpListener listener, Uri baseAddress)
    {
        _listener = listener;
        BaseAddress = baseAddress;
        _runTask = Task.Run(() => RunAsync(_cts.Token));
    }

    public Uri BaseAddress { get; }

    public string FetchUrl => new Uri(BaseAddress, "fetch").ToString();

    public string SearchBaseUrl => $"{BaseAddress}search?q=";

    public string ScrapeUrl => new Uri(BaseAddress, "scrape").ToString();

    public IReadOnlyCollection<string> RequestPaths => _requestPaths.ToArray();

    public static Task<LoopbackWebServer> StartAsync()
    {
        var port = GetFreeTcpPort();
        var listener = new TcpListener(System.Net.IPAddress.Loopback, port);
        listener.Start();

        var server = new LoopbackWebServer(listener, new Uri($"http://127.0.0.1:{port}/"));
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
            if (parts.Length < 2)
            {
                return;
            }

            var method = parts[0];
            var path = parts[1];
            _requestPaths.Add(path);

            while (true)
            {
                var line = await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false);
                if (line is null || line.Length == 0)
                {
                    break;
                }
            }

            if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                await WriteTextResponseAsync(stream, "Method not allowed", "text/plain; charset=utf-8", 405, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (path.StartsWith("/search", StringComparison.OrdinalIgnoreCase))
            {
                var query = ExtractQuery(path);
                var html = $"""
                    <html>
                      <head><title>Loopback Search</title></head>
                      <body>
                        <main>Search results for {System.Net.WebUtility.HtmlEncode(query)}</main>
                        <p>Result one for {System.Net.WebUtility.HtmlEncode(query)}</p>
                        <p>Result two for {System.Net.WebUtility.HtmlEncode(query)}</p>
                      </body>
                    </html>
                    """;

                await WriteTextResponseAsync(stream, html, contentType: "text/html; charset=utf-8", cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            if (string.Equals(path, "/scrape", StringComparison.OrdinalIgnoreCase))
            {
                var html = """
                    <html>
                      <head><title>Loopback Scrape</title></head>
                      <body>
                        <article class="card" data-id="one">
                          <h2>First Article</h2>
                          <a href="/articles/one">Read more</a>
                        </article>
                        <article class="card" data-id="two">
                          <h2>Second Article</h2>
                          <a href="/articles/two">Read more</a>
                        </article>
                      </body>
                    </html>
                    """;

                await WriteTextResponseAsync(stream, html, contentType: "text/html; charset=utf-8", cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            if (string.Equals(path, "/fetch", StringComparison.OrdinalIgnoreCase))
            {
                var html = """
                    <html>
                      <head><title>Loopback Fetch</title></head>
                      <body>
                        <main>Loopback fetch content.</main>
                      </body>
                    </html>
                    """;

                await WriteTextResponseAsync(stream, html, contentType: "text/html; charset=utf-8", cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            await WriteTextResponseAsync(stream, "Not found", "text/plain; charset=utf-8", 404, cancellationToken).ConfigureAwait(false);
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

    private static string ExtractQuery(string path)
    {
        var queryIndex = path.IndexOf('?');
        if (queryIndex < 0 || queryIndex == path.Length - 1)
        {
            return string.Empty;
        }

        var query = path[(queryIndex + 1)..];
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equalsIndex = part.IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            var key = part[..equalsIndex];
            if (string.Equals(key, "q", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(part[(equalsIndex + 1)..].Replace('+', ' '));
            }
        }

        return string.Empty;
    }

    private static async Task WriteTextResponseAsync(
        Stream stream,
        string body,
        string contentType = "text/plain; charset=utf-8",
        int statusCode = 200,
        CancellationToken cancellationToken = default)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var headerBytes = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {statusCode} {(statusCode == 200 ? "OK" : statusCode == 404 ? "Not Found" : "Error")}\r\n" +
            $"Content-Type: {contentType}\r\n" +
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

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
