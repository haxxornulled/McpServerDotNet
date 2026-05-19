namespace McpServer.Application.Web.Commands;

public sealed record ScrapeWebCommand(
    string Url,
    string Selector,
    string? Attribute = null,
    int MaxResults = 5,
    int? TimeoutSeconds = null);
