using System.Text.Json;
using LanguageExt;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Domain.AgentLoops;
using McpServer.AgentRouter.Domain.Mcp;
using Microsoft.Extensions.Logging;

namespace McpServer.AgentRouter.Application.AgentLoops;

/// <summary>
/// Plans the next agent loop step from the current loop context.
/// </summary>
public sealed class McpAgentStepPlanner : IAgentStepPlanner
{
    private static readonly JsonElement DefaultListDirectoryArguments = JsonSerializer.SerializeToElement(new
    {
        path = "."
    });

    /// <summary>
    /// Plans the next loop step.
    /// </summary>
    public ValueTask<Fin<AgentPlannedStep>> PlanNextStepAsync(
        AgentLoopExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var toolName = string.IsNullOrWhiteSpace(context.Request.ToolName)
            ? "fs.list_directory"
            : context.Request.ToolName.Trim();

        var arguments = context.Request.Arguments.HasValue
            ? context.Request.Arguments.Value.Clone()
            : DefaultListDirectoryArguments.Clone();

        var isShellExecution = string.Equals(toolName, "shell.exec", StringComparison.OrdinalIgnoreCase);
        var isSshExecution = string.Equals(toolName, "ssh.exec", StringComparison.OrdinalIgnoreCase);

        return new ValueTask<Fin<AgentPlannedStep>>(Fin<AgentPlannedStep>.Succ(new AgentPlannedStep
        {
            Phase = AgentStepPhase.Act,
            Capability = isShellExecution
                ? "shell.exec"
                : isSshExecution
                    ? "ssh.exec"
                    : "mcp.tools.call",
            ToolName = toolName,
            RiskLevel = isShellExecution || isSshExecution ? ToolRiskLevel.Medium : ToolRiskLevel.Low,
            InputSummary = isShellExecution
                ? $"Run approved shell command for goal: {context.Run.Goal}"
                : isSshExecution
                    ? $"Run approved SSH command for goal: {context.Run.Goal}"
                    : $"Call MCP tool '{toolName}' for goal: {context.Run.Goal}",
            ArgumentsJson = arguments
        }));
    }
}

/// <summary>
/// Executes MCP-backed agent loop steps.
/// </summary>
public sealed class McpAgentToolExecutor : IAgentToolExecutor
{
    private readonly IMcpToolCallService _toolCallService;
    private readonly ILogger<McpAgentToolExecutor> _logger;

