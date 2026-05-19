using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace McpServer.AgentRouter.Tools;

internal static class MarkdownConsoleFormatter
{
    public static IReadOnlyList<string> WrapPlainLines(IReadOnlyList<string> lines, int contentWidth)
    {
        var width = Math.Max(20, contentWidth);
        var rendered = new List<string>();

        for (var index = 0; index < lines.Count; index++)
        {
            var line = ConsoleGlyphNormalizer.ReplaceEmojiWithZero(lines[index]);
            if (string.IsNullOrEmpty(line))
            {
                rendered.Add(string.Empty);
                continue;
            }

            rendered.AddRange(WrapParagraph(line, width));
        }

        if (rendered.Count == 0)
        {
            rendered.Add(string.Empty);
        }

        return rendered;
    }

    public static IReadOnlyList<string> RenderMarkdown(string? text, int contentWidth)
    {
        var width = Math.Max(20, contentWidth);
        var rendered = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            rendered.Add(string.Empty);
            return rendered;
        }

        var lines = SplitLines(text);
        var paragraph = new StringBuilder();
        var inCodeFence = false;
        var fenceMarker = '`';
        var fenceLanguage = string.Empty;

        for (var index = 0; index < lines.Count; index++)
        {
            var line = ConsoleGlyphNormalizer.ReplaceEmojiWithZero(lines[index]);
            var trimmed = line.TrimStart();

            if (!inCodeFence && TryParseFenceStart(trimmed, out fenceMarker, out fenceLanguage))
            {
                FlushParagraph(paragraph, rendered, width);
                AddBlankLine(rendered);

                rendered.Add(string.IsNullOrWhiteSpace(fenceLanguage)
                    ? "Code block"
                    : FormattableString.Invariant($"Code: {fenceLanguage}"));

                inCodeFence = true;
                continue;
            }

            if (inCodeFence && TryParseFenceEnd(trimmed, fenceMarker))
            {
                inCodeFence = false;
                AddBlankLine(rendered);
                continue;
            }

            if (inCodeFence)
            {
                rendered.Add(ConsoleGlyphNormalizer.ReplaceEmojiWithZero(line).Replace("\t", "    "));
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                FlushParagraph(paragraph, rendered, width);
                AddBlankLine(rendered);
                continue;
            }

            if (TryParseHeading(trimmed, out var heading))
            {
                FlushParagraph(paragraph, rendered, width);
                AddBlankLine(rendered);
                rendered.AddRange(WrapParagraph(heading, width));
                AddBlankLine(rendered);
                continue;
            }

            if (TryParseListItem(trimmed, out var prefix, out var body))
            {
                FlushParagraph(paragraph, rendered, width);
                rendered.AddRange(WrapWithPrefix(prefix, body, width));
                continue;
            }

            if (TryParseQuote(trimmed, out var quote))
            {
                FlushParagraph(paragraph, rendered, width);
                rendered.AddRange(WrapWithPrefix("> ", quote, width));
                continue;
            }

            if (paragraph.Length > 0)
            {
                paragraph.Append(' ');
            }

            paragraph.Append(trimmed);
        }

        FlushParagraph(paragraph, rendered, width);
        TrimTrailingBlankLines(rendered);

        if (rendered.Count == 0)
        {
            rendered.Add(string.Empty);
        }

