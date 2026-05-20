using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Markdig;

namespace McpServer.AgentRouter.Tools;

internal static class MarkdownConsoleFormatter
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

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
        if (string.IsNullOrWhiteSpace(text))
        {
            rendered.Add(string.Empty);
            return rendered;
        }

        var html = Markdown.ToHtml(text, Pipeline);
        var parser = new HtmlParser();
        var document = parser.ParseDocument(html);
        var root = document.Body ?? document.DocumentElement;

        if (root is null)
        {
            rendered.Add(string.Empty);
            return rendered;
        }

        RenderBlockChildren(root.ChildNodes, rendered, width);
        TrimTrailingBlankLines(rendered);

        if (rendered.Count == 0)
        {
            rendered.Add(string.Empty);
        }

        return rendered;
    }

    private static void RenderBlockChildren(INodeList nodes, List<string> rendered, int width)
    {
        for (var index = 0; index < nodes.Length; index++)
        {
            if (nodes[index] is not IElement element)
            {
                continue;
            }

            RenderElement(element, rendered, width);
        }
    }

    private static void RenderElement(IElement element, List<string> rendered, int width)
    {
        var tag = element.TagName.ToUpperInvariant();
        switch (tag)
        {
            case "H1":
            case "H2":
            case "H3":
            case "H4":
            case "H5":
            case "H6":
                FlushHeading(element, rendered, width);
                break;
            case "P":
                FlushParagraph(element, rendered, width);
                break;
            case "BLOCKQUOTE":
                FlushBlockQuote(element, rendered, width);
                break;
            case "UL":
            case "OL":
                FlushList(element, rendered, width, tag == "OL");
                break;
            case "PRE":
                FlushCodeBlock(element, rendered, width);
                break;
            case "HR":
                AddBlankLine(rendered);
                rendered.Add("─");
                AddBlankLine(rendered);
                break;
            case "TABLE":
                FlushTable(element, rendered, width);
                break;
            default:
                RenderBlockChildren(element.ChildNodes, rendered, width);
                break;
        }
    }

    private static void FlushHeading(IElement element, List<string> rendered, int width)
    {
        FlushInlineBlock(element, rendered, width, addBlankLines: true);
    }

    private static void FlushParagraph(IElement element, List<string> rendered, int width)
    {
        var text = RenderInlineText(element);
        if (string.IsNullOrWhiteSpace(text))
        {
            AddBlankLine(rendered);
            return;
        }

        rendered.AddRange(WrapParagraph(text, width));
        AddBlankLine(rendered);
    }

    private static void FlushBlockQuote(IElement element, List<string> rendered, int width)
    {
        var bodyLines = new List<string>();
        if (HasBlockChildren(element))
        {
            RenderBlockChildren(element.ChildNodes, bodyLines, Math.Max(1, width - 2));
        }
        else
        {
            bodyLines.AddRange(WrapParagraph(RenderInlineText(element), Math.Max(1, width - 2)));
        }

        PrefixLines(rendered, bodyLines, "> ");
        AddBlankLine(rendered);
    }

    private static void FlushList(IElement element, List<string> rendered, int width, bool ordered)
    {
        var itemIndex = 1;
        foreach (var child in element.Children)
        {
            if (!string.Equals(child.TagName, "LI", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var prefix = ordered ? FormattableString.Invariant($"{itemIndex}. ") : "• ";
            var prefixWidth = ConsoleTextWidth.GetDisplayWidth(prefix);
            var bodyWidth = Math.Max(1, width - prefixWidth);
            var itemLines = new List<string>();
            if (HasBlockChildren(child))
            {
                RenderBlockChildren(child.ChildNodes, itemLines, bodyWidth);
            }
            else
            {
                itemLines.AddRange(WrapParagraph(RenderInlineText(child), bodyWidth));
            }

            if (itemLines.Count == 0)
            {
                itemLines.Add(string.Empty);
            }

            rendered.Add(prefix + itemLines[0]);
            for (var index = 1; index < itemLines.Count; index++)
            {
                rendered.Add(new string(' ', prefixWidth) + itemLines[index]);
            }

            itemIndex++;
        }

        AddBlankLine(rendered);
    }

    private static void FlushCodeBlock(IElement element, List<string> rendered, int width)
    {
        var code = element.QuerySelector("code");
        var language = ExtractLanguage(code?.ClassName);
        var content = code?.TextContent ?? element.TextContent;

        AddBlankLine(rendered);
        rendered.Add(string.IsNullOrWhiteSpace(language)
            ? "Code block"
            : FormattableString.Invariant($"Code: {language}"));

        var lines = SplitLines(content);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = ConsoleGlyphNormalizer.ReplaceEmojiWithZero(lines[index]).Replace("\t", "    ", StringComparison.Ordinal);
            rendered.Add(line);
        }

        AddBlankLine(rendered);
    }

    private static void FlushTable(IElement element, List<string> rendered, int width)
    {
        var rows = element.QuerySelectorAll("tr");
        if (rows.Length == 0)
        {
            RenderBlockChildren(element.ChildNodes, rendered, width);
            return;
        }

        AddBlankLine(rendered);
        foreach (var row in rows)
        {
            var cells = new List<string>();
            foreach (var cell in row.Children)
            {
                var cellText = RenderInlineText(cell);
                cells.Add(string.IsNullOrWhiteSpace(cellText) ? string.Empty : cellText);
            }

            rendered.Add(string.Join(" | ", cells));
        }
        AddBlankLine(rendered);
    }

    private static void FlushInlineBlock(IElement element, List<string> rendered, int width, bool addBlankLines)
    {
        var text = RenderInlineText(element);
        if (string.IsNullOrWhiteSpace(text))
        {
            if (addBlankLines)
            {
                AddBlankLine(rendered);
            }

            return;
        }

        if (addBlankLines)
        {
            AddBlankLine(rendered);
        }

        rendered.AddRange(WrapParagraph(text, width));

        if (addBlankLines)
        {
            AddBlankLine(rendered);
        }
    }

    private static void PrefixLines(List<string> rendered, IReadOnlyList<string> lines, string prefix)
    {
        var prefixWidth = ConsoleTextWidth.GetDisplayWidth(prefix);
        for (var index = 0; index < lines.Count; index++)
        {
            rendered.Add(index == 0 ? prefix + lines[index] : new string(' ', prefixWidth) + lines[index]);
        }
    }

    private static string RenderInlineText(IElement element)
    {
        var builder = new StringBuilder();
        RenderInlineNodes(element.ChildNodes, builder);
        return NormalizeInlineText(builder.ToString());
    }

    private static void RenderInlineNodes(INodeList nodes, StringBuilder builder)
    {
        for (var index = 0; index < nodes.Length; index++)
        {
            var node = nodes[index];
            if (node is IElement element)
            {
                RenderInlineElement(element, builder);
                continue;
            }

            var text = node.TextContent;
            if (!string.IsNullOrEmpty(text))
            {
                builder.Append(text);
            }
        }
    }

    private static void RenderInlineElement(IElement element, StringBuilder builder)
    {
        var tag = element.TagName.ToUpperInvariant();
        switch (tag)
        {
            case "A":
            {
                var label = new StringBuilder();
                RenderInlineNodes(element.ChildNodes, label);
                var href = element.GetAttribute("href");
                if (!string.IsNullOrWhiteSpace(href))
                {
                    builder.Append(label);
                    builder.Append(" (");
                    builder.Append(href);
                    builder.Append(')');
                }
                else
                {
                    builder.Append(label);
                }

                break;
            }
            case "CODE":
                builder.Append(element.TextContent);
                break;
            case "BR":
                builder.Append(' ');
                break;
            default:
                RenderInlineNodes(element.ChildNodes, builder);
                break;
        }
    }

    private static string NormalizeInlineText(string text)
    {
        var normalized = ConsoleGlyphNormalizer.ReplaceEmojiWithZero(text);
        var builder = new StringBuilder(normalized.Length);
        var inWhitespace = false;

        for (var index = 0; index < normalized.Length; index++)
        {
            var character = normalized[index];
            if (char.IsWhiteSpace(character))
            {
                inWhitespace = true;
                continue;
            }

            if (inWhitespace && builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(character);
            inWhitespace = false;
        }

        return builder.ToString().Trim();
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

    private static string ExtractLanguage(string? className)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            return string.Empty;
        }

        var classes = className.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < classes.Length; index++)
        {
            var css = classes[index];
            if (css.StartsWith("language-", StringComparison.OrdinalIgnoreCase))
            {
                return css["language-".Length..];
            }
        }

        return string.Empty;
    }

    private static bool HasBlockChildren(IElement element)
    {
        foreach (var child in element.ChildNodes)
        {
            if (child is IElement childElement && IsBlockTag(childElement.TagName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBlockTag(string tagName)
    {
        return tagName.Equals("P", StringComparison.OrdinalIgnoreCase)
            || tagName.Equals("BLOCKQUOTE", StringComparison.OrdinalIgnoreCase)
            || tagName.Equals("UL", StringComparison.OrdinalIgnoreCase)
            || tagName.Equals("OL", StringComparison.OrdinalIgnoreCase)
            || tagName.Equals("PRE", StringComparison.OrdinalIgnoreCase)
            || tagName.Equals("TABLE", StringComparison.OrdinalIgnoreCase)
            || tagName.Equals("HR", StringComparison.OrdinalIgnoreCase)
            || tagName.Equals("H1", StringComparison.OrdinalIgnoreCase)
            || tagName.Equals("H2", StringComparison.OrdinalIgnoreCase)
            || tagName.Equals("H3", StringComparison.OrdinalIgnoreCase)
            || tagName.Equals("H4", StringComparison.OrdinalIgnoreCase)
            || tagName.Equals("H5", StringComparison.OrdinalIgnoreCase)
            || tagName.Equals("H6", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> WrapParagraph(string text, int width)
    {
        var rendered = new List<string>();
        text = NormalizeInlineText(text);
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
}
