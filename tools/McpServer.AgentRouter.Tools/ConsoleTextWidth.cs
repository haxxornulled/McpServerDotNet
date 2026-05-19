using System.Globalization;
using System.Text;

namespace McpServer.AgentRouter.Tools;

internal static class ConsoleTextWidth
{
    public static int GetDisplayWidth(string text)
    {
        var width = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            width += GetDisplayWidthElement(element);
        }

        return width;
    }

    private static int GetDisplayWidthElement(string textElement)
    {
        if (string.IsNullOrEmpty(textElement))
        {
            return 0;
        }

        var width = 0;
        foreach (var rune in textElement.EnumerateRunes())
        {
            width += GetDisplayWidth(rune);
        }

        return width;
    }

    public static int GetDisplayWidth(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
        {
            return 0;
        }

        if (rune.Value >= 0x1100 && (
            rune.Value <= 0x115F ||
            rune.Value == 0x2329 ||
            rune.Value == 0x232A ||
            (rune.Value >= 0x2E80 && rune.Value <= 0xA4CF && rune.Value != 0x303F) ||
            (rune.Value >= 0xAC00 && rune.Value <= 0xD7A3) ||
            (rune.Value >= 0xF900 && rune.Value <= 0xFAFF) ||
            (rune.Value >= 0xFE10 && rune.Value <= 0xFE19) ||
            (rune.Value >= 0xFE30 && rune.Value <= 0xFE6F) ||
            (rune.Value >= 0xFF00 && rune.Value <= 0xFF60) ||
            (rune.Value >= 0xFFE0 && rune.Value <= 0xFFE6) ||
            (rune.Value >= 0x1F300 && rune.Value <= 0x1FAFF)))
        {
            return 2;
        }

        return 1;
    }
}
