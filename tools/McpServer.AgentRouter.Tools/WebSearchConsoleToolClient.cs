using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace McpServer.AgentRouter.Tools;

internal sealed class WebSearchConsoleToolClient
{
    private readonly HttpClient _httpClient;
    private readonly Uri _routerBaseUrl;

    public WebSearchConsoleToolClient(HttpClient httpClient, Uri routerBaseUrl)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _routerBaseUrl = routerBaseUrl ?? throw new ArgumentNullException(nameof(routerBaseUrl));
    }

    public async Task<WebSearchConsoleResult> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Query is required.", nameof(query));
        }

        var response = await HttpJson.PostAsync(
                _httpClient,
                new Uri(_routerBaseUrl, "/agent/mcp/tools/call"),
                new
                {
                    toolName = "web.search",
                    arguments = new
                    {
                        query,
                        maxResults
                    },
                    timeoutSeconds = 30,
                    maxOutputChars = 20_000
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (!response.Success)
        {
            throw new InvalidOperationException(response.ErrorMessage);
        }

        var resultNode = response.Json?["result"];
        if (resultNode is null)
        {
            throw new InvalidOperationException("Web search response did not include a result.");
        }

        var structured = resultNode["structuredContent"];
        if (structured is not null)
        {
            var parsed = TryParseStructuredResult(structured);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        var contentText = ReadTextContent(resultNode);
        if (!string.IsNullOrWhiteSpace(contentText))
        {
            return new WebSearchConsoleResult(query, 0, Array.Empty<WebSearchConsoleResultItem>(), contentText);
        }

        throw new InvalidOperationException("Web search response did not contain readable content.");
    }

    private static WebSearchConsoleResult? TryParseStructuredResult(JsonNode structured)
    {
        if (structured is not JsonObject objectNode)
        {
            return null;
        }

        var query = objectNode["query"]?.GetValue<string>() ?? string.Empty;
        var resultCount = objectNode["result_count"]?.GetValue<int>() ?? 0;
        var items = new List<WebSearchConsoleResultItem>();

        if (objectNode["results"] is JsonArray resultsArray)
        {
            foreach (var item in resultsArray)
            {
                if (item is not JsonObject resultObject)
                {
                    continue;
                }

                items.Add(new WebSearchConsoleResultItem(
                    resultObject["title"]?.GetValue<string>() ?? string.Empty,
                    resultObject["url"]?.GetValue<string>() ?? string.Empty,
                    resultObject["snippet"]?.GetValue<string>() ?? string.Empty,
                    resultObject["relevance"]?.GetValue<double>() ?? 0));
            }
        }

        return new WebSearchConsoleResult(query, resultCount, items, null);
    }

    private static string? ReadTextContent(JsonNode resultNode)
    {
        if (resultNode["content"] is not JsonArray contentArray || contentArray.Count == 0)
        {
            return null;
        }

        foreach (var item in contentArray)
        {
            if (item is not JsonObject contentObject)
            {
                continue;
            }

            var text = contentObject["text"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }
}

internal sealed record WebSearchConsoleResult(
    string Query,
    int ResultCount,
    IReadOnlyList<WebSearchConsoleResultItem> Results,
    string? RawText);

internal sealed record WebSearchConsoleResultItem(
    string Title,
    string Url,
    string Snippet,
    double Relevance);
