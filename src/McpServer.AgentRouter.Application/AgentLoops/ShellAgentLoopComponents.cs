using System.Text.Json;
using LanguageExt;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Domain.AgentLoops;
using McpServer.AgentRouter.Domain.Shell;
using McpServer.AgentRouter.Domain.Ssh;
using Microsoft.Extensions.Logging;

namespace McpServer.AgentRouter.Application.AgentLoops;

/// <summary>
/// Dispatches agent loop steps to the MCP, shell, or SSH executor.
/// </summary>
public sealed class CompositeAgentToolExecutor : IAgentToolExecutor
{
    private readonly McpAgentToolExecutor _mcpExecutor;
    private readonly ShellAgentToolExecutor _shellExecutor;
    private readonly SshAgentToolExecutor _sshExecutor;

    /// <summary>
    /// Initializes a new composite agent tool executor.
    /// </summary>
    public CompositeAgentToolExecutor(
        McpAgentToolExecutor mcpExecutor,
        ShellAgentToolExecutor shellExecutor,
        SshAgentToolExecutor sshExecutor)
    {
        _mcpExecutor = mcpExecutor ?? throw new ArgumentNullException(nameof(mcpExecutor));
        _shellExecutor = shellExecutor ?? throw new ArgumentNullException(nameof(shellExecutor));
        _sshExecutor = sshExecutor ?? throw new ArgumentNullException(nameof(sshExecutor));
    }

    /// <summary>
    /// Routes the request to the correct executor.
    /// </summary>
    public ValueTask<Fin<AgentToolExecutionResult>> ExecuteAsync(
        AgentToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.Equals(request.PlannedStep.Capability, "shell.exec", StringComparison.OrdinalIgnoreCase))
        {
            return _shellExecutor.ExecuteAsync(request, cancellationToken);
        }

        if (string.Equals(request.PlannedStep.Capability, "ssh.exec", StringComparison.OrdinalIgnoreCase))
        {
            return _sshExecutor.ExecuteAsync(request, cancellationToken);
        }

        return _mcpExecutor.ExecuteAsync(request, cancellationToken);
    }
}

/// <summary>
/// Executes shell-backed agent loop steps.
/// </summary>
public sealed class ShellAgentToolExecutor : IAgentToolExecutor
{
    private readonly IShellExecutionService _shellExecutionService;
    private readonly ILogger<ShellAgentToolExecutor> _logger;

    /// <summary>
    /// Initializes a new shell agent tool executor.
    /// </summary>
    public ShellAgentToolExecutor(
        IShellExecutionService shellExecutionService,
        ILogger<ShellAgentToolExecutor> logger)
    {
        _shellExecutionService = shellExecutionService ?? throw new ArgumentNullException(nameof(shellExecutionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes the planned shell step.
    /// </summary>
    public async ValueTask<Fin<AgentToolExecutionResult>> ExecuteAsync(
        AgentToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var shellRequest = MapRequest(request.PlannedStep);
        var result = await _shellExecutionService.ExecuteAsync(shellRequest, cancellationToken).ConfigureAwait(false);

        if (result.IsFail)
        {
            return result.Match<Fin<AgentToolExecutionResult>>(
                Succ: _ => throw new InvalidOperationException("Unexpected shell execution success while handling failure."),
                Fail: error => error);
        }

        var response = result.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected shell execution failure while handling success."));

        if (!response.Allowed || string.Equals(response.Status, ShellExecutionStatusNames.Denied, StringComparison.OrdinalIgnoreCase))
        {
            return Fin<AgentToolExecutionResult>.Succ(new AgentToolExecutionResult
            {
                Status = ToolExecutionStatus.Denied,
                TraceId = response.TraceId,
                ExitCode = 1,
                OutputSummary = response.PolicyReason ?? "Approved shell execution was denied by policy.",
                ErrorMessage = response.PolicyReason,
                ElapsedMilliseconds = response.ElapsedMilliseconds
            });
        }

        var status = string.Equals(response.Status, ShellExecutionStatusNames.Completed, StringComparison.OrdinalIgnoreCase)
            ? ToolExecutionStatus.Succeeded
            : ToolExecutionStatus.Failed;

        _logger.LogDebug(
            "Autonomous loop shell command {Command} completed via trace {TraceId} with status {Status} in {ElapsedMilliseconds}ms.",
            response.Command,
            response.TraceId,
            response.Status,
            response.ElapsedMilliseconds);

        return Fin<AgentToolExecutionResult>.Succ(new AgentToolExecutionResult
        {
            Status = status,
            TraceId = response.TraceId,
            ExitCode = response.ExitCode ?? 1,
            OutputSummary = $"{response.Summary} Trace: {response.TraceId}.",
            ErrorMessage = status == ToolExecutionStatus.Succeeded ? null : response.Summary,
            ElapsedMilliseconds = response.ElapsedMilliseconds
        });
    }

    private static ShellExecutionRequest MapRequest(AgentPlannedStep plannedStep)
    {
        var arguments = plannedStep.ArgumentsJson;
        if (arguments is null)
        {
            return new ShellExecutionRequest
            {
                Command = plannedStep.ToolName
            };
        }

        var root = arguments.Value;
        var command = TryGetString(root, "command") ?? plannedStep.ToolName;
        var shellArguments = TryGetStringArray(root, "arguments");
        var workingDirectory = TryGetString(root, "working_directory") ?? TryGetString(root, "workingDirectory");
        var timeoutSeconds = TryGetInt32(root, "timeout_seconds") ?? TryGetInt32(root, "timeoutSeconds");

        return new ShellExecutionRequest
        {
            Command = command,
            Arguments = shellArguments,
            WorkingDirectory = workingDirectory,
            TimeoutSeconds = timeoutSeconds
        };
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static int? TryGetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt32(out var value))
        {
            return null;
        }

        return value;
    }

    private static IList<string> TryGetStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return new List<string>();
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            var value = property.GetString();
            return string.IsNullOrWhiteSpace(value)
                ? new List<string>()
                : new List<string> { value };
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            return new List<string>();
        }

        var values = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }

