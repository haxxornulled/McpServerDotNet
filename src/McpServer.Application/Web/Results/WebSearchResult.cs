namespace McpServer.Application.Web.Results
{
    public record WebSearchResult(
        string Title,
        string Url,
        string Snippet,
        double Relevance);
}