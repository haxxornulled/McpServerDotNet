using Microsoft.Extensions.Logging;


namespace McpServer.Application.WebSearch
{
    // Shared result schema
    public class WebSearchResult
    {
        public string? Title { get; set; }
        public string? Url { get; set; }
        public string? Snippet { get; set; }
        public double? Score { get; set; }
    }

    // Abstraction for your own API
    public interface ICustomWebSearchApi
    {
        Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct);
    }

    // 1. Implementation of your own API client
    public class CustomWebSearchApi : ICustomWebSearchApi
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<CustomWebSearchApi> _logger;
        private readonly string _baseUrl;
        private readonly string _apiKey;

        public CustomWebSearchApi(HttpClient httpClient, ILogger<CustomWebSearchApi> logger, string baseUrl, string apiKey)
        {
            _httpClient = httpClient;
            _logger = logger;
            _baseUrl = baseUrl.TrimEnd('/');
            _apiKey = apiKey;
        }

        public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct)
        {
            var url = $"{_baseUrl}/search?q={Uri.EscapeDataString(query)}&max={maxResults}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("Authorization", $"Bearer {_apiKey}");
            try
            {
                var resp = await _httpClient.SendAsync(req, ct);
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync(ct);
                var results = System.Text.Json.JsonSerializer.Deserialize<List<WebSearchResult>>(json);
                return results ?? new List<WebSearchResult>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Web search API failed for query: {Query}", query);
                return Array.Empty<WebSearchResult>();
            }
        }
    }
}