        return rendered;
    }

    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var current = new StringBuilder();

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '\r')
            {
                continue;
            }

            if (character == '\n')
            {
                lines.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        lines.Add(current.ToString());
        return lines;
    }

    private static void FlushParagraph(StringBuilder paragraph, List<string> rendered, int width)
    {
        if (paragraph.Length == 0)
        {
            return;
        }

        rendered.AddRange(WrapParagraph(paragraph.ToString(), width));
        paragraph.Clear();
    }

    private static void AddBlankLine(ICollection<string> rendered)
    {
        if (rendered is List<string> list)
        {
            if (list.Count > 0 && list[^1].Length > 0)
            {
                list.Add(string.Empty);
            }

            return;
        }

        rendered.Add(string.Empty);
    }

    private static void TrimTrailingBlankLines(List<string> rendered)
    {
        while (rendered.Count > 0 && rendered[^1].Length == 0)
        {
            rendered.RemoveAt(rendered.Count - 1);
        }
    }

    private static bool TryParseFenceStart(string line, out char fenceMarker, out string fenceLanguage)
    {
        fenceMarker = default;
        fenceLanguage = string.Empty;

        if (line.Length < 3)
        {
            return false;
        }

        if (line.StartsWith("```", StringComparison.Ordinal))
        {
            fenceMarker = '`';
            fenceLanguage = line[3..].Trim();
            return true;
        }

        if (line.StartsWith("~~~", StringComparison.Ordinal))
        {
            fenceMarker = '~';
            fenceLanguage = line[3..].Trim();
            return true;
        }

        return false;
    }

    private static bool TryParseFenceEnd(string line, char fenceMarker)
    {
        if (fenceMarker == '`')
        {
            return line.StartsWith("```", StringComparison.Ordinal);
        }

        if (fenceMarker == '~')
        {
            return line.StartsWith("~~~", StringComparison.Ordinal);
        }

        return false;
    }

    private static bool TryParseHeading(string line, out string heading)
    {
        heading = string.Empty;
        if (!line.StartsWith("#", StringComparison.Ordinal))
        {
            return false;
        }

        var index = 0;
        while (index < line.Length && line[index] == '#')
        {
            index++;
        }

        if (index == 0 || index >= line.Length)
        {
            return false;
        }

        if (line[index] != ' ')
        {
            return false;
        }

        heading = line[(index + 1)..].Trim();
        return heading.Length > 0;
    }

    private static bool TryParseListItem(string line, out string prefix, out string body)
    {
        prefix = string.Empty;
        body = string.Empty;

        if (line.Length < 2)
        {
            return false;
        }

        if (line[0] is '-' or '*' or '+'
            && line[1] == ' ')
        {
            prefix = "• ";
            body = line[2..].Trim();
            return body.Length > 0;
        }

        var index = 0;
        while (index < line.Length && char.IsDigit(line[index]))
        {
            index++;
        }

        if (index == 0 || index + 1 >= line.Length)
        {
            return false;
        }

        if (line[index] is not '.' and not ')'
            || line[index + 1] != ' ')
        {
            return false;
        }

        prefix = line[..(index + 2)];
        body = line[(index + 2)..].Trim();
        return body.Length > 0;
    }

    private static bool TryParseQuote(string line, out string quote)
    {
        quote = string.Empty;
        if (!line.StartsWith(">", StringComparison.Ordinal))
        {
            return false;
        }

        quote = line[1..].TrimStart();
        return quote.Length > 0;
    }

    private static IReadOnlyList<string> WrapParagraph(string text, int width)
    {
        var rendered = new List<string>();
        text = ConsoleGlyphNormalizer.ReplaceEmojiWithZero(NormalizeInlineMarkdown(text));
        if (string.IsNullOrWhiteSpace(text))
        {
            rendered.Add(string.Empty);
            return rendered;
        }

        var normalized = text.Replace('\t', ' ');
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var current = new StringBuilder();
        var currentWidth = 0;

        foreach (var word in words)
        {
            var wordWidth = ConsoleTextWidth.GetDisplayWidth(word);
            if (wordWidth > width)
            {
                if (current.Length > 0)
                {
                    rendered.Add(current.ToString());
                    current.Clear();
                    currentWidth = 0;
                }

                foreach (var chunk in SplitByWidth(word, width))
                {
                    rendered.Add(chunk);
                }

                continue;
            }

            if (current.Length == 0)
            {
                current.Append(word);
                currentWidth = wordWidth;
                continue;
            }

            if (currentWidth + 1 + wordWidth <= width)
            {
                current.Append(' ');
                current.Append(word);
                currentWidth += 1 + wordWidth;
                continue;
            }

            rendered.Add(current.ToString());
            current.Clear();
            current.Append(word);
            currentWidth = wordWidth;
        }

        if (current.Length > 0)
        {
            rendered.Add(current.ToString());
        }

        if (rendered.Count == 0)
        {
            rendered.Add(string.Empty);
        }

        return rendered;
    }

    private static IReadOnlyList<string> WrapWithPrefix(string prefix, string body, int width)
    {
        var rendered = new List<string>();
        body = ConsoleGlyphNormalizer.ReplaceEmojiWithZero(NormalizeInlineMarkdown(body));
        var prefixWidth = ConsoleTextWidth.GetDisplayWidth(prefix);
        var bodyWidth = Math.Max(1, width - prefixWidth);
        var wrapped = WrapParagraph(body, bodyWidth);

        for (var index = 0; index < wrapped.Count; index++)
        {
            rendered.Add(index == 0 ? prefix + wrapped[index] : new string(' ', prefixWidth) + wrapped[index]);
        }

        if (rendered.Count == 0)
        {
            rendered.Add(prefix.TrimEnd());
        }

        return rendered;
    }

    private static IReadOnlyList<string> SplitByWidth(string text, int width)
    {
        var chunks = new List<string>();
        var current = new StringBuilder();
        var currentWidth = 0;

        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            var elementWidth = ConsoleTextWidth.GetDisplayWidth(element);
            if (currentWidth > 0 && currentWidth + elementWidth > width)
            {
                chunks.Add(current.ToString());
                current.Clear();
                currentWidth = 0;
            }

            current.Append(element);
            currentWidth += elementWidth;
        }

        if (current.Length > 0)
        {
            chunks.Add(current.ToString());
        }

        if (chunks.Count == 0)
        {
            chunks.Add(string.Empty);
        }

        return chunks;
    }

    private static string NormalizeInlineMarkdown(string text)
    {
        var normalized = text;
        normalized = Regex.Replace(normalized, @"\*\*(.+?)\*\*", "$1");
        normalized = Regex.Replace(normalized, @"__(.+?)__", "$1");
        normalized = Regex.Replace(normalized, @"(?<!\w)\*(.+?)\*(?!\w)", "$1");
        normalized = Regex.Replace(normalized, @"(?<!\w)_(.+?)_(?!\w)", "$1");
        normalized = Regex.Replace(normalized, @"`([^`]+)`", "$1");
        normalized = Regex.Replace(normalized, @"\[(.+?)\]\((.+?)\)", "$1 ($2)");
        return normalized;
    }
}
