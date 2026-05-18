using System.Net;
using System.Text.RegularExpressions;

namespace McpServer.Infrastructure.Web;

public static partial class HtmlTextExtractor
{
    public static string ExtractReadableText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var noScript = ScriptRegex().Replace(html, " ");
        var noStyle = StyleRegex().Replace(noScript, " ");
        var noTags = TagRegex().Replace(noStyle, " ");
        var decoded = WebUtility.HtmlDecode(noTags);

        return WhitespaceRegex().Replace(decoded, " ").Trim();
    }

    public static string? ExtractTitle(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var match = TitleRegex().Match(html);
        if (!match.Success)
        {
            return null;
        }

        return WebUtility.HtmlDecode(match.Groups["title"].Value).Trim();
    }

    public static IReadOnlyList<string> ExtractLinks(string html, Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return Array.Empty<string>();
        }

        var links = new List<string>();

        foreach (Match match in HrefRegex().Matches(html))
        {
            var href = match.Groups["href"].Value;
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            if (Uri.TryCreate(baseUri, href, out var resolved))
            {
                links.Add(resolved.ToString());
            }
        }

        return links.Distinct(StringComparer.Ordinal).ToArray();
    }

    [GeneratedRegex(@"<script\b[^>]*>.*?</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex ScriptRegex();

    [GeneratedRegex(@"<style\b[^>]*>.*?</style>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex StyleRegex();

    [GeneratedRegex(@"<title\b[^>]*>(?<title>.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"<a\b[^>]*href\s*=\s*[""'](?<href>[^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex HrefRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();
}