    /// <summary>
    /// Initializes a new MCP agent tool executor.
    /// </summary>
    public McpAgentToolExecutor(
        IMcpToolCallService toolCallService,
        ILogger<McpAgentToolExecutor> logger)
    {
        _toolCallService = toolCallService ?? throw new ArgumentNullException(nameof(toolCallService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes the planned MCP step.
    /// </summary>
    public async ValueTask<Fin<AgentToolExecutionResult>> ExecuteAsync(
        AgentToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var plannedStep = request.PlannedStep;
        var toolRequest = new McpToolCallRequest
        {
            ToolName = plannedStep.ToolName,
            Arguments = plannedStep.ArgumentsJson?.Clone()
        };

        var result = await _toolCallService.CallToolAsync(toolRequest, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFail)
        {
            return result.Match<Fin<AgentToolExecutionResult>>(
                Succ: _ => throw new InvalidOperationException("Unexpected MCP tool call success while handling failure."),
                Fail: error => error);
        }

        var response = result.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected MCP tool call failure while handling success."));

        if (!response.Allowed || string.Equals(response.Status, "denied", StringComparison.OrdinalIgnoreCase))
        {
            return Fin<AgentToolExecutionResult>.Succ(new AgentToolExecutionResult
            {
                Status = ToolExecutionStatus.Denied,
                ExitCode = 1,
                OutputSummary = response.PolicyReason ?? $"MCP tool '{response.ToolName}' was denied by policy.",
                ErrorMessage = response.PolicyReason,
                ElapsedMilliseconds = response.ElapsedMilliseconds
            });
        }

        if (!string.Equals(response.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return Fin<AgentToolExecutionResult>.Succ(new AgentToolExecutionResult
            {
                Status = ToolExecutionStatus.Failed,
                ExitCode = 1,
                OutputSummary = response.ErrorMessage ?? $"MCP tool '{response.ToolName}' returned status '{response.Status}'.",
                ErrorMessage = response.ErrorMessage ?? $"MCP tool '{response.ToolName}' returned status '{response.Status}'.",
                ElapsedMilliseconds = response.ElapsedMilliseconds
            });
        }

        var summary = SummarizeResponse(response);

        _logger.LogDebug(
            "Autonomous loop MCP tool {ToolName} completed via trace {TraceId} in {ElapsedMilliseconds}ms.",
            response.ToolName,
            response.TraceId,
            response.ElapsedMilliseconds);

        return Fin<AgentToolExecutionResult>.Succ(new AgentToolExecutionResult
        {
            Status = ToolExecutionStatus.Succeeded,
            TraceId = response.TraceId,
            ExitCode = 0,
            OutputSummary = summary,
            ElapsedMilliseconds = response.ElapsedMilliseconds
        });
    }

    private static string SummarizeResponse(McpToolCallResponse response)
    {
        if (response.Result is null)
        {
            return $"MCP tool '{response.ToolName}' completed. Trace: {response.TraceId}.";
        }

        var result = response.Result.Value;
        var structuredSummary = TrySummarizeStructuredContent(response.ToolName, result);
        if (!string.IsNullOrWhiteSpace(structuredSummary))
        {
            return $"{structuredSummary} Trace: {response.TraceId}.";
        }

        var contentSummary = TrySummarizeContentText(response.ToolName, result);
        if (!string.IsNullOrWhiteSpace(contentSummary))
        {
            return $"{contentSummary} Trace: {response.TraceId}.";
        }

        return $"MCP tool '{response.ToolName}' completed. Trace: {response.TraceId}.";
    }

    private static string? TrySummarizeStructuredContent(
        string toolName,
        JsonElement result)
    {
        if (!result.TryGetProperty("structuredContent", out var structuredContent) ||
            structuredContent.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (string.Equals(toolName, "fs.list_directory", StringComparison.OrdinalIgnoreCase))
        {
            return SummarizeListDirectory(structuredContent);
        }

        if (string.Equals(toolName, "fs.get_metadata", StringComparison.OrdinalIgnoreCase))
        {
            return SummarizeFileMetadata(structuredContent);
        }

        return $"MCP tool '{toolName}' completed with structured content.";
    }

    private static string? SummarizeListDirectory(JsonElement structuredContent)
    {
        var path = TryGetStringProperty(structuredContent, "path") ?? "requested path";
        if (!structuredContent.TryGetProperty("entries", out var entries) ||
            entries.ValueKind != JsonValueKind.Array)
        {
            return $"Listed {path}.";
        }

        var names = new List<string>(8);
        var directoryCount = 0;
        var fileCount = 0;
        var totalCount = 0;

        foreach (var entry in entries.EnumerateArray())
        {
            totalCount++;

            var name = TryGetStringProperty(entry, "name");
            if (!string.IsNullOrWhiteSpace(name) && names.Count < 8)
            {
                names.Add(name);
            }

            if (TryGetBooleanProperty(entry, "isDirectory") == true ||
                TryGetBooleanProperty(entry, "is_directory") == true)
            {
                directoryCount++;
            }
            else
            {
                fileCount++;
            }
        }

        var entryText = totalCount == 1 ? "1 entry" : $"{totalCount} entries";
        var kindText = $"{directoryCount} director{(directoryCount == 1 ? "y" : "ies")}, {fileCount} file{(fileCount == 1 ? string.Empty : "s")}";
        var preview = names.Count == 0
            ? string.Empty
            : $" Preview: {string.Join(", ", names)}{(totalCount > names.Count ? ", ..." : ".")}";

        return $"Listed {path}. Found {entryText} ({kindText}).{preview}";
    }

    private static string? SummarizeFileMetadata(JsonElement structuredContent)
    {
        var path = TryGetStringProperty(structuredContent, "path") ?? "requested path";
        var isDirectory = TryGetBooleanProperty(structuredContent, "isDirectory") ??
            TryGetBooleanProperty(structuredContent, "is_directory");

        if (isDirectory is null)
        {
            return $"Read metadata for {path}.";
        }

        return isDirectory.Value
            ? $"Read metadata for directory {path}."
            : $"Read metadata for file {path}.";
    }

    private static string? TrySummarizeContentText(
        string toolName,
        JsonElement result)
    {
        if (!result.TryGetProperty("content", out var content) ||
            content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in content.EnumerateArray())
        {
            var text = TryGetStringProperty(item, "text");
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            return $"MCP tool '{toolName}' completed. Output: {CompactText(text, 500)}";
        }

        return null;
    }

    private static string CompactText(
        string text,
        int maxLength)
    {
        var compacted = string.Join(
            " ",
            text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (compacted.Length <= maxLength)
        {
            return compacted;
        }

        return compacted[..maxLength] + "...";
    }

    private static string? TryGetStringProperty(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static bool? TryGetBooleanProperty(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }
}

/// <summary>
/// Inspects MCP tool results and determines the next loop action.
/// </summary>
public sealed class McpAgentResultInspector : IAgentResultInspector
{
    /// <summary>
    /// Inspects the tool execution result.
    /// </summary>
    public ValueTask<Fin<AgentResultInspection>> InspectAsync(
        AgentLoopExecutionContext context,
        AgentPlannedStep plannedStep,
        AgentToolExecutionResult executionResult,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(plannedStep);
        ArgumentNullException.ThrowIfNull(executionResult);
        cancellationToken.ThrowIfCancellationRequested();

        if (executionResult.Status == ToolExecutionStatus.Succeeded)
        {
            return new ValueTask<Fin<AgentResultInspection>>(Fin<AgentResultInspection>.Succ(new AgentResultInspection
            {
                Decision = AgentDecisionType.StopSucceeded,
                OutputSummary = executionResult.OutputSummary,
                FinalResult = executionResult.OutputSummary
            }));
        }

        return new ValueTask<Fin<AgentResultInspection>>(Fin<AgentResultInspection>.Succ(new AgentResultInspection
        {
            Decision = AgentDecisionType.StopFailed,
            OutputSummary = executionResult.ErrorMessage ?? executionResult.OutputSummary,
            FinalResult = executionResult.ErrorMessage ?? executionResult.OutputSummary
        }));
    }
}
