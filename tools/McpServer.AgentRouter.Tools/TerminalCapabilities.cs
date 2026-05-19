using System;
using System.IO;

namespace McpServer.AgentRouter.Tools;

internal static class TerminalCapabilities
{
    public static bool SupportsAnsiEscapes(TextWriter writer)
    {
        if (!ReferenceEquals(writer, Console.Out) || Console.IsOutputRedirected)
        {
            return false;
        }

        var noColor = Environment.GetEnvironmentVariable("NO_COLOR");
        if (!string.IsNullOrEmpty(noColor))
        {
            return false;
        }

        var term = Environment.GetEnvironmentVariable("TERM");
        if (!string.IsNullOrWhiteSpace(term) && !string.Equals(term, "dumb", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (OperatingSystem.IsWindows())
        {
            return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WT_SESSION"))
                || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANSICON"))
                || string.Equals(Environment.GetEnvironmentVariable("ConEmuANSI"), "ON", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
