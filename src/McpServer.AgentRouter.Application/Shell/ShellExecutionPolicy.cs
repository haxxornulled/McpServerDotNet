using LanguageExt;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.Runtime;
using McpServer.AgentRouter.Domain.Shell;

namespace McpServer.AgentRouter.Application.Shell;

/// <summary>
/// Evaluates shell execution requests against policy and workspace rules.
/// </summary>
public sealed class ShellExecutionPolicy : IShellExecutionPolicy
{
    private static readonly string[] InlineCommandSwitches =
    [
        "-c",
        "--command",
        "-command",
        "/c"
    ];

    private readonly IAgentRouterRuntimePathResolver _pathResolver;
    private readonly AgentRouterRuntimeSettings _settings;

    /// <summary>
    /// Initializes a new shell execution policy.
    /// </summary>
    public ShellExecutionPolicy(
        IAgentRouterRuntimePathResolver pathResolver,
        AgentRouterRuntimeSettings settings)
    {
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>
    /// Evaluates the supplied shell request.
    /// </summary>
    public ValueTask<Fin<ShellExecutionPolicyDecision>> EvaluateAsync(
        ShellExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var options = _settings.ShellExecution;

        if (!options.Enabled)
        {
            return Succeed(Denied("Approved shell execution is disabled by configuration."));
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
        if (IsDeniedCommand(normalizedCommand, options.DeniedCommands))
        {
            return Succeed(Denied($"Command '{command}' is explicitly denied."));
        }

        if (options.RequireExplicitAllowlist && !IsAllowedCommand(normalizedCommand, options.AllowedCommands))
        {
            return Succeed(Denied($"Command '{command}' is not in AgentRouter:ShellExecution:AllowedCommands."));
        }

        var arguments = request.Arguments
            .Where(static argument => argument is not null)
            .Select(static argument => argument.Trim())
            .Where(static argument => !string.IsNullOrWhiteSpace(argument))
            .ToArray();

        if (!options.AllowShellInterpreterInlineCommands && IsShellInterpreter(normalizedCommand) && ContainsInlineCommand(arguments))
        {
            return Succeed(Denied($"Command '{command}' cannot use inline command switches until argument-aware shell policy is enabled."));
        }

        var timeoutSeconds = request.TimeoutSeconds is null
            ? options.TimeoutSeconds
            : Math.Min(Math.Max(1, request.TimeoutSeconds.Value), Math.Max(1, options.TimeoutSeconds));

        var workingDirectoryResult = ResolveWorkingDirectory(
            request.WorkingDirectory,
            options.WorkingDirectoryRoot,
            options.AllowWorkingDirectoryOutsideRoot);

        if (workingDirectoryResult.IsFail)
        {
            return workingDirectoryResult.Match<ValueTask<Fin<ShellExecutionPolicyDecision>>>(
                Succ: _ => throw new InvalidOperationException("Unexpected working directory success while handling failure."),
                Fail: error => Succeed(Denied(error.Message)));
        }

        var workingDirectory = workingDirectoryResult.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected working directory failure while handling success."));

        return Succeed(new ShellExecutionPolicyDecision
        {
            Allowed = true,
            Decision = "allowed",
            ResolvedCommand = command,
            ResolvedArguments = arguments,
            WorkingDirectory = workingDirectory,
            TimeoutSeconds = timeoutSeconds,
            MaxOutputChars = Math.Max(1, options.MaxOutputChars)
        });
    }

    private Fin<string> ResolveWorkingDirectory(
        string? requestedWorkingDirectory,
        string configuredRoot,
        bool allowOutsideRoot)
    {
        var root = ResolveConfiguredPath(configuredRoot, "workspace");
        Directory.CreateDirectory(root);

        var requested = string.IsNullOrWhiteSpace(requestedWorkingDirectory)
            ? "."
            : requestedWorkingDirectory.Trim();

        var resolved = Path.IsPathRooted(requested)
            ? Path.GetFullPath(Environment.ExpandEnvironmentVariables(requested))
            : Path.GetFullPath(Path.Combine(root, Environment.ExpandEnvironmentVariables(requested)));

        if (!Directory.Exists(resolved))
        {
            return LanguageExt.Common.Error.New($"Working directory '{requested}' does not exist under shell execution root '{root}'.");
        }

        if (allowOutsideRoot)
        {
            return Fin<string>.Succ(resolved);
        }

        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedResolved = resolved.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!normalizedResolved.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return LanguageExt.Common.Error.New($"Working directory '{requested}' escapes the configured shell execution root '{root}'.");
        }

        return Fin<string>.Succ(resolved);
    }

    private string ResolveConfiguredPath(
        string configuredPath,
        string defaultPath)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? defaultPath
            : configuredPath.Trim();

        return _pathResolver.ResolveRelativeToContentRoot(path);
    }

    private static bool ContainsPathSeparator(string command)
    {
        return command.Contains(Path.DirectorySeparatorChar) ||
               command.Contains(Path.AltDirectorySeparatorChar);
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
               normalizedCommand.Equals("cmd", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsInlineCommand(IEnumerable<string> arguments)
    {
        return arguments.Any(argument => InlineCommandSwitches.Contains(argument, StringComparer.OrdinalIgnoreCase));
    }

    private static ShellExecutionPolicyDecision Denied(string reason)
    {
        return new ShellExecutionPolicyDecision
        {
            Allowed = false,
            Decision = "denied",
            Reason = reason
        };
    }

    private static ValueTask<Fin<ShellExecutionPolicyDecision>> Succeed(ShellExecutionPolicyDecision decision)
    {
        return new ValueTask<Fin<ShellExecutionPolicyDecision>>(Fin<ShellExecutionPolicyDecision>.Succ(decision));
    }
}
