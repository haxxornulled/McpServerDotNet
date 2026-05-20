using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using McpServer.AgentRouter.Tools;
using Xunit;

namespace McpServer.AgentRouter.UnitTests;

public sealed class OllamaListRunnerTests
{
    [Fact]
    public async Task RunAsync_Should_List_Models_From_Ollama_Tags()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            Assert.Equal("/api/tags", request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "models": [
                        {
                          "name": "qwen3-coder:30b",
                          "size": 16613851648,
                          "modified_at": "2026-05-18T18:00:00Z",
                          "digest": "sha256:abc123",
                          "details": {
                            "family": "qwen3",
                            "quantization_level": "Q4_K_M"
                          }
                        },
                        {
                          "name": "devstral-small-2",
                          "size": 4123456789,
                          "modified_at": "2026-05-17T09:30:00Z",
                          "digest": "sha256:def456",
                          "details": {
                            "family": "devstral",
                            "quantization_level": "Q4_K_M"
                          }
                        }
                      ]
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var runner = new OllamaListRunner(
            new OllamaListSettings
            {
                BaseUrl = "http://127.0.0.1:11434"
            },
            httpClient,
            output,
            error);

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.True(result.ServerReachable);
        Assert.Null(result.Message);
        Assert.Equal(2, result.Models.Count);
        Assert.Equal("qwen3-coder:30b", result.Models[0].Name);
        Assert.Equal("devstral-small-2", result.Models[1].Name);
        Assert.Contains("Models   : 2", output.ToString());
        Assert.Contains("qwen3-coder:30b", output.ToString());
        Assert.Contains("devstral-small-2", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }
}
