using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using LanguageExt;
using LanguageExt.Common;
using McpServer.Application.Abstractions.Web;
using McpServer.Application.Web.Commands;
using McpServer.Application.Web.Results;
using Microsoft.Extensions.Logging;

namespace McpServer.Infrastructure.Web;

public sealed class WebFetchService(
    IHttpClientFactory httpClientFactory,
    IWebPolicy webPolicy,
    ILogger<WebFetchService> logger) : IWebFetchService
{
    public async ValueTask<Fin<FetchedPageResult>> FetchUrlAsync(FetchUrlCommand command, CancellationToken ct)
    {
        var validated = await ValidateUrlAndDnsAsync(command.Url, ct).ConfigureAwait(false);
        if (validated.IsFail)
        {
            return PropagateFailure<FetchedPageResult>(validated);
        }

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(
                command.TimeoutSeconds.HasValue
                    ? TimeSpan.FromSeconds(command.TimeoutSeconds.Value)
                    : webPolicy.DefaultTimeout);

            var client = httpClientFactory.CreateClient("web-access");
            var stopwatch = Stopwatch.StartNew();
            using var response = await SendWithPolicyAsync(client, command.Url, linkedCts.Token).ConfigureAwait(false);
            stopwatch.Stop();

            var contentType = response.Content.Headers.ContentType?.MediaType;
            var maxBytes = command.MaxBytes ?? webPolicy.MaxResponseBytes;

            await using var stream = await response.Content.ReadAsStreamAsync(linkedCts.Token).ConfigureAwait(false);
            using var ms = new MemoryStream();

            var buffer = new byte[16 * 1024];
            var total = 0;

            while (true)
            {
                var read = await stream.ReadAsync(buffer, linkedCts.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > maxBytes)
                {
                    return Error.New($"Response exceeded max allowed bytes: {maxBytes}");
                }

                await ms.WriteAsync(buffer.AsMemory(0, read), linkedCts.Token).ConfigureAwait(false);
            }

            var body = Encoding.UTF8.GetString(ms.ToArray());

            string? text = null;
            string? rawBody = null;
            string? title = null;

            if (contentType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true)
            {
                title = HtmlTextExtractor.ExtractTitle(body);
                if (command.ExtractReadableText)
                {
                    text = HtmlTextExtractor.ExtractReadableText(body);
                }
                else
                {
                    rawBody = body;
                }
            }
            else
            {
                rawBody = body;
            }

            logger.LogInformation(
                "Fetched URL {Url} status {StatusCode} contentType {ContentType}",
                response.RequestMessage?.RequestUri,
                (int)response.StatusCode,
                contentType);

            return new FetchedPageResult(
                Url: response.RequestMessage?.RequestUri?.ToString() ?? command.Url,
                Title: title ?? string.Empty,
                Content: text ?? rawBody ?? body,
                ContentType: contentType ?? string.Empty,
                StatusCode: (int)response.StatusCode,
                FetchTimeMs: stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed fetching URL {Url}", command.Url);
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
