using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using McpServer.AgentRouter.Tools;
using Xunit;

namespace McpServer.AgentRouter.UnitTests;

public sealed class OllamaBenchRunnerTests
{
    [Fact]
    public async Task RunAsync_Should_Rank_Better_Model_Higher_Than_Worse_Model()
    {
        var handler = new FakeHttpMessageHandler(request =>
        {
            var body = request.Content is null
                ? string.Empty
                : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (request.RequestUri!.AbsolutePath.Equals("/api/tags", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "models": [
                            { "name": "good-model" },
                            { "name": "bad-model" }
                          ]
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                };
            }

            if (!request.RequestUri.AbsolutePath.Equals("/api/chat", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Unexpected request: " + request.RequestUri);
            }

            var model = ReadJsonString(body, "model");
            var prompt = ReadUserPrompt(body);

            if (string.Equals(model, "good-model", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        CreateResponseJson(prompt, correct: true),
                        Encoding.UTF8,
                        "application/json")
                };
            }

            if (string.Equals(model, "bad-model", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "message": {
                            "content": "{\u0022activity\u0022:\u0022Explain\u0022,\u0022confidence\u0022:0.10,\u0022reason\u0022:\u0022wrong on purpose\u0022,\u0022requiresWorkspace\u0022:false,\u0022requiresShell\u0022:false,\u0022requiresStructuredOutput\u0022:false,\u0022schemaName\u0022:\u0022explain_result\u0022}"
                          }
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                };
            }

            throw new InvalidOperationException("Unexpected model: " + model);
        });

        using var httpClient = new HttpClient(handler);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var runner = new OllamaBenchRunner(
            new OllamaBenchSettings
            {
                BaseUrl = "http://127.0.0.1:11434",
                TimeoutSeconds = 30
            },
            httpClient,
            output,
            error);

        var result = await runner.RunAsync(CancellationToken.None);

        Assert.True(result.ServerReachable);
        Assert.Equal(2, result.Models.Count);
        Assert.Equal("good-model", result.Models[0].Model);
        Assert.Equal("bad-model", result.Models[1].Model);
        Assert.True(result.Models[0].Accuracy > result.Models[1].Accuracy);
        Assert.True(result.Models[0].AverageScore > result.Models[1].AverageScore);
        Assert.Contains("Ranking:", output.ToString());
        Assert.Contains("good-model", output.ToString());
        Assert.Contains("bad-model", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task RunAsync_With_Report_Directory_Should_Write_Json_And_Csv_Summaries()
    {
        var reportDirectory = Path.Combine(Path.GetTempPath(), "ollama-bench-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture));

        try
        {
            var handler = new FakeHttpMessageHandler(request =>
            {
                if (request.RequestUri!.AbsolutePath.Equals("/api/tags", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            """
                            {
                              "models": [
                                { "name": "good-model" }
                              ]
                            }
                            """,
                            Encoding.UTF8,
                            "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """
                        {
                          "message": {
                            "content": "{\u0022activity\u0022:\u0022Explain\u0022,\u0022confidence\u0022:0.95,\u0022reason\u0022:\u0022bench response\u0022,\u0022requiresWorkspace\u0022:false,\u0022requiresShell\u0022:false,\u0022requiresStructuredOutput\u0022:false,\u0022schemaName\u0022:\u0022explain_result\u0022}"
                          }
                        }
                        """,
                        Encoding.UTF8,
                        "application/json")
                };
            });

            using var httpClient = new HttpClient(handler);
            using var output = new StringWriter();
            using var error = new StringWriter();

            var runner = new OllamaBenchRunner(
                new OllamaBenchSettings
                {
                    BaseUrl = "http://127.0.0.1:11434",
                    TimeoutSeconds = 30,
                    ReportDirectory = reportDirectory
                },
                httpClient,
                output,
                error);

            var result = await runner.RunAsync(CancellationToken.None);

            Assert.True(result.ServerReachable);
            Assert.True(File.Exists(Path.Combine(reportDirectory, "summary.json")));
            Assert.True(File.Exists(Path.Combine(reportDirectory, "summary.csv")));

            var summaryJson = await File.ReadAllTextAsync(Path.Combine(reportDirectory, "summary.json"));
            using var summaryDocument = System.Text.Json.JsonDocument.Parse(summaryJson);
            var summaryRoot = summaryDocument.RootElement;
            Assert.Equal("http://127.0.0.1:11434/", summaryRoot.GetProperty("baseUrl").GetString());
            Assert.Equal("activity-routing", summaryRoot.GetProperty("promptSet").GetString());
            Assert.Equal(1, summaryRoot.GetProperty("modelCount").GetInt32());
            Assert.Equal("good-model", summaryRoot.GetProperty("models")[0].GetProperty("model").GetString());

            var summaryCsv = await File.ReadAllTextAsync(Path.Combine(reportDirectory, "summary.csv"));
            Assert.Contains("BaseUrl,PromptSet,Rank,Model", summaryCsv);
            Assert.Contains("good-model", summaryCsv);
        }
        finally
        {
            if (Directory.Exists(reportDirectory))
            {
                Directory.Delete(reportDirectory, recursive: true);
            }
        }
    }

    private static string CreateResponseJson(string prompt, bool correct)
    {
        var activity = prompt.Contains("build failed", StringComparison.OrdinalIgnoreCase)
            ? "BuildFix"
            : prompt.Contains("test failed", StringComparison.OrdinalIgnoreCase)
                ? "TestFailureAnalysis"
                : prompt.Contains("deep code review", StringComparison.OrdinalIgnoreCase)
                    ? "DeepCodeReview"
                    : prompt.Contains("hard", StringComparison.OrdinalIgnoreCase)
                        ? "SecurityReview"
                        : prompt.Contains("hanging", StringComparison.OrdinalIgnoreCase)
                            ? "Diagnostic"
                            : prompt.Contains("plan", StringComparison.OrdinalIgnoreCase)
                                ? "ImplementationPlan"
                                : "Explain";

        var confidence = correct ? 0.92 : 0.10;
        var schema = activity switch
        {
            "Explain" => "explain_result",
            "BuildFix" => "build_fix_result",
            "TestFailureAnalysis" => "test_failure_analysis_result",
            "DeepCodeReview" => "deep_code_review_result",
            "SecurityReview" => "security_review_result",
            "Diagnostic" => "diagnostic_result",
            "ImplementationPlan" => "implementation_plan_result",
            _ => "explain_result"
        };
        return $$"""
            {
              "message": {
                "content": "{\u0022activity\u0022:\u0022{{activity}}\u0022,\u0022confidence\u0022:{{confidence.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}},\u0022reason\u0022:\u0022bench response\u0022,\u0022requiresWorkspace\u0022:false,\u0022requiresShell\u0022:false,\u0022requiresStructuredOutput\u0022:false,\u0022schemaName\u0022:\u0022{{schema}}\u0022}"
              }
            }
            """;
    }

    private static string ReadJsonString(string body, string propertyName)
    {
        using var document = System.Text.Json.JsonDocument.Parse(body);
        var root = document.RootElement;
        return root.TryGetProperty(propertyName, out var property) && property.ValueKind == System.Text.Json.JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string ReadUserPrompt(string body)
    {
        using var document = System.Text.Json.JsonDocument.Parse(body);
        var root = document.RootElement;
        var messages = root.GetProperty("messages");
        return messages[1].GetProperty("content").GetString() ?? string.Empty;
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
