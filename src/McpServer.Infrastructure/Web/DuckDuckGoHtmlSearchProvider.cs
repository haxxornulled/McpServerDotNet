using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using LanguageExt;
using LanguageExt.Common;
using McpServer.Application.Abstractions.Web;
using McpServer.Application.Web.Commands;
using McpServer.Application.Web.Results;
using Microsoft.Extensions.Logging;

namespace McpServer.Infrastructure.Web;

public sealed class DuckDuckGoHtmlSearchProvider(
    IHttpClientFactory httpClientFactory,
    IWebPolicy webPolicy,
    ILogger<DuckDuckGoHtmlSearchProvider> logger) : IWebSearchProvider
{
    public async ValueTask<Fin<IReadOnlyList<WebSearchResult>>> SearchAsync(SearchWebCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Query))
        {
            return Error.New("Search query is required.");
        }

        var requestUri = $"{webPolicy.SearchBaseUrl}{Uri.EscapeDataString(command.Query)}";
        var validated = await ValidateUrlAndDnsAsync(requestUri, ct).ConfigureAwait(false);
        if (validated.IsFail)
        {
            return PropagateFailure<IReadOnlyList<WebSearchResult>>(validated);
        }

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(webPolicy.DefaultTimeout);

            var client = httpClientFactory.CreateClient("web-search");

            using var response = await SendWithPolicyAsync(client, requestUri, linkedCts.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
            var responseUri = response.RequestMessage?.RequestUri ?? new Uri(requestUri);
            var results = ExtractResults(body, responseUri, command.MaxResults);

            if (results.Count == 0)
            {
                var text = HtmlTextExtractor.ExtractReadableText(body);
                results =
                [
                    new WebSearchResult(
                        Title: $"Search results for: {command.Query}",
                        Url: responseUri.ToString(),
                        Snippet: text.Length > 300 ? text[..300] : text,
                        Relevance: 1.0)
                ];
            }

            var limitedResults = LimitResults(results, Math.Max(1, command.MaxResults));
            logger.LogInformation("Executed web search with {ResultCount} result(s)", limitedResults.Length);

            return limitedResults;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed searching web search provider");
            return Error.New(ex.Message);
        }
    }

    private async Task<HttpResponseMessage> SendWithPolicyAsync(HttpClient client, string url, CancellationToken ct)
    {
        var currentUrl = url;

        for (var redirects = 0; redirects <= webPolicy.MaxRedirects; redirects++)
        {
            var validated = await ValidateUrlAndDnsAsync(currentUrl, ct).ConfigureAwait(false);
            if (validated.IsFail)
            {
                throw new InvalidOperationException(validated.Match(
                    Succ: _ => "Unexpected web policy success.",
                    Fail: error => error.Message));
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("McpServer", "0.1.0"));

            var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct).ConfigureAwait(false);

            if (!IsRedirect(response.StatusCode))
            {
                return response;
            }

            var location = response.Headers.Location;
            if (location is null)
            {
                return response;
            }

            if (redirects == webPolicy.MaxRedirects)
            {
                response.Dispose();
                throw new InvalidOperationException($"Exceeded maximum redirect count: {webPolicy.MaxRedirects}");
            }

            currentUrl = ResolveRedirectUrl(response.RequestMessage?.RequestUri, location);
            response.Dispose();
        }

        throw new InvalidOperationException($"Exceeded maximum redirect count: {webPolicy.MaxRedirects}");
    }

    private async ValueTask<Fin<Unit>> ValidateUrlAndDnsAsync(string url, CancellationToken ct)
    {
        var urlValidation = webPolicy.ValidateUrl(url);
        if (urlValidation.IsFail)
        {
            return urlValidation;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return Error.New($"Invalid URL: {url}");
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.Host, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Error.New($"Failed to resolve host '{uri.Host}': {ex.Message}");
        }

        return webPolicy.ValidateResolvedAddresses(uri.Host, addresses);
    }

    private static IReadOnlyList<WebSearchResult> ExtractResults(string html, Uri requestUri, int maxResults)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return Array.Empty<WebSearchResult>();
        }

        var document = new HtmlParser().ParseDocument(html);
        var anchors = document.QuerySelectorAll("a.result__a");
        if (anchors.Length == 0)
        {
            return Array.Empty<WebSearchResult>();
        }

        var limit = Math.Max(1, maxResults);
        var results = new List<WebSearchResult>(Math.Min(limit, anchors.Length));

        for (var i = 0; i < anchors.Length; i++)
        {
            if (results.Count >= limit)
            {
                break;
            }

            var anchor = anchors[i];
            var href = anchor.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            var title = HtmlTextExtractor.ExtractReadableText(anchor.TextContent);
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var url = NormalizeResultUrl(requestUri, href);
            if (url is null)
            {
                continue;
            }

            var container = FindResultContainer(anchor);
            var snippet = ExtractSnippet(container, title);
            var relevance = Math.Max(0.1, 1.0 - (results.Count * 0.1));

            results.Add(new WebSearchResult(title, url, snippet, relevance));
        }

        return results;
    }

    private static WebSearchResult[] LimitResults(IReadOnlyList<WebSearchResult> results, int maxResults)
    {
        var limit = Math.Min(Math.Max(1, maxResults), results.Count);
        var limited = new WebSearchResult[limit];
        for (var i = 0; i < limit; i++)
        {
            limited[i] = results[i];
        }

        return limited;
    }

    private static IElement? FindResultContainer(IElement anchor)
    {
        IElement? current = anchor.ParentElement;
        while (current is not null)
        {
            if (current.ClassList.Contains("result"))
            {
                return current;
            }

            current = current.ParentElement;
        }

        return anchor.ParentElement;
    }

    private static string? NormalizeResultUrl(Uri requestUri, string href)
    {
        var decodedHref = WebUtility.HtmlDecode(href).Trim();
        if (string.IsNullOrWhiteSpace(decodedHref))
        {
            return null;
        }

        if (!Uri.TryCreate(requestUri, decodedHref, out var resolved))
        {
            return null;
        }

        return TryExtractDuckDuckGoTarget(resolved, out var target) ? target : resolved.ToString();
    }

    private static string ExtractSnippet(IElement? container, string title)
    {
        var snippetElement = container?.QuerySelector(".result__snippet");
        var snippetHtml = snippetElement?.TextContent ?? container?.TextContent ?? string.Empty;

        var snippet = HtmlTextExtractor.ExtractReadableText(snippetHtml);
        if (snippet.StartsWith(title, StringComparison.OrdinalIgnoreCase))
        {
            snippet = snippet[title.Length..].TrimStart(':', '-', '–', '—', ' ');
        }

        return snippet.Length > 300 ? snippet[..300] : snippet;
    }

    private static bool TryExtractDuckDuckGoTarget(Uri uri, out string target)
    {
        target = string.Empty;

        if (!uri.Host.Contains("duckduckgo", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.Contains("/l/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equalsIndex = pair.IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            var name = Uri.UnescapeDataString(pair[..equalsIndex]);
            if (!name.Equals("uddg", StringComparison.OrdinalIgnoreCase) &&
                !name.Equals("u", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            target = Uri.UnescapeDataString(pair[(equalsIndex + 1)..]);
            return !string.IsNullOrWhiteSpace(target);
        }

        return false;
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Moved or
            HttpStatusCode.Redirect or
            HttpStatusCode.RedirectMethod or
            HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;

    private static string ResolveRedirectUrl(Uri? requestUri, Uri location)
    {
        if (location.IsAbsoluteUri)
        {
            return location.ToString();
        }

        if (requestUri is null)
        {
            throw new InvalidOperationException("Relative redirect cannot be resolved because request URI is unavailable.");
        }

        return new Uri(requestUri, location).ToString();
    }

    private static Fin<T> PropagateFailure<T>(Fin<Unit> failure) =>
        failure.Match<Fin<T>>(
            Succ: _ => throw new InvalidOperationException("Expected validation failure while propagating result."),
            Fail: error => error);
}
