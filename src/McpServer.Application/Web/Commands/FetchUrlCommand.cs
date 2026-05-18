namespace McpServer.Application.Web.Commands;

public sealed record FetchUrlCommand(
    string Url,
    bool ExtractReadableText = true,
    int? MaxBytes = null,
    int? TimeoutSeconds = null);
