using System.Diagnostics;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using LanguageExt;
using LanguageExt.Common;
using McpServer.Application.Abstractions.Web;
using McpServer.Application.Web.Commands;
using McpServer.Application.Web.Results;
using Microsoft.Extensions.Logging;

namespace McpServer.Infrastructure.Web;

public sealed class WebScrapeService(
    IWebFetchService webFetchService,
    ILogger<WebScrapeService> logger) : IWebScrapeService
{
    private const int MaxTextLength = 1000;

    public async ValueTask<Fin<WebScrapeResult>> ScrapeAsync(ScrapeWebCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            var started = Stopwatch.StartNew();
            var fetch = await webFetchService.FetchUrlAsync(
                new FetchUrlCommand(
                    command.Url,
                    ExtractReadableText: false,
                    TimeoutSeconds: command.TimeoutSeconds), ct).ConfigureAwait(false);

            if (fetch.IsFail)
            {
                return PropagateFailure<WebScrapeResult>(fetch.Map(_ => Unit.Default));
            }

            var page = fetch.Match(
                Succ: value => value,
                Fail: _ => throw new InvalidOperationException("Unexpected web fetch failure while handling success."));

            var document = new HtmlParser().ParseDocument(page.Content);
            var elements = document.QuerySelectorAll(command.Selector);
            var limit = Math.Max(1, command.MaxResults);
            var matchCount = Math.Min(limit, elements.Length);
            var matches = new WebScrapeMatch[matchCount];
            for (var i = 0; i < matchCount; i++)
            {
                matches[i] = CreateMatch(i + 1, elements[i], command.Attribute, page.Url);
            }

            started.Stop();

            logger.LogInformation(
                "Scraped URL {Url} using selector {Selector} and returned {MatchCount} match(es)",
                page.Url,
                command.Selector,
                elements.Length);

            return new WebScrapeResult(
                Url: page.Url,
                Title: page.Title,
                Selector: command.Selector,
                MatchCount: elements.Length,
                Truncated: elements.Length > matches.Length,
                FetchTimeMs: started.ElapsedMilliseconds,
                Matches: matches);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed scraping URL {Url} with selector {Selector}", command.Url, command.Selector);
            return Error.New(ex.Message);
        }
    }

    private static WebScrapeMatch CreateMatch(
        int rank,
        IElement element,
        string? attributeName,
        string baseUrl)
    {
        var text = Trim(HtmlTextExtractor.ExtractReadableText(element.TextContent));
        var href = NormalizeHref(
            baseUrl,
            element.GetAttribute("href") ?? element.QuerySelector("a[href]")?.GetAttribute("href"));
        var attributeValue = string.IsNullOrWhiteSpace(attributeName)
            ? null
            : Trim(element.GetAttribute(attributeName));

        return new WebScrapeMatch(
            Rank: rank,
            TagName: element.TagName.ToLowerInvariant(),
            Text: text,
            Href: href,
            AttributeName: string.IsNullOrWhiteSpace(attributeName) ? null : attributeName.Trim(),
            AttributeValue: attributeValue);
    }

    private static string? NormalizeHref(string baseUrl, string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
        {
            return href.Trim();
        }

        return Uri.TryCreate(baseUri, href, out var resolved)
            ? resolved.ToString()
            : href.Trim();
    }

    private static string Trim(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = HtmlTextExtractor.ExtractReadableText(value);
        return trimmed.Length <= MaxTextLength
            ? trimmed
            : trimmed[..MaxTextLength];
    }

    private static Fin<T> PropagateFailure<T>(Fin<Unit> failure) =>
        failure.Match<Fin<T>>(
            Succ: _ => throw new InvalidOperationException("Unexpected validation success while propagating result."),
            Fail: error => error);
}
