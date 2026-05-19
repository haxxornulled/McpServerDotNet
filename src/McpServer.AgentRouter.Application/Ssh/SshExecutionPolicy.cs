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
            return Succeed(Denied($"Unknown SSH profile '{profileName}'. Configure it in config/agentrouter/ssh-profiles.local.json or the user-level SSH profiles file."));
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

        var command = request.Command.Trim();
        if (ContainsPathSeparator(command))
        {
            return Succeed(Denied("command must be an executable name, not a path."));
        }

        var normalizedCommand = NormalizeCommandName(command);
        var sudoAllowed = IsSudoCommand(normalizedCommand) && profile.AllowSudoCommand;
        var deniedCommands = options.DeniedCommands.Concat(profile.DeniedCommands);
        if (!sudoAllowed && IsDeniedCommand(normalizedCommand, deniedCommands))
        {
            return Succeed(Denied($"Command '{command}' is explicitly denied."));
        }

        IEnumerable<string> allowedCommands = profile.AllowedCommands.Count > 0
            ? profile.AllowedCommands
            : options.AllowedCommands;

        if (options.RequireExplicitProfileAllowlist && !IsAllowedCommand(normalizedCommand, allowedCommands) && !sudoAllowed)
        {
            return Succeed(Denied($"Command '{command}' is not allowed for SSH profile '{profileName}'."));
        }

        var arguments = request.Arguments
            .Where(static argument => argument is not null)
            .Select(static argument => argument.Trim())
            .Where(static argument => !string.IsNullOrWhiteSpace(argument))
            .ToArray();

        if (!options.AllowShellInterpreterInlineCommands && IsShellInterpreter(normalizedCommand) && ContainsInlineCommand(arguments))
        {
            return Succeed(Denied($"Command '{command}' cannot use inline command switches until argument-aware SSH shell policy is enabled."));
        }

        var workingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory)
            ? profile.WorkingDirectory ?? string.Empty
            : request.WorkingDirectory.Trim();

        if (!IsAllowedRemoteWorkingDirectory(workingDirectory, profile.AllowedRemotePathPrefixes))
        {
            return Succeed(Denied($"Remote working directory '{workingDirectory}' is not allowed for SSH profile '{profileName}'."));
        }

        var timeoutSeconds = request.TimeoutSeconds is null
            ? options.TimeoutSeconds
            : Math.Min(Math.Max(1, request.TimeoutSeconds.Value), Math.Max(1, options.TimeoutSeconds));

        var port = profile.Port <= 0 ? 22 : profile.Port;

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
            PasswordEnvironmentVariable = profile.PasswordEnvironmentVariable,
            PrivateKeyPath = profile.PrivateKeyPath,
            PrivateKeyPassphraseEnvironmentVariable = profile.PrivateKeyPassphraseEnvironmentVariable,
            HostKeySha256 = profile.HostKeySha256,
            AcceptUnknownHostKey = profile.AcceptUnknownHostKey || options.AllowUnknownHostKeys
        });
    }

    private static bool IsAllowedRemoteWorkingDirectory(
        string workingDirectory,
        IEnumerable<string> allowedPrefixes)
    {
        var prefixes = allowedPrefixes
            .Where(static prefix => !string.IsNullOrWhiteSpace(prefix))
            .Select(static prefix => NormalizeRemotePath(prefix.Trim()))
            .ToArray();

        if (prefixes.Length == 0 || string.IsNullOrWhiteSpace(workingDirectory))
        {
            return true;
        }

        var normalized = NormalizeRemotePath(workingDirectory);
        return prefixes.Any(prefix =>
            normalized.Equals(prefix, StringComparison.Ordinal) ||
            normalized.StartsWith(prefix.EndsWith("/", StringComparison.Ordinal) ? prefix : prefix + "/", StringComparison.Ordinal));
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
        return allowedCommands
            .Where(static command => !string.IsNullOrWhiteSpace(command))
            .Select(static command => NormalizeCommandName(command.Trim()))
            .Contains(normalizedCommand, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsDeniedCommand(
        string normalizedCommand,
        IEnumerable<string> deniedCommands)
    {
        return deniedCommands
            .Where(static command => !string.IsNullOrWhiteSpace(command))
            .Select(static command => NormalizeCommandName(command.Trim()))
            .Contains(normalizedCommand, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsShellInterpreter(string normalizedCommand)
    {
        return normalizedCommand.Equals("bash", StringComparison.OrdinalIgnoreCase) ||
               normalizedCommand.Equals("sh", StringComparison.OrdinalIgnoreCase) ||
               normalizedCommand.Equals("pwsh", StringComparison.OrdinalIgnoreCase) ||
               normalizedCommand.Equals("powershell", StringComparison.OrdinalIgnoreCase) ||
               normalizedCommand.Equals("cmd", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSudoCommand(string normalizedCommand) =>
        normalizedCommand.Equals("sudo", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsInlineCommand(IEnumerable<string> arguments)
    {
        return arguments.Any(argument => InlineCommandSwitches.Contains(argument, StringComparer.OrdinalIgnoreCase));
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
}