        return values;
    }
}


/// <summary>
/// Executes SSH-backed agent loop steps.
/// </summary>
public sealed class SshAgentToolExecutor : IAgentToolExecutor
{
    private readonly ISshExecutionService _sshExecutionService;
    private readonly ILogger<SshAgentToolExecutor> _logger;

    /// <summary>
    /// Initializes a new SSH agent tool executor.
    /// </summary>
    public SshAgentToolExecutor(
        ISshExecutionService sshExecutionService,
        ILogger<SshAgentToolExecutor> logger)
    {
        _sshExecutionService = sshExecutionService ?? throw new ArgumentNullException(nameof(sshExecutionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes the planned SSH step.
    /// </summary>
    public async ValueTask<Fin<AgentToolExecutionResult>> ExecuteAsync(
        AgentToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var sshRequest = MapRequest(request.PlannedStep);
        var result = await _sshExecutionService.ExecuteAsync(sshRequest, cancellationToken).ConfigureAwait(false);

        if (result.IsFail)
        {
            return result.Match<Fin<AgentToolExecutionResult>>(
                Succ: _ => throw new InvalidOperationException("Unexpected SSH execution success while handling failure."),
                Fail: error => error);
        }

        var response = result.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected SSH execution failure while handling success."));

        if (!response.Allowed || string.Equals(response.Status, SshExecutionStatusNames.Denied, StringComparison.OrdinalIgnoreCase))
        {
            return Fin<AgentToolExecutionResult>.Succ(new AgentToolExecutionResult
            {
                Status = ToolExecutionStatus.Denied,
                TraceId = response.TraceId,
                ExitCode = 1,
                OutputSummary = response.PolicyReason ?? "SSH execution was denied by policy.",
                ErrorMessage = response.PolicyReason,
                ElapsedMilliseconds = response.ElapsedMilliseconds
            });
        }

        var status = string.Equals(response.Status, SshExecutionStatusNames.Completed, StringComparison.OrdinalIgnoreCase)
            ? ToolExecutionStatus.Succeeded
            : ToolExecutionStatus.Failed;

        _logger.LogDebug(
            "Autonomous loop SSH command {Command} via profile {Profile} completed via trace {TraceId} with status {Status} in {ElapsedMilliseconds}ms.",
            response.Command,
            response.Profile,
            response.TraceId,
            response.Status,
            response.ElapsedMilliseconds);

        return Fin<AgentToolExecutionResult>.Succ(new AgentToolExecutionResult
        {
            Status = status,
            TraceId = response.TraceId,
            ExitCode = response.ExitCode ?? 1,
            OutputSummary = $"{response.Summary} Trace: {response.TraceId}.",
            ErrorMessage = status == ToolExecutionStatus.Succeeded ? null : response.Summary,
            ElapsedMilliseconds = response.ElapsedMilliseconds
        });
    }

    private static SshExecutionRequest MapRequest(AgentPlannedStep plannedStep)
    {
        var arguments = plannedStep.ArgumentsJson;
        if (arguments is null)
        {
            return new SshExecutionRequest
            {
                Command = plannedStep.ToolName
            };
        }

        var root = arguments.Value;
        var command = TryGetString(root, "command") ?? plannedStep.ToolName;
        var sshArguments = TryGetStringArray(root, "arguments");
        var workingDirectory = TryGetString(root, "working_directory") ?? TryGetString(root, "workingDirectory");
        var timeoutSeconds = TryGetInt32(root, "timeout_seconds") ?? TryGetInt32(root, "timeoutSeconds");

        return new SshExecutionRequest
        {
            Profile = TryGetString(root, "profile"),
            Command = command,
            Arguments = sshArguments,
            WorkingDirectory = workingDirectory,
            TimeoutSeconds = timeoutSeconds
        };
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static int? TryGetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt32(out var value))
        {
            return null;
        }

        return value;
    }

    private static IList<string> TryGetStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return new List<string>();
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            var value = property.GetString();
            return string.IsNullOrWhiteSpace(value)
                ? new List<string>()
                : new List<string> { value };
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            return new List<string>();
        }

        var values = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }

        return values;
    }
}
