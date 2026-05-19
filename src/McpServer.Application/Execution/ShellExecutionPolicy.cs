using LanguageExt;
using LanguageExt.Common;
using McpServer.Application.Mcp.Tools;
using static LanguageExt.Prelude;

namespace McpServer.Application.Execution;

public sealed class ShellExecutionPolicy : IShellExecutionPolicy
{
    private readonly ShellExecutionPolicyOptions _options;
    private readonly System.Collections.Generic.HashSet<string> _allowedCommands;
    private readonly System.Collections.Generic.HashSet<string> _deniedCommands;

    public ShellExecutionPolicy(ShellExecutionPolicyOptions options)
    {
        _options = options;
        _allowedCommands = new System.Collections.Generic.HashSet<string>(
            options.AllowedCommands.Where(static command => !string.IsNullOrWhiteSpace(command)),
            StringComparer.OrdinalIgnoreCase);
        _deniedCommands = new System.Collections.Generic.HashSet<string>(
            options.DeniedCommands.Where(static command => !string.IsNullOrWhiteSpace(command)),
            StringComparer.OrdinalIgnoreCase);
    }

    public Fin<Unit> Validate(
        ShellExecRequest request,
        bool requiresShellFallback)
    {
        if (string.IsNullOrWhiteSpace(request.Command))
        {
            return Error.New("Command is required.");
        }

        if (request.TimeoutSeconds < 1 || request.TimeoutSeconds > _options.MaxTimeoutSeconds)
        {
            return Error.New($"Command timeout must be between 1 and {_options.MaxTimeoutSeconds} seconds.");
        }

        if (request.MaxOutputChars < 256 || request.MaxOutputChars > _options.MaxOutputChars)
        {
            return Error.New($"Max output chars must be between 256 and {_options.MaxOutputChars}.");
        }

        var executable = ExtractExecutableName(request.Command);

        if (_deniedCommands.Contains(executable))
        {
            return Error.New($"Command '{executable}' is denied by shell execution policy.");
        }

        if (_allowedCommands.Count == 0)
        {
            return Error.New("Shell execution requires an explicit command allowlist.");
        }

        if (!_allowedCommands.Contains(executable))
        {
            return Error.New($"Command '{executable}' is not in the configured shell allowlist.");
        }

        if (requiresShellFallback && !_options.AllowShellFallback)
        {
            return Error.New("Bare shell command lines are disabled by shell execution policy. Pass an executable plus args, or enable AllowShellFallback explicitly.");
        }

        return unit;
    }

    private static string ExtractExecutableName(string command)
    {
        var trimmed = command.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var first = ExtractFirstCommandToken(trimmed);
        return Path.GetFileNameWithoutExtension(first.Trim('"', '\'')).ToLowerInvariant();
    }

    private static string ExtractFirstCommandToken(string command)
    {
        if (command.Length == 0)
        {
            return string.Empty;
        }

        var quote = command[0];
        if (quote is '"' or '\'')
        {
            var closingQuoteIndex = command.IndexOf(quote, startIndex: 1);
            if (closingQuoteIndex > 1)
            {
                return command[..(closingQuoteIndex + 1)];
            }
        }

        var separatorIndex = command.IndexOfAny([' ', '\t']);
        return separatorIndex < 0 ? command : command[..separatorIndex];
    }
}
