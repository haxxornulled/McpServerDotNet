namespace McpServer.Application.Web.Commands;

public sealed record SearchWebCommand(
    string Query,
    int MaxResults = 5);
