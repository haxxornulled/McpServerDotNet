namespace McpServer.Application.Mcp.Validation;

public static class ToolRequestValidation
{
    private static readonly char[] InvalidCommandCharacters = ['\0', '\r', '\n'];
    private static readonly char[] ShellControlCharacters = ['&', '|', ';', '>', '<', '`', '$'];

    public static void RequireNonWhiteSpace(List<string> errors, string? value, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"Argument '{argumentName}' is required and cannot be empty.");
        }
    }

    public static void RequireUri(List<string> errors, string? value, string argumentName)
    {
        RequireNonWhiteSpace(errors, value, argumentName);
        if (!string.IsNullOrWhiteSpace(value) && !Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            errors.Add($"Argument '{argumentName}' must be an absolute URI.");
        }
    }

    public static void RequireRange(List<string> errors, int value, string argumentName, int min, int max)
    {
        if (value < min || value > max)
        {
            errors.Add($"Argument '{argumentName}' must be between {min} and {max}.");
        }
    }

    public static void RequireNotRootLikePath(List<string> errors, string? value, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmed = value.Trim();
        if (trimmed is "/" or "\\" || Path.GetPathRoot(trimmed)?.Equals(trimmed, StringComparison.OrdinalIgnoreCase) == true)
        {
            errors.Add($"Argument '{argumentName}' cannot target a filesystem root.");
        }
    }

    public static void RequireExecutableOnly(List<string> errors, string? value, string argumentName)
    {
        RequireNonWhiteSpace(errors, value, argumentName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmed = value.Trim();
        if (trimmed.IndexOfAny(InvalidCommandCharacters) >= 0)
        {
            errors.Add($"Argument '{argumentName}' cannot contain embedded nulls or line breaks.");
        }

        if (trimmed.Contains(' ') || trimmed.Contains('\t'))
        {
            errors.Add($"Argument '{argumentName}' must only contain the executable name or path. Pass command arguments via 'args'.");
        }

        if (trimmed.IndexOfAny(ShellControlCharacters) >= 0)
        {
            errors.Add($"Argument '{argumentName}' cannot contain shell control characters.");
        }
    }

    public static void RequireShellSafeCommandText(List<string> errors, string? value, string argumentName)
    {
        RequireNonWhiteSpace(errors, value, argumentName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmed = value.Trim();
        if (trimmed.IndexOfAny(InvalidCommandCharacters) >= 0)
        {
            errors.Add($"Argument '{argumentName}' cannot contain embedded nulls or line breaks.");
        }

        if (trimmed.IndexOfAny(ShellControlCharacters) >= 0)
        {
            errors.Add($"Argument '{argumentName}' cannot contain shell control characters. Pass arguments separately instead of using shell operators.");
        }
    }

    public static void RequireSafeArgumentValues(List<string> errors, IEnumerable<string>? values, string argumentName)
    {
        if (values is null)
        {
            return;
        }

        var index = 0;
        foreach (var value in values)
        {
            if (value is not null && value.IndexOfAny(InvalidCommandCharacters) >= 0)
            {
                errors.Add($"Argument '{argumentName}[{index}]' cannot contain embedded nulls or line breaks.");
            }

            index++;
        }
    }
}
