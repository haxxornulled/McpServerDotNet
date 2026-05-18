using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using McpServer.IntegrationTests.Infrastructure;
using Xunit;

namespace McpServer.IntegrationTests.Protocol;

public sealed class AgentRouterChatCompletionStreamingIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static string AgentRouterHostProjectPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "McpServer.AgentRouter.Host", "McpServer.AgentRouter.Host.csproj"));

    [Fact]
    public async Task Chat_Completions_StreamTrue_Should_Stream_Sse_Chunks_End_To_End()
    {
        await using var provider = await StreamingProviderServer.StartAsync(
                new[]
                {
                    JsonSerializer.Serialize(new
                    {
                        message = new
                        {
                            role = "assistant",
                            content = "Hel"
                        },
                        done = false
                    }, JsonOptions),
                    JsonSerializer.Serialize(new
                    {
                        message = new
                        {
                            role = "assistant",
                            content = "lo"
                        },
                        done = false
                    }, JsonOptions),
                    JsonSerializer.Serialize(new
                    {
                        message = new
                        {
                            role = "assistant",
                            content = string.Empty
                        },
                        done = true,
                        done_reason = "stop",
                        prompt_eval_count = 4,
                        eval_count = 2
                    }, JsonOptions)
                });

        var hostPort = GetFreeTcpPort();
        var hostBaseAddress = new Uri($"http://127.0.0.1:{hostPort}/");

        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["AgentRouter__Startup__Enabled"] = "false",
            ["AgentRouter__Startup__EnsureOllama"] = "false",
            ["AgentRouter__Startup__VerifyMcpToolCatalogAfterStart"] = "false",
            ["AgentRouter__DefaultProfile"] = "local-code",
            ["AgentRouter__ModelProfiles__local-code__Provider"] = "Ollama",
            ["AgentRouter__ModelProfiles__local-code__Model"] = "qwen3-coder:30b",
            ["AgentRouter__ModelProfiles__local-code__BaseUrl"] = provider.BaseAddress.GetLeftPart(UriPartial.Authority)
        };

        await using var host = await HttpTestServerProcess.StartAsync(
                AgentRouterHostProjectPath,
                hostBaseAddress,
                environment);

        using var client = new HttpClient
        {
            BaseAddress = host.BaseAddress,
            Timeout = TimeSpan.FromSeconds(30)
        };

        var requestJson = JsonSerializer.Serialize(new
        {
            model = "local-code",
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = "Say hello in one short sentence."
                }
            },
            stream = true
        }, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);

        var events = await ReadSseDataEventsAsync(response.Content);

        Assert.Equal(4, events.Count);

        using var firstChunk = JsonDocument.Parse(events[0]);
        using var secondChunk = JsonDocument.Parse(events[1]);
        using var finalChunk = JsonDocument.Parse(events[2]);

        var firstChoice = firstChunk.RootElement.GetProperty("choices").EnumerateArray().Single();
        var secondChoice = secondChunk.RootElement.GetProperty("choices").EnumerateArray().Single();
        var finalChoice = finalChunk.RootElement.GetProperty("choices").EnumerateArray().Single();

        Assert.Equal("chat.completion.chunk", firstChunk.RootElement.GetProperty("object").GetString());
        Assert.Equal("assistant", firstChoice.GetProperty("delta").GetProperty("role").GetString());
        Assert.Equal("Hel", firstChoice.GetProperty("delta").GetProperty("content").GetString());
        Assert.False(firstChunk.RootElement.TryGetProperty("usage", out _));

        Assert.Equal("lo", secondChoice.GetProperty("delta").GetProperty("content").GetString());
        Assert.False(secondChunk.RootElement.TryGetProperty("usage", out _));

        Assert.Equal("stop", finalChoice.GetProperty("finish_reason").GetString());
        Assert.False(finalChunk.RootElement.TryGetProperty("usage", out _));

        Assert.Equal("[DONE]", events[3]);
        Assert.Equal("POST", provider.RequestMethod);
        Assert.Equal("/api/chat", provider.RequestPath);
        Assert.Contains("\"stream\":true", provider.RequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"model\":\"qwen3-coder:30b\"", provider.RequestBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Chat_Completions_StreamTrue_Should_Handle_Concurrent_Requests_End_To_End()
    {
        await using var provider = await StreamingProviderServer.StartAsync(
                new[]
                {
                    JsonSerializer.Serialize(new
                    {
                        message = new
                        {
                            role = "assistant",
                            content = "Hel"
                        },
                        done = false
                    }, JsonOptions),
                    JsonSerializer.Serialize(new
                    {
                        message = new
                        {
                            role = "assistant",
                            content = "lo"
                        },
                        done = false
                    }, JsonOptions),
                    JsonSerializer.Serialize(new
                    {
                        message = new
                        {
                            role = "assistant",
                            content = string.Empty
                        },
                        done = true,
                        done_reason = "stop",
                        prompt_eval_count = 4,
                        eval_count = 2
                    }, JsonOptions)
                },
                expectedRequestCount: 2,
                interChunkDelay: TimeSpan.FromMilliseconds(40));

        var hostPort = GetFreeTcpPort();
        var hostBaseAddress = new Uri($"http://127.0.0.1:{hostPort}/");

        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["AgentRouter__Startup__Enabled"] = "false",
            ["AgentRouter__Startup__EnsureOllama"] = "false",
            ["AgentRouter__Startup__VerifyMcpToolCatalogAfterStart"] = "false",
            ["AgentRouter__DefaultProfile"] = "local-code",
            ["AgentRouter__ModelProfiles__local-code__Provider"] = "Ollama",
            ["AgentRouter__ModelProfiles__local-code__Model"] = "qwen3-coder:30b",
            ["AgentRouter__ModelProfiles__local-code__BaseUrl"] = provider.BaseAddress.GetLeftPart(UriPartial.Authority)
        };

        await using var host = await HttpTestServerProcess.StartAsync(
                AgentRouterHostProjectPath,
                hostBaseAddress,
                environment);

        using var client = new HttpClient
        {
            BaseAddress = host.BaseAddress,
            Timeout = TimeSpan.FromSeconds(30)
        };

        var firstTask = SendStreamingChatCompletionAsync(client, "Say hello in one short sentence.");
        var secondTask = SendStreamingChatCompletionAsync(client, "Say hello in one short sentence.");

        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(2, provider.RequestBodies.Count);
        Assert.True(provider.MaxConcurrentHandlers >= 2);
        Assert.All(provider.RequestBodies, body =>
        {
            Assert.Contains("\"stream\":true", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"model\":\"qwen3-coder:30b\"", body, StringComparison.OrdinalIgnoreCase);
        });

        Assert.All(results, events =>
        {
            Assert.Equal(4, events.Count);

            using var firstChunk = JsonDocument.Parse(events[0]);
            using var secondChunk = JsonDocument.Parse(events[1]);
            using var finalChunk = JsonDocument.Parse(events[2]);

            var firstChoice = firstChunk.RootElement.GetProperty("choices").EnumerateArray().Single();
            var secondChoice = secondChunk.RootElement.GetProperty("choices").EnumerateArray().Single();
            var finalChoice = finalChunk.RootElement.GetProperty("choices").EnumerateArray().Single();

            Assert.Equal("chat.completion.chunk", firstChunk.RootElement.GetProperty("object").GetString());
            Assert.Equal("assistant", firstChoice.GetProperty("delta").GetProperty("role").GetString());
            Assert.Equal("Hel", firstChoice.GetProperty("delta").GetProperty("content").GetString());
            Assert.False(firstChunk.RootElement.TryGetProperty("usage", out _));

            Assert.Equal("lo", secondChoice.GetProperty("delta").GetProperty("content").GetString());
            Assert.False(secondChunk.RootElement.TryGetProperty("usage", out _));

            Assert.Equal("stop", finalChoice.GetProperty("finish_reason").GetString());
            Assert.False(finalChunk.RootElement.TryGetProperty("usage", out _));
            Assert.Equal("[DONE]", events[3]);
        });
    }

    [Fact]
    public async Task Chat_Completions_StreamTrue_Should_Return_Http_Error_When_Provider_Fails_Before_First_Chunk()
    {
        await using var provider = await StreamingProviderServer.StartAsync(
            new[] { "{ invalid json" });

        var hostPort = GetFreeTcpPort();
        var hostBaseAddress = new Uri($"http://127.0.0.1:{hostPort}/");

        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["AgentRouter__Startup__Enabled"] = "false",
            ["AgentRouter__Startup__EnsureOllama"] = "false",
            ["AgentRouter__Startup__VerifyMcpToolCatalogAfterStart"] = "false",
            ["AgentRouter__DefaultProfile"] = "local-code",
            ["AgentRouter__ModelProfiles__local-code__Provider"] = "Ollama",
            ["AgentRouter__ModelProfiles__local-code__Model"] = "qwen3-coder:30b",
            ["AgentRouter__ModelProfiles__local-code__BaseUrl"] = provider.BaseAddress.GetLeftPart(UriPartial.Authority)
        };

        await using var host = await HttpTestServerProcess.StartAsync(
            AgentRouterHostProjectPath,
            hostBaseAddress,
            environment);

        using var client = new HttpClient
        {
            BaseAddress = host.BaseAddress,
            Timeout = TimeSpan.FromSeconds(30)
        };

        var requestJson = JsonSerializer.Serialize(new
        {
            model = "local-code",
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = "Say hello in one short sentence."
                }
            },
            stream = true
        }, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        using var errorDocument = JsonDocument.Parse(body);
        Assert.Equal("stream_error", errorDocument.RootElement.GetProperty("error").GetProperty("type").GetString());
        Assert.Equal("stream_error", errorDocument.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains(
            "invalid JSON",
            errorDocument.RootElement.GetProperty("error").GetProperty("message").GetString(),
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal("POST", provider.RequestMethods.Single());
        Assert.Equal("/api/chat", provider.RequestPaths.Single());
        Assert.Contains("\"stream\":true", provider.RequestBodies.Single(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<IReadOnlyList<string>> ReadSseDataEventsAsync(HttpContent content)
    {
        var events = new List<string>();
        await using var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (true)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                events.Add(line["data: ".Length..]);
            }
        }

        return events;
    }

    private static async Task<IReadOnlyList<string>> SendStreamingChatCompletionAsync(
        HttpClient client,
        string prompt)
    {
        var requestJson = JsonSerializer.Serialize(new
        {
            model = "local-code",
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = prompt
                }
            },
            stream = true
        }, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead)
            .ConfigureAwait(false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);

        return await ReadSseDataEventsAsync(response.Content).ConfigureAwait(false);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class StreamingProviderServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _runTask;
        private readonly IReadOnlyList<string> _responseLines;
        private readonly TimeSpan _interChunkDelay;
        private readonly int _expectedRequestCount;
        private readonly TaskCompletionSource<bool> _allRequestsStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeHandlers;
        private int _maxConcurrentHandlers;
        private int _startedRequests;

        private StreamingProviderServer(
            TcpListener listener,
            Uri baseAddress,
            IReadOnlyList<string> responseLines,
            int expectedRequestCount,
            TimeSpan interChunkDelay)
        {
            _listener = listener;
            BaseAddress = baseAddress;
            _responseLines = responseLines;
            _interChunkDelay = interChunkDelay;
            _expectedRequestCount = expectedRequestCount;
            _runTask = Task.Run(() => RunAsync(_cts.Token));
        }

        public Uri BaseAddress { get; }

        public string? RequestMethod { get; private set; }

        public string? RequestPath { get; private set; }

        public string? RequestBody { get; private set; }

        public ConcurrentBag<string> RequestMethods { get; } = new();

        public ConcurrentBag<string> RequestPaths { get; } = new();

        public ConcurrentBag<string> RequestBodies { get; } = new();

        public int MaxConcurrentHandlers => Volatile.Read(ref _maxConcurrentHandlers);

        public static Task<StreamingProviderServer> StartAsync(
            IReadOnlyList<string> responseLines,
            int expectedRequestCount = 1,
            TimeSpan? interChunkDelay = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(responseLines);

            var port = GetFreeTcpPort();
            var listener = new TcpListener(System.Net.IPAddress.Loopback, port);
            listener.Start();

            var server = new StreamingProviderServer(
                listener,
                new Uri($"http://127.0.0.1:{port}/"),
                responseLines,
                expectedRequestCount,
                interChunkDelay ?? TimeSpan.FromMilliseconds(20));

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
            try
            {
                var handlers = new List<Task>(_expectedRequestCount);

                for (var index = 0; index < _expectedRequestCount; index++)
                {
                    var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                    handlers.Add(HandleClientAsync(client, cancellationToken));
                }

                await Task.WhenAll(handlers).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            var currentHandlers = Interlocked.Increment(ref _activeHandlers);
            UpdateMaxConcurrentHandlers(currentHandlers);

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
                    RequestMethod = parts[0];
                    RequestPath = parts[1];
                    RequestMethods.Add(parts[0]);
                    RequestPaths.Add(parts[1]);
                }

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

                string requestBody;
                if (headers.TryGetValue("Content-Length", out var contentLengthText) &&
                    int.TryParse(contentLengthText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var contentLength) &&
                    contentLength > 0)
                {
                    var bodyBytes = await ReadExactBytesAsync(stream, contentLength, cancellationToken).ConfigureAwait(false);
                    requestBody = Encoding.UTF8.GetString(bodyBytes);
                }
                else
                {
                    requestBody = string.Empty;
                }

                RequestBody = requestBody;
                RequestBodies.Add(requestBody);

                if (Interlocked.Increment(ref _startedRequests) == _expectedRequestCount)
                {
                    _allRequestsStarted.TrySetResult(true);
                }

                await _allRequestsStarted.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                await WriteResponseAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
            finally
            {
                Interlocked.Decrement(ref _activeHandlers);
                client.Dispose();
            }
        }

        private void UpdateMaxConcurrentHandlers(int currentHandlers)
        {
            while (true)
            {
                var observed = Volatile.Read(ref _maxConcurrentHandlers);
                if (currentHandlers <= observed)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _maxConcurrentHandlers, currentHandlers, observed) == observed)
                {
                    return;
                }
            }
        }

        private async Task WriteResponseAsync(Stream stream, CancellationToken cancellationToken)
        {
            var headerBytes = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: application/x-ndjson\r\n" +
                "Transfer-Encoding: chunked\r\n" +
                "Connection: close\r\n\r\n");

            await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);

            for (var index = 0; index < _responseLines.Count; index++)
            {
                var lineBytes = Encoding.UTF8.GetBytes(_responseLines[index] + "\n");
                var chunkHeader = Encoding.ASCII.GetBytes(lineBytes.Length.ToString("X", System.Globalization.CultureInfo.InvariantCulture) + "\r\n");

                await stream.WriteAsync(chunkHeader, cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync(lineBytes, cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync(Encoding.ASCII.GetBytes("\r\n"), cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

                if (index < _responseLines.Count - 1 && _interChunkDelay > TimeSpan.Zero)
                {
                    await Task.Delay(_interChunkDelay, cancellationToken).ConfigureAwait(false);
                }
            }

            await stream.WriteAsync(Encoding.ASCII.GetBytes("0\r\n\r\n"), cancellationToken).ConfigureAwait(false);
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
                    throw new EndOfStreamException("Provider closed the connection while reading the request body.");
                }

                offset += read;
            }

            return buffer;
        }
    }
}
