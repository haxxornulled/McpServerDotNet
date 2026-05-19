using System.Text.Json;
using LanguageExt;
using McpServer.Application.Abstractions.Web;
using McpServer.Application.Mcp.Validation;
using McpServer.Application.Web.Commands;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Mcp.Tools;

public sealed class WebScrapeUrlToolHandler(
    IWebScrapeService webScrapeService,
    ILogger<WebScrapeUrlToolHandler> logger) : IToolHandler<WebScrapeUrlRequest>
{
    public string Name => "web.scrape_url";

    public string Description => "Fetches an HTML page and extracts elements matching a CSS selector.";

    public JsonElement GetInputSchema() =>
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            additionalProperties = false,
            properties = new
            {
                url = new { type = "string" },
                selector = new { type = "string" },
                attribute = new { type = "string" },
                maxResults = new { type = "integer", @default = 5, minimum = 1, maximum = 20 },
                timeout_seconds = new { type = "integer", @default = 30, minimum = 1, maximum = 300 }
            },
            required = new[] { "url", "selector" }
        });

    public IReadOnlyList<string> Validate(WebScrapeUrlRequest request)
    {
        var errors = new List<string>();
        ToolRequestValidation.RequireUri(errors, request.Url, "url");
        ToolRequestValidation.RequireNonWhiteSpace(errors, request.Selector, "selector");
        ToolRequestValidation.RequireRange(errors, request.MaxResults, "maxResults", 1, 20);
        ToolRequestValidation.RequireRange(errors, request.TimeoutSeconds, "timeout_seconds", 1, 300);
        return errors;
    }

    public async ValueTask<Fin<CallToolResult>> Handle(WebScrapeUrlRequest request, CancellationToken ct)
    {
        var result = await webScrapeService
            .ScrapeAsync(new ScrapeWebCommand(
                request.Url,
                request.Selector,
                request.Attribute,
                request.MaxResults,
                request.TimeoutSeconds), ct)
            .ConfigureAwait(false);

        return result.Map(scrapeResult =>
        {
            logger.LogInformation(
                "Tool {ToolName} completed with {MatchCount} match(es)",
                Name,
                scrapeResult.MatchCount);

            var content = JsonSerializer.Serialize(scrapeResult, new JsonSerializerOptions { WriteIndented = true });

            return new CallToolResult(
            [
                new ContentItem("text", content)
            ],
            StructuredContent: scrapeResult);
        });
    }
}
