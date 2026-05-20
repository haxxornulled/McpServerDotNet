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
                EnableToolCalling = false,
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
    public async Task Prompt_Mode_With_Tool_Calling_Should_Show_Streaming_On_In_Header()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("/agent/mcp/tools", StringComparison.OrdinalIgnoreCase) &&
                request.Method == HttpMethod.Get)
            {
                return CreateJsonResponse("""
                    {
                      "status": "ok",
                      "transport": "stdio",
                      "protocolVersion": "2025-03-26",
                      "server": {
                        "name": "McpServer.Host",
                        "version": "1.0.0"
                      },
                      "toolCount": 1,
                      "elapsedMilliseconds": 1,
                      "tools": [
                        {
                          "name": "web.search",
                          "description": "Search the web.",
                          "inputSchema": {
                            "type": "object",
                            "properties": {
                              "query": { "type": "string" },
                              "maxResults": { "type": "integer" }
                            },
                            "required": ["query"]
                          }
                        }
                      ]
                    }
                    """);
            }

            return CreateJsonResponse("""
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
                        "content": "Hello there"
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
                """);
        });

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
                EnableToolCalling = true,
                TimeoutSeconds = 30
            },
            httpClient,
            input,
            output,
            error,
            inputRedirected: false,
            clipboardTextProvider: new FakeClipboardTextProvider(null));

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        var transcript = output.ToString();
        Assert.Contains("Streaming", transcript);
        Assert.DoesNotContain("Streaming   : off", transcript);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task Prompt_Mode_Should_Start_And_Stop_Status_Indicator_While_Waiting()
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
        var statusFactory = new FakeStatusIndicatorFactory();

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
                EnableToolCalling = false,
                TimeoutSeconds = 30
            },
            httpClient,
            input,
            output,
            error,
            inputRedirected: false,
            clipboardTextProvider: new FakeClipboardTextProvider(null),
            statusIndicatorFactory: statusFactory);

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, statusFactory.CreateCount);
        Assert.Equal("Waiting on GPU...", statusFactory.LastMessage);
        Assert.Equal(1, statusFactory.Current.StopCount);
    }

    [Fact]
    public void Console_Text_Width_Should_Treat_Combining_Marks_As_One_Cell()
    {
        Assert.Equal(1, ConsoleTextWidth.GetDisplayWidth("e\u0301"));
        Assert.Equal(4, ConsoleTextWidth.GetDisplayWidth("中文"));
        Assert.Equal(2, ConsoleTextWidth.GetDisplayWidth("🚀"));
    }

    [Fact]
    public void Markdown_Inline_Syntax_Should_Render_Without_Markup_Noise()
    {
        var lines = MarkdownConsoleFormatter.RenderMarkdown("""
            Use **bold**, _italic_, `code`, and [link](https://example.com).
            """, 80);

        Assert.Single(lines);
        Assert.Equal("Use bold, italic, code, and link (https://example.com).", lines[0]);
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
                EnableToolCalling = false,
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
                EnableToolCalling = false,
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
                EnableToolCalling = false,
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
    public async Task Paste_Mode_Should_Collect_MultiLine_Input_As_One_Turn()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert.Contains("First line", body, StringComparison.Ordinal);
            Assert.Contains("Second line", body, StringComparison.Ordinal);
            return CreateJsonResponse("""
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
                        "content": "collected"
                      },
                      "finish_reason": "stop"
                    }
                  ],
                  "usage": {
                    "prompt_tokens": 3,
                    "completion_tokens": 1,
                    "total_tokens": 4
                  }
                }
                """);
        });

        using var httpClient = new HttpClient(handler);
        using var input = new StringReader("""
            /paste
            First line
            Second line
            /end
            """);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var runner = new ChatConsoleRunner(
            new ChatConsoleSettings
            {
                RouterBaseUrl = new Uri("http://127.0.0.1:5177"),
                Model = "fast-local",
                StreamRequested = false,
                EnableToolCalling = false,
                TimeoutSeconds = 30
            },
            httpClient,
            input,
            output,
            error,
            inputRedirected: false,
            clipboardTextProvider: new FakeClipboardTextProvider(null));

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Single(result.Turns);
        Assert.Equal("collected", result.Response);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task Paste_Command_Should_Use_Clipboard_Content_When_Available()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert.Contains("Clipboard line one", body, StringComparison.Ordinal);
            Assert.Contains("Clipboard line two", body, StringComparison.Ordinal);
            return CreateJsonResponse("""
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
                        "content": "clipboard"
                      },
                      "finish_reason": "stop"
                    }
                  ],
                  "usage": {
                    "prompt_tokens": 3,
                    "completion_tokens": 1,
                    "total_tokens": 4
                  }
                }
                """);
        });

        using var httpClient = new HttpClient(handler);
        using var input = new StringReader("/paste");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var runner = new ChatConsoleRunner(
            new ChatConsoleSettings
            {
                RouterBaseUrl = new Uri("http://127.0.0.1:5177"),
                Model = "fast-local",
                StreamRequested = false,
                EnableToolCalling = false,
                TimeoutSeconds = 30
            },
            httpClient,
            input,
            output,
            error,
            inputRedirected: false,
            clipboardTextProvider: new FakeClipboardTextProvider("Clipboard line one\r\nClipboard line two"));

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Single(result.Turns);
        Assert.Equal("clipboard", result.Response);
        Assert.Contains("Clipboard pasted.", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task Search_Command_Should_Call_Web_Search_Tool_And_Render_Results()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert.Contains("/agent/mcp/tools/call", request.RequestUri!.AbsolutePath, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("web.search", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("McpServer AgentRouter", body, StringComparison.OrdinalIgnoreCase);

            return CreateJsonResponse("""
                {
                  "status": "completed",
                  "toolName": "web.search",
                  "allowed": true,
                  "policyDecision": "allowed",
                  "traceId": "mcp-call-test",
                  "transport": "stdio",
                  "elapsedMilliseconds": 42,
                  "result": {
                    "content": [
                      {
                        "type": "text",
                        "text": "{\u0022query\u0022:\u0022McpServer AgentRouter\u0022,\u0022result_count\u0022:1,\u0022results\u0022:[{\u0022rank\u0022:1,\u0022title\u0022:\u0022AgentRouter Docs\u0022,\u0022url\u0022:\u0022https://example.com/docs\u0022,\u0022snippet\u0022:\u0022Clean Architecture notes.\u0022,\u0022relevance\u0022:1.0}]}"
                      }
                    ],
                    "structuredContent": {
                      "query": "McpServer AgentRouter",
                      "result_count": 1,
                      "results": [
                        {
                          "rank": 1,
                          "title": "AgentRouter Docs",
                          "url": "https://example.com/docs",
                          "snippet": "Clean Architecture notes.",
                          "relevance": 1.0
                        }
                      ]
                    }
                  }
                }
                """);
        });

        using var httpClient = new HttpClient(handler);
        using var input = new StringReader("/search McpServer AgentRouter");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var runner = new ChatConsoleRunner(
            new ChatConsoleSettings
            {
                RouterBaseUrl = new Uri("http://127.0.0.1:5177"),
                Model = "fast-local",
                StreamRequested = false,
                TimeoutSeconds = 30
            },
            httpClient,
            input,
            output,
            error,
            inputRedirected: false,
            clipboardTextProvider: new FakeClipboardTextProvider(null));

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Turns);
        var transcript = output.ToString();
        Assert.Contains("Web Search", transcript);
        Assert.Contains("AgentRouter Docs", transcript);
        Assert.Contains("Search results added to context", transcript);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task Prompt_Mode_Should_Allow_Model_Driven_Web_Search_Tool_Calls()
    {
        var chatRequestCount = 0;
        var handler = new FakeHttpMessageHandler(request =>
        {
            var body = request.Content is null
                ? string.Empty
                : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (request.RequestUri!.AbsolutePath.Contains("/agent/mcp/tools/call", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Contains("web.search", body, StringComparison.OrdinalIgnoreCase);
                return CreateJsonResponse("""
                    {
                      "status": "completed",
                      "toolName": "web.search",
                      "allowed": true,
                      "policyDecision": "allowed",
                      "traceId": "mcp-call-test",
                      "transport": "stdio",
                      "elapsedMilliseconds": 42,
                      "result": {
                        "content": [
                          {
                            "type": "text",
                            "text": "{\u0022query\u0022:\u0022AgentRouter\u0022,\u0022result_count\u0022:1,\u0022results\u0022:[{\u0022rank\u0022:1,\u0022title\u0022:\u0022AgentRouter Docs\u0022,\u0022url\u0022:\u0022https://example.com/docs\u0022,\u0022snippet\u0022:\u0022Clean Architecture notes.\u0022,\u0022relevance\u0022:1.0}]}"
                          }
                        ],
                        "structuredContent": {
                          "query": "AgentRouter",
                          "result_count": 1,
                          "results": [
                            {
                              "rank": 1,
                              "title": "AgentRouter Docs",
                              "url": "https://example.com/docs",
                              "snippet": "Clean Architecture notes.",
                              "relevance": 1.0
                            }
                          ]
                        }
                      }
                    }
                    """);
            }

            if (request.RequestUri!.AbsolutePath.Contains("/agent/mcp/tools", StringComparison.OrdinalIgnoreCase) &&
                request.Method == HttpMethod.Get)
            {
                return CreateJsonResponse("""
                    {
                      "status": "ok",
                      "transport": "stdio",
                      "protocolVersion": "2025-03-26",
                      "server": {
                        "name": "McpServer.Host",
                        "version": "1.0.0"
                      },
                      "toolCount": 1,
                      "elapsedMilliseconds": 1,
                      "tools": [
                        {
                          "name": "web.search",
                          "description": "Search the web.",
                          "inputSchema": {
                            "type": "object",
                            "properties": {
                              "query": { "type": "string" },
                              "maxResults": { "type": "integer" }
                            },
                            "required": ["query"]
                          }
                        }
                      ]
                    }
                    """);
            }

            if (request.RequestUri!.AbsolutePath.Contains("/v1/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                chatRequestCount++;
                if (chatRequestCount == 1)
                {
                    Assert.Contains("\"tools\"", body, StringComparison.OrdinalIgnoreCase);
                    Assert.Contains("web.search", body, StringComparison.OrdinalIgnoreCase);
                    return CreateJsonResponse("""
                        {
                          "id": "chatcmpl-tool-1",
                          "object": "chat.completion",
                          "created": 1,
                          "model": "fast-local",
                          "choices": [
                            {
                              "index": 0,
                              "message": {
                                "role": "assistant",
                                "content": "",
                                "tool_calls": [
                                  {
                                    "id": "call-1",
                                    "type": "function",
                                    "function": {
                                      "name": "web.search",
                                      "arguments": "{\"query\":\"AgentRouter\",\"maxResults\":3}"
                                    }
                                  }
                                ]
                              },
                              "finish_reason": "tool_calls"
                            }
                          ],
                          "usage": {
                            "prompt_tokens": 12,
                            "completion_tokens": 6,
                            "total_tokens": 18
                          }
                        }
                        """);
                }

                Assert.Contains("\"tool_call_id\":\"call-1\"", body, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("\"role\":\"tool\"", body, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("AgentRouter Docs", body, StringComparison.OrdinalIgnoreCase);
                return CreateJsonResponse("""
                    {
                      "id": "chatcmpl-tool-2",
                      "object": "chat.completion",
                      "created": 1,
                      "model": "fast-local",
                      "choices": [
                        {
                          "index": 0,
                          "message": {
                            "role": "assistant",
                            "content": "Tool call complete."
                          },
                          "finish_reason": "stop"
                        }
                      ],
                      "usage": {
                        "prompt_tokens": 14,
                        "completion_tokens": 4,
                        "total_tokens": 18
                      }
                    }
                    """);
            }

            throw new InvalidOperationException("Unexpected request URI: " + request.RequestUri);
        });

        using var httpClient = new HttpClient(handler);
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var runner = new ChatConsoleRunner(
            new ChatConsoleSettings
            {
                RouterBaseUrl = new Uri("http://127.0.0.1:5177"),
                Model = "fast-local",
                Prompt = "Use the web if needed.",
                StreamRequested = false,
                TimeoutSeconds = 30,
                EnableToolCalling = true
            },
            httpClient,
            input,
            output,
            error,
            inputRedirected: false,
            clipboardTextProvider: new FakeClipboardTextProvider(null));

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Single(result.Turns);
        Assert.Equal("Tool call complete.", result.Response);
        var transcript = output.ToString();
        Assert.Contains("Tool Request", transcript);
        Assert.Contains("Web Search", transcript);
        Assert.Contains("AgentRouter Docs", transcript);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal(4, handler.RequestBodies.Count);
    }

    [Fact]
    public async Task Prompt_Mode_Should_Interpret_Raw_Json_Tool_Request_Content()
    {
        var chatRequestCount = 0;
        var handler = new FakeHttpMessageHandler(request =>
        {
            var body = request.Content is null
                ? string.Empty
                : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (request.RequestUri!.AbsolutePath.Contains("/agent/mcp/tools/call", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Contains("activity.route", body, StringComparison.OrdinalIgnoreCase);
                return CreateJsonResponse("""
                    {
                      "status": "completed",
                      "toolName": "activity.route",
                      "allowed": true,
                      "policyDecision": "allowed",
                      "traceId": "mcp-call-test",
                      "transport": "stdio",
                      "elapsedMilliseconds": 42,
                      "result": {
                        "content": [
                          {
                            "type": "text",
                            "text": "{\u0022activity\u0022:\u0022ImplementationPlan\u0022,\u0022confidence\u0022:0.93,\u0022reason\u0022:\u0022The user asked for an implementation plan.\u0022,\u0022requiresWorkspace\u0022:true,\u0022requiresShell\u0022:false,\u0022requiresStructuredOutput\u0022:false,\u0022schemaName\u0022:\u0022ActivityPlan\u0022}"
                          }
                        ],
                        "structuredContent": {
                          "activity": "ImplementationPlan",
                          "confidence": 0.93,
                          "reason": "The user asked for an implementation plan.",
                          "requiresWorkspace": true,
                          "requiresShell": false,
                          "requiresStructuredOutput": false,
                          "schemaName": "ActivityPlan"
                        }
                      }
                    }
                    """);
            }

            if (request.RequestUri!.AbsolutePath.Contains("/agent/mcp/tools", StringComparison.OrdinalIgnoreCase) &&
                request.Method == HttpMethod.Get)
            {
                return CreateJsonResponse("""
                    {
                      "status": "ok",
                      "transport": "stdio",
                      "protocolVersion": "2025-03-26",
                      "server": {
                        "name": "McpServer.Host",
                        "version": "1.0.0"
                      },
                      "toolCount": 1,
                      "elapsedMilliseconds": 1,
                      "tools": [
                        {
                          "name": "activity.route",
                          "description": "Classifies a user request into the dynamic MCP activity that should handle it, including the structured-output schema to use.",
                          "inputSchema": {
                            "type": "object",
                            "properties": {
                              "request": { "type": "string" }
                            },
                            "required": ["request"]
                          }
                        }
                      ]
                    }
                    """);
            }

            if (request.RequestUri!.AbsolutePath.Contains("/v1/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                chatRequestCount++;
                if (chatRequestCount == 1)
                {
                    return CreateJsonResponse("""
                        {
                          "id": "chatcmpl-tool-1",
                          "object": "chat.completion",
                          "created": 1,
                          "model": "fast-local",
                          "choices": [
                            {
                              "index": 0,
                              "message": {
                                "role": "assistant",
                                "content": "{\u0022name\u0022:\u0022activity.route\u0022,\u0022arguments\u0022:{\u0022request\u0022:\u0022hi\u0022}}"
                              },
                              "finish_reason": "stop"
                            }
                          ],
                          "usage": {
                            "prompt_tokens": 12,
                            "completion_tokens": 6,
                            "total_tokens": 18
                          }
                        }
                        """);
                }

                Assert.Contains("\"tool_call_id\":\"assistant-content-1\"", body, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("\"role\":\"tool\"", body, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("ImplementationPlan", body, StringComparison.OrdinalIgnoreCase);
                return CreateJsonResponse("""
                    {
                      "id": "chatcmpl-tool-2",
                      "object": "chat.completion",
                      "created": 1,
                      "model": "fast-local",
                      "choices": [
                        {
                          "index": 0,
                          "message": {
                            "role": "assistant",
                            "content": "Planning complete."
                          },
                          "finish_reason": "stop"
                        }
                      ],
                      "usage": {
                        "prompt_tokens": 14,
                        "completion_tokens": 4,
                        "total_tokens": 18
                      }
                    }
                    """);
            }

            throw new InvalidOperationException("Unexpected request URI: " + request.RequestUri);
        });

        using var httpClient = new HttpClient(handler);
        using var input = new StringReader(string.Empty);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var runner = new ChatConsoleRunner(
            new ChatConsoleSettings
            {
                RouterBaseUrl = new Uri("http://127.0.0.1:5177"),
                Model = "fast-local",
                Prompt = "Use the router.",
                StreamRequested = false,
                TimeoutSeconds = 30,
                EnableToolCalling = true
            },
            httpClient,
            input,
            output,
            error,
            inputRedirected: false,
            clipboardTextProvider: new FakeClipboardTextProvider(null));

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Single(result.Turns);
        Assert.Equal("Planning complete.", result.Response);
        var transcript = output.ToString();
        Assert.Contains("Tool Request", transcript);
        Assert.Contains("Activity Route", transcript);
        Assert.Contains("Activity route: ImplementationPlan", transcript);
        Assert.DoesNotContain("{\"name\":\"activity.route\"", transcript, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Equal(4, handler.RequestBodies.Count);
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
                EnableToolCalling = false,
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

    private sealed class FakeClipboardTextProvider : IClipboardTextProvider
    {
        private readonly string? _text;

        public FakeClipboardTextProvider(string? text)
        {
            _text = text;
        }

        public Task<string?> GetTextAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>(_text);
        }
    }

    private sealed class FakeStatusIndicatorFactory : IChatConsoleStatusIndicatorFactory
    {
        public int CreateCount { get; private set; }

        public string? LastMessage { get; private set; }

        public FakeStatusIndicator Current { get; private set; } = new();

        public IChatConsoleStatusIndicator Create(string message)
        {
            CreateCount++;
            LastMessage = message;
            Current = new FakeStatusIndicator();
            return Current;
        }
    }

    private sealed class FakeStatusIndicator : IChatConsoleStatusIndicator
    {
        public int StopCount { get; private set; }

        public void Stop()
        {
            StopCount++;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
