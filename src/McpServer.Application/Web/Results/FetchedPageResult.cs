namespace McpServer.Application.Web.Results
{
    public record FetchedPageResult(
        string Url,
        string Title,
        string Content,
        string ContentType,
        int StatusCode,
        long FetchTimeMs);
}