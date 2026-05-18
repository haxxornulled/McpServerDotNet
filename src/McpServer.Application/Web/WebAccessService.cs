using System.Runtime.CompilerServices;
using LanguageExt;
using McpServer.Application.Abstractions.Web;
using McpServer.Application.Web.Commands;
using McpServer.Application.Web.Results;
using Microsoft.Extensions.Logging;

namespace McpServer.Application.Web
{
    public class WebAccessService : IWebAccessService
    {
        private readonly ILogger<WebAccessService> _logger;
        private readonly HttpClient _httpClient;

        public WebAccessService(ILogger<WebAccessService> logger, HttpClient httpClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public ValueTask<Fin<IReadOnlyList<WebSearchResult>>> SearchWebAsync(SearchWebCommand command, CancellationToken ct)
        {
            // Hot path optimization - check for null/empty immediately
            if (string.IsNullOrWhiteSpace(command.Query))
            {
                _logger.LogWarning("SearchWebAsync called with invalid query");
                return new ValueTask<Fin<IReadOnlyList<WebSearchResult>>>(Fin<IReadOnlyList<WebSearchResult>>.Fail(Error.New("Query cannot be null or empty")));
            }

            try
            {
                _logger.LogInformation("Searching web for query: {Query}", command.Query);

                // In a real implementation, this would perform an actual web search
                // For now, we'll simulate with mock results
                var searchResults = new List<WebSearchResult>
                {
                    new WebSearchResult(
                        "Sample Search Result 1",
                        "https://example.com/result1",
                        "This is a sample search result description for the first result.",
                        0.95),
                    new WebSearchResult(
                        "Sample Search Result 2", 
                        "https://example.com/result2",
                        "This is a sample search result description for the second result.",
                        0.87),
                    new WebSearchResult(
                        "Sample Search Result 3",
                        "https://example.com/result3",
                        "This is a sample search result description for the third result.",
                        0.72)
                };

                _logger.LogInformation("Successfully searched web for query: {Query}", command.Query);
                
                return new ValueTask<Fin<IReadOnlyList<WebSearchResult>>>(Fin<IReadOnlyList<WebSearchResult>>.Succ(searchResults));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to search web for query: {Query}", command.Query);
                return new ValueTask<Fin<IReadOnlyList<WebSearchResult>>>(Fin<IReadOnlyList<WebSearchResult>>.Fail(Error.New($"Failed to search web: {ex.Message}")));
            }
        }

        public ValueTask<Fin<FetchedPageResult>> FetchUrlAsync(FetchUrlCommand command, CancellationToken ct)
        {
            // Hot path optimization - check for null/empty immediately
            if (string.IsNullOrWhiteSpace(command.Url))
            {
                _logger.LogWarning("FetchUrlAsync called with invalid URL");
                return new ValueTask<Fin<FetchedPageResult>>(Fin<FetchedPageResult>.Fail(Error.New("URL cannot be null or empty")));
            }

            // Hot path optimization - validate URL format early
            if (!Uri.IsWellFormedUriString(command.Url, UriKind.Absolute))
            {
                _logger.LogWarning("FetchUrlAsync called with invalid URL format: {Url}", command.Url);
                return new ValueTask<Fin<FetchedPageResult>>(Fin<FetchedPageResult>.Fail(Error.New($"Invalid URL format: {command.Url}")));
            }

            try
            {
                _logger.LogInformation("Fetching URL: {Url}", command.Url);

                // In a real implementation, this would fetch the actual URL content
                // For now, we'll simulate with mock data
                var mockContent = $"""
                    <!DOCTYPE html>
                    <html>
                    <head><title>Mock Page</title></head>
                    <body>
                        <h1>Mock Page Content</h1>
                        <p>This is simulated content from {command.Url}</p>
                        <p>Fetched at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}</p>
                    </body>
                    </html>
                    """;

                var result = new FetchedPageResult(
                    command.Url,
                    "Mock Page",
                    mockContent,
                    "text/html",
                    200,
                    150);

                _logger.LogInformation("Successfully fetched URL: {Url}", command.Url);
                
                return new ValueTask<Fin<FetchedPageResult>>(Fin<FetchedPageResult>.Succ(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch URL: {Url}", command.Url);
                return new ValueTask<Fin<FetchedPageResult>>(Fin<FetchedPageResult>.Fail(Error.New($"Failed to fetch URL: {ex.Message}")));
            }
        }
    }
}

