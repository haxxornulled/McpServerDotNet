using System.Globalization;
using System.Text;

namespace McpServer.AgentRouter.Tools;

internal static class ConsoleGlyphNormalizer
{
    public static string ReplaceEmojiWithZero(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            builder.Append(IsEmojiLikeTextElement(element) ? '0' : element);
        }

        return builder.ToString();
    }

    private static bool IsEmojiLikeTextElement(string textElement)
    {
        if (string.IsNullOrEmpty(textElement))
        {
            return false;
        }

        var sawEmojiRange = false;
        foreach (var rune in textElement.EnumerateRunes())
        {
            if (rune.Value is 0x200D or 0xFE0F or 0xFE0E)
            {
                return true;
            }

            if (rune.Value is >= 0x1F000 and <= 0x1FAFF)
            {
                sawEmojiRange = true;
                continue;
            }

            if (rune.Value is >= 0x2600 and <= 0x27BF)
            {
                sawEmojiRange = true;
                continue;
            }

            if (rune.Value is >= 0x1F1E6 and <= 0x1F1FF)
            {
                sawEmojiRange = true;
                continue;
            }
        }

        return sawEmojiRange;
    }
}
