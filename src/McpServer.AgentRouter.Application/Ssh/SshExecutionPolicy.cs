using LanguageExt;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.Runtime;
using McpServer.AgentRouter.Domain.Ssh;

namespace McpServer.AgentRouter.Application.Ssh;

/// <summary>
/// Evaluates SSH execution requests against policy and profile rules.
/// </summary>
public sealed class SshExecutionPolicy : ISshExecutionPolicy
{
    private static readonly string[] InlineCommandSwitches =
    [
        "-c",
        "--command",
        "-command",
        "/c"
    ];

    private readonly AgentRouterRuntimeSettings _settings;
    private readonly ISshProfileStore _profileStore;

    /// <summary>
    /// Initializes a new SSH execution policy.
    /// </summary>
    public SshExecutionPolicy(
        AgentRouterRuntimeSettings settings,
        ISshProfileStore profileStore)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
    }

    /// <summary>
    /// Evaluates the supplied SSH request.
    /// </summary>
    public async ValueTask<Fin<SshExecutionPolicyDecision>> EvaluateAsync(
        SshExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var options = _settings.SshExecution;
        if (!options.Enabled)
        {
            return Succeed(Denied("SSH execution is disabled by configuration."));
        }

        if (string.IsNullOrWhiteSpace(request.Profile))
        {
            return Succeed(Denied("profile is required."));
        }

        var profileName = request.Profile.Trim();
        var catalogResult = await _profileStore.LoadProfilesAsync(cancellationToken).ConfigureAwait(false);
        if (catalogResult.IsFail)
        {
            return catalogResult.Match<Fin<SshExecutionPolicyDecision>>(
                Succ: _ => throw new InvalidOperationException("Unexpected SSH profile catalog success while handling failure."),
                Fail: error => error);
        }

        var catalog = catalogResult.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected SSH profile catalog failure while handling success."));

        if (!catalog.Profiles.TryGetValue(profileName, out var profile))
        {
            return Succeed(Denied($"Unknown SSH profile '{profileName}'. Configure it in config/mcpserver/ssh-profiles.local.json or the user-level SSH profiles file."));
        }

        if (string.IsNullOrWhiteSpace(profile.Host))
        {
            return Succeed(Denied($"SSH profile '{profileName}' is missing Host."));
        }

        if (string.IsNullOrWhiteSpace(profile.Username))
        {
            return Succeed(Denied($"SSH profile '{profileName}' is missing Username."));
        }

        if (string.IsNullOrWhiteSpace(request.Command))
        {
            return Succeed(Denied("command is required."));
        }

        var parsedCommand = ParseCommand(request.Command);
        var command = parsedCommand.Command;
        if (ContainsPathSeparator(command))
        {
            return Succeed(Denied("command must be an executable name, not a path."));
        }

        var arguments = BuildArguments(parsedCommand.Arguments, request.Arguments);

        var normalizedCommand = NormalizeCommandName(command);
        var allowAllCommands = profile.AllowAllCommands;
        var workingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory)
            ? profile.WorkingDirectory ?? string.Empty
            : request.WorkingDirectory.Trim();

        var timeoutSeconds = request.TimeoutSeconds is null
            ? options.TimeoutSeconds
            : Math.Min(Math.Max(1, request.TimeoutSeconds.Value), Math.Max(1, options.TimeoutSeconds));

        var port = profile.Port <= 0 ? 22 : profile.Port;

        var sudoAllowed = IsSudoCommand(normalizedCommand) && (profile.AllowSudoCommand || allowAllCommands);
        var deniedCommands = BuildMergedStrings(options.DeniedCommands, profile.DeniedCommands);
        if (!allowAllCommands && !sudoAllowed && IsDeniedCommand(normalizedCommand, deniedCommands))
        {
            return Succeed(Denied($"Command '{command}' is explicitly denied."));
        }

        if (allowAllCommands)
        {
            return Succeed(new SshExecutionPolicyDecision
            {
                Allowed = true,
                Decision = "allowed",
                ProfileName = profileName,
                Host = profile.Host.Trim(),
                Port = port,
                Username = profile.Username.Trim(),
                ResolvedCommand = command,
                ResolvedArguments = arguments,
                WorkingDirectory = workingDirectory,
                TimeoutSeconds = timeoutSeconds,
                MaxOutputChars = Math.Max(1, options.MaxOutputChars),
                PasswordVaultItemName = profile.PasswordVaultItemName,
                PrivateKeyPath = profile.PrivateKeyPath,
                PrivateKeyPassphraseVaultItemName = profile.PrivateKeyPassphraseVaultItemName,
                HostKeySha256 = profile.HostKeySha256,
                AcceptUnknownHostKey = profile.AcceptUnknownHostKey || options.AllowUnknownHostKeys
            });
        }

        IEnumerable<string> allowedCommands = profile.AllowedCommands.Count > 0
            ? profile.AllowedCommands
            : options.AllowedCommands;

        if (options.RequireExplicitProfileAllowlist && !IsAllowedCommand(normalizedCommand, allowedCommands) && !sudoAllowed)
        {
            return Succeed(Denied($"Command '{command}' is not allowed for SSH profile '{profileName}'."));
        }

        if (!options.AllowShellInterpreterInlineCommands && IsShellInterpreter(normalizedCommand) && ContainsInlineCommand(arguments))
        {
            return Succeed(Denied($"Command '{command}' cannot use inline command switches until argument-aware SSH shell policy is enabled."));
        }

        if (!IsAllowedRemoteWorkingDirectory(workingDirectory, profile.AllowedRemotePathPrefixes))
        {
            return Succeed(Denied($"Remote working directory '{workingDirectory}' is not allowed for SSH profile '{profileName}'."));
        }

        return Succeed(new SshExecutionPolicyDecision
        {
            Allowed = true,
            Decision = "allowed",
            ProfileName = profileName,
            Host = profile.Host.Trim(),
            Port = port,
            Username = profile.Username.Trim(),
            ResolvedCommand = command,
            ResolvedArguments = arguments,
            WorkingDirectory = workingDirectory,
            TimeoutSeconds = timeoutSeconds,
            MaxOutputChars = Math.Max(1, options.MaxOutputChars),
            PasswordVaultItemName = profile.PasswordVaultItemName,
            PrivateKeyPath = profile.PrivateKeyPath,
            PrivateKeyPassphraseVaultItemName = profile.PrivateKeyPassphraseVaultItemName,
            HostKeySha256 = profile.HostKeySha256,
            AcceptUnknownHostKey = profile.AcceptUnknownHostKey || options.AllowUnknownHostKeys
        });
    }

    private static ParsedCommand ParseCommand(string commandText)
    {
        var parts = commandText
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
        {
            return new ParsedCommand(string.Empty, Array.Empty<string>());
        }

        if (parts.Length == 1)
        {
            return new ParsedCommand(parts[0], Array.Empty<string>());
        }

        var arguments = new string[parts.Length - 1];
        Array.Copy(parts, 1, arguments, 0, arguments.Length);
        return new ParsedCommand(parts[0], arguments);
    }

    private static bool IsAllowedRemoteWorkingDirectory(
        string workingDirectory,
        IEnumerable<string> allowedPrefixes)
    {
        var prefixes = NormalizeStrings(allowedPrefixes);

        if (prefixes.Length == 0 || string.IsNullOrWhiteSpace(workingDirectory))
        {
            return true;
        }

        var normalized = NormalizeRemotePath(workingDirectory);
        foreach (var prefix in prefixes)
        {
            if (normalized.Equals(prefix, StringComparison.Ordinal) ||
                normalized.StartsWith(prefix.EndsWith("/", StringComparison.Ordinal) ? prefix : prefix + "/", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeRemotePath(string value)
    {
        var replaced = value.Replace('\\', '/').Trim();
        while (replaced.Contains("//", StringComparison.Ordinal))
        {
            replaced = replaced.Replace("//", "/", StringComparison.Ordinal);
        }

        return replaced.TrimEnd('/');
    }

    private static bool ContainsPathSeparator(string command)
    {
        return command.Contains(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
               command.Contains(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
               command.Contains("/", StringComparison.Ordinal);
    }

    private static string NormalizeCommandName(string command)
    {
        var fileName = Path.GetFileName(command.Trim());
        return fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^4]
            : fileName;
    }

    private static bool IsAllowedCommand(
        string normalizedCommand,
        IEnumerable<string> allowedCommands)
    {
        foreach (var command in allowedCommands)
        {
            if (!string.IsNullOrWhiteSpace(command) &&
                string.Equals(NormalizeCommandName(command.Trim()), normalizedCommand, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDeniedCommand(
        string normalizedCommand,
        IEnumerable<string> deniedCommands)
    {
        foreach (var command in deniedCommands)
        {
            if (!string.IsNullOrWhiteSpace(command) &&
                string.Equals(NormalizeCommandName(command.Trim()), normalizedCommand, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsShellInterpreter(string normalizedCommand)
    {
        return normalizedCommand.Equals("bash", StringComparison.OrdinalIgnoreCase) ||
               normalizedCommand.Equals("sh", StringComparison.OrdinalIgnoreCase) ||
               normalizedCommand.Equals("cmd", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSudoCommand(string normalizedCommand) =>
        normalizedCommand.Equals("sudo", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsInlineCommand(IEnumerable<string> arguments)
    {
        foreach (var argument in arguments)
        {
            for (var i = 0; i < InlineCommandSwitches.Length; i++)
            {
                if (string.Equals(argument, InlineCommandSwitches[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static SshExecutionPolicyDecision Denied(string reason)
    {
        return new SshExecutionPolicyDecision
        {
            Allowed = false,
            Decision = "denied",
            Reason = reason
        };
    }

    private static Fin<SshExecutionPolicyDecision> Succeed(SshExecutionPolicyDecision decision)
    {
        return Fin<SshExecutionPolicyDecision>.Succ(decision);
    }

    private static string[] BuildArguments(IReadOnlyList<string> parsedArguments, IEnumerable<string?> requestArguments)
    {
        var buffer = new List<string>();

        for (var i = 0; i < parsedArguments.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(parsedArguments[i]))
            {
                buffer.Add(parsedArguments[i]);
            }
        }

        foreach (var argument in requestArguments)
        {
            if (!string.IsNullOrWhiteSpace(argument))
            {
                buffer.Add(argument.Trim());
            }
        }

        return buffer.ToArray();
    }

    private static string[] BuildMergedStrings(IEnumerable<string> first, IEnumerable<string> second)
    {
        var values = new List<string>();
        AppendStrings(values, first);
        AppendStrings(values, second);
        return values.ToArray();
    }

    private static string[] NormalizeStrings(IEnumerable<string> values)
    {
        var list = new List<string>();
        AppendStrings(list, values);
        return list.ToArray();
    }

    private static void AppendStrings(List<string> target, IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                target.Add(NormalizeRemotePath(value.Trim()));
            }
        }
    }

    private sealed record ParsedCommand(string Command, IReadOnlyList<string> Arguments);
}
