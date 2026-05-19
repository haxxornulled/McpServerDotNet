using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using McpServer.AgentRouter.Tools;
using Xunit;

namespace McpServer.AgentRouter.UnitTests;

public sealed class ChatConsoleRunnerTests
{
    [Fact]
    public async Task Prompt_Mode_Should_Return_Assistant_Response()
    {
        var handler = new FakeHttpMessageHandler(_ => CreateJsonResponse("""
            {
              "id": "chatcmpl-test",
              "object": "chat.completion",
              "created": 1,
              "model": "fast-local",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "hello back"
                  },
                  "finish_reason": "stop"
                }
              ],
              "usage": {
                "prompt_tokens": 4,
                "completion_tokens": 2,
                "total_tokens": 6
              }
            }
            """));

        using var httpClient = new HttpClient(handler);
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var runner = new ChatConsoleRunner(
            new ChatConsoleSettings
            {
                RouterBaseUrl = new Uri("http://127.0.0.1:5177"),
                Model = "fast-local",
                Prompt = "Say hello",
                StreamRequested = false,
                TimeoutSeconds = 30
            },
            httpClient,
            input,
            output,
            error,
            inputRedirected: false);

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("hello back", result.Response);
        Assert.Single(result.Turns);
        Assert.Contains("╭─ Chat Console", output.ToString());
        Assert.Contains("╭─ Assistant", output.ToString());
        Assert.Contains("hello back", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("\"stream\":false", handler.RequestBodies[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Console_Text_Width_Should_Treat_Combining_Marks_As_One_Cell()
    {
        Assert.Equal(1, ConsoleTextWidth.GetDisplayWidth("e\u0301"));
        Assert.Equal(4, ConsoleTextWidth.GetDisplayWidth("中文"));
        Assert.Equal(2, ConsoleTextWidth.GetDisplayWidth("🚀"));
    }

    [Fact]
    public async Task Streaming_Mode_Should_Stream_Assistant_Response()
    {
        var handler = new FakeHttpMessageHandler(_ => CreateStreamResponse("""
            data: {"id":"chatcmpl-test","object":"chat.completion.chunk","created":1,"model":"fast-local","choices":[{"index":0,"delta":{"role":"assistant","content":"Hel"},"finish_reason":null}]}

            data: {"id":"chatcmpl-test","object":"chat.completion.chunk","created":1,"model":"fast-local","choices":[{"index":0,"delta":{"content":"lo"},"finish_reason":"stop"}]}

            data: [DONE]
            """));

        using var httpClient = new HttpClient(handler);
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var runner = new ChatConsoleRunner(
            new ChatConsoleSettings
            {
                RouterBaseUrl = new Uri("http://127.0.0.1:5177"),
                Model = "fast-local",
                Prompt = "Say hello",
                StreamRequested = true,
                TimeoutSeconds = 30
            },
            httpClient,
            input,
            output,
            error,
            inputRedirected: false);

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Hello", result.Response);
        Assert.Single(result.Turns);
        Assert.Contains("╭─ Assistant", output.ToString());
        Assert.Contains("Hello", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("\"stream\":true", handler.RequestBodies[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Streaming_Markdown_Response_Should_Render_As_A_Markdown_Transcript()
    {
        var handler = new FakeHttpMessageHandler(_ => CreateStreamResponse("""
            data: {"id":"chatcmpl-test","object":"chat.completion.chunk","created":1,"model":"fast-local","choices":[{"index":0,"delta":{"role":"assistant","content":"# Streamed Update\n\n- markdown\n- 中文\n- emoji 🚀\n\n```python\n"},"finish_reason":null}]}

            data: {"id":"chatcmpl-test","object":"chat.completion.chunk","created":1,"model":"fast-local","choices":[{"index":0,"delta":{"content":"print(\"hello\")\n```"},"finish_reason":"stop"}]}

            data: [DONE]
            """));

        using var httpClient = new HttpClient(handler);
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var runner = new ChatConsoleRunner(
            new ChatConsoleSettings
            {
                RouterBaseUrl = new Uri("http://127.0.0.1:5177"),
                Model = "fast-local",
                Prompt = "Stream markdown please",
                StreamRequested = true,
                TimeoutSeconds = 30
            },
            httpClient,
            input,
            output,
            error,
            inputRedirected: false);

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        var transcript = output.ToString();
        Assert.Contains("Streamed Update", transcript);
        Assert.Contains("• markdown", transcript);
        Assert.Contains("中文", transcript);
        Assert.Contains("emoji 0", transcript);
        Assert.Contains("Code: python", transcript);
        Assert.Contains("print(\"hello\")", transcript);
        Assert.DoesNotContain("```", transcript);
        Assert.DoesNotContain("🚀", transcript);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("\"stream\":true", handler.RequestBodies[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Markdown_Response_Should_Render_As_A_Markdown_Transcript()
    {
        var handler = new FakeHttpMessageHandler(_ => CreateJsonResponse("""
            {
              "id": "chatcmpl-test",
              "object": "chat.completion",
              "created": 1,
              "model": "fast-local",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "# Multilingual Update\n\nThe router now supports:\n- **markdown** output\n- 中文, 日本語, Español, and emoji 🚀\n\n```python\nprint(\"hello from python\")\n```\n\n```csharp\nConsole.WriteLine(\"hello from csharp\");\n```"
                  },
                  "finish_reason": "stop"
                }
              ],
              "usage": {
                "prompt_tokens": 8,
                "completion_tokens": 19,
                "total_tokens": 27
              }
            }
            """));

        using var httpClient = new HttpClient(handler);
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var runner = new ChatConsoleRunner(
            new ChatConsoleSettings
            {
                RouterBaseUrl = new Uri("http://127.0.0.1:5177"),
                Model = "fast-local",
                Prompt = "Show me a markdown status update.",
                StreamRequested = false,
                TimeoutSeconds = 30
            },
            httpClient,
            input,
            output,
            error,
            inputRedirected: false);

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        var transcript = output.ToString();
        Assert.Contains("Multilingual Update", transcript);
        Assert.Contains("• markdown output", transcript);
        Assert.Contains("中文", transcript);
        Assert.Contains("emoji 0", transcript);
        Assert.Contains("Code: python", transcript);
        Assert.Contains("print(\"hello from python\")", transcript);
        Assert.Contains("Code: csharp", transcript);
        Assert.Contains("Console.WriteLine(\"hello from csharp\");", transcript);
        Assert.DoesNotContain("```", transcript);
        Assert.DoesNotContain("🚀", transcript);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task Prompt_File_Should_Be_Used_And_Transcript_Should_Be_Written()
    {
        using var tempDirectory = new TempDirectory();
        var promptFilePath = Path.Combine(tempDirectory.DirectoryPath, "prompt.txt");
        var transcriptPath = Path.Combine(tempDirectory.DirectoryPath, "transcript.json");
        await File.WriteAllTextAsync(promptFilePath, "Say hello from file.");

        var handler = new FakeHttpMessageHandler(_ => CreateJsonResponse("""
            {
              "id": "chatcmpl-test",
              "object": "chat.completion",
              "created": 1,
              "model": "fast-local",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "file hello"
                  },
                  "finish_reason": "stop"
                }
              ],
              "usage": {
                "prompt_tokens": 5,
                "completion_tokens": 2,
                "total_tokens": 7
              }
            }
            """));

        using var httpClient = new HttpClient(handler);
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var runner = new ChatConsoleRunner(
            new ChatConsoleSettings
            {
                RouterBaseUrl = new Uri("http://127.0.0.1:5177"),
                Model = "fast-local",
                PromptFilePath = promptFilePath,
                TranscriptPath = transcriptPath,
                StreamRequested = false,
                TimeoutSeconds = 30
            },
            httpClient,
            input,
            output,
            error,
            inputRedirected: false);

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Single(result.Turns);
        Assert.Equal("file hello", result.Turns[0].Response);
        Assert.Equal("file hello", result.Response);
        Assert.True(File.Exists(transcriptPath));
        Assert.Contains("╭─ Chat Console", output.ToString());
        var transcript = await File.ReadAllTextAsync(transcriptPath);
        var transcriptJson = JsonNode.Parse(transcript);
        Assert.NotNull(transcriptJson);
        Assert.Equal("chat", transcriptJson?["sessionName"]?.GetValue<string>());
        Assert.Equal("Say hello from file.", transcriptJson?["turns"]?[0]?["prompt"]?.GetValue<string>());
        Assert.Equal("file hello", transcriptJson?["turns"]?[0]?["response"]?.GetValue<string>());
        Assert.Contains("Say hello from file.", handler.RequestBodies[0], StringComparison.OrdinalIgnoreCase);
    }

    private static HttpResponseMessage CreateJsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage CreateStreamResponse(string sse)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
        };
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory ?? throw new ArgumentNullException(nameof(responseFactory));
        }

        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            }
            else
            {
                RequestBodies.Add(string.Empty);
            }

            return _responseFactory(request);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "mcpserver-chat-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
