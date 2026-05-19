using LanguageExt;
using LanguageExt.Common;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Domain.Shell;
using Microsoft.Extensions.Logging;

namespace McpServer.AgentRouter.Application.Shell;

/// <summary>
/// Coordinates shell policy, execution, and tracing.
/// </summary>
public sealed class ShellExecutionService : IShellExecutionService
{
    private readonly IShellExecutionPolicy _policy;
    private readonly IShellCommandExecutor _executor;
    private readonly IShellExecutionTraceWriter _traceWriter;
    private readonly ILogger<ShellExecutionService> _logger;

    /// <summary>
    /// Initializes a new shell execution service.
    /// </summary>
    public ShellExecutionService(
        IShellExecutionPolicy policy,
        IShellCommandExecutor executor,
        IShellExecutionTraceWriter traceWriter,
        ILogger<ShellExecutionService> logger)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _traceWriter = traceWriter ?? throw new ArgumentNullException(nameof(traceWriter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes the supplied shell request end to end.
    /// </summary>
    public async ValueTask<Fin<ShellExecutionResponse>> ExecuteAsync(
        ShellExecutionRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return LanguageExt.Common.Error.New("Request body is required.");
        }

        var createdAt = DateTimeOffset.UtcNow;
        var traceId = "shell-exec-" + Guid.NewGuid().ToString("N");

        var policyResult = await _policy.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
        if (policyResult.IsFail)
        {
            return policyResult.Match<Fin<ShellExecutionResponse>>(
                Succ: _ => throw new InvalidOperationException("Unexpected shell policy success while handling failure."),
                Fail: error => error);
        }

        var policy = policyResult.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected shell policy failure while handling success."));

        if (!policy.Allowed)
        {
            var deniedResponse = new ShellExecutionResponse
            {
                Id = traceId,
                Status = ShellExecutionStatusNames.Denied,
                Allowed = false,
                PolicyDecision = policy.Decision,
                PolicyReason = policy.Reason,
                Command = request.Command?.Trim() ?? string.Empty,
                Arguments = request.Arguments.ToArray(),
                WorkingDirectory = string.Empty,
                Summary = policy.Reason ?? "Approved shell execution was denied by policy.",
                TraceId = traceId,
                CreatedAt = createdAt,
                CompletedAt = DateTimeOffset.UtcNow
            };

            await WriteTraceAsync(deniedResponse, cancellationToken).ConfigureAwait(false);
            return Fin<ShellExecutionResponse>.Succ(deniedResponse);
        }

        var command = new ShellExecutionCommand
        {
            Command = policy.ResolvedCommand,
            Arguments = policy.ResolvedArguments.ToArray(),
            WorkingDirectory = policy.WorkingDirectory,
            TimeoutSeconds = policy.TimeoutSeconds,
            MaxOutputChars = policy.MaxOutputChars
        };

        var executionStartedAt = DateTimeOffset.UtcNow;
        var executionResult = await _executor.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        if (executionResult.IsFail)
        {
            var error = executionResult.Match(
                Succ: _ => throw new InvalidOperationException("Unexpected shell execution success while handling failure."),
                Fail: failure => failure);

            var failedResponse = MapFailedResponse(
                traceId,
                createdAt,
                policy,
                error,
                executionStartedAt,
                DateTimeOffset.UtcNow);

            await WriteTraceAsync(failedResponse, cancellationToken).ConfigureAwait(false);
            return error;
        }

        var execution = executionResult.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected shell execution failure while handling success."));

        var response = MapResponse(traceId, createdAt, policy, execution);
        await WriteTraceAsync(response, cancellationToken).ConfigureAwait(false);

        LogCompletion(response);
        return Fin<ShellExecutionResponse>.Succ(response);
    }

    private async ValueTask WriteTraceAsync(
        ShellExecutionResponse response,
        CancellationToken cancellationToken)
    {
        var trace = new ShellExecutionTraceRecord
        {
            TraceId = response.TraceId,
            CreatedAt = response.CreatedAt,
            CompletedAt = response.CompletedAt,
            Status = response.Status,
            Allowed = response.Allowed,
            PolicyDecision = response.PolicyDecision,
            PolicyReason = response.PolicyReason,
            Command = response.Command,
            Arguments = response.Arguments.ToArray(),
            WorkingDirectory = response.WorkingDirectory,
            ExitCode = response.ExitCode,
            TimedOut = response.TimedOut,
            Stdout = response.Stdout,
            Stderr = response.Stderr,
            StdoutTruncated = response.StdoutTruncated,
            StderrTruncated = response.StderrTruncated,
            Summary = response.Summary,
            ElapsedMilliseconds = response.ElapsedMilliseconds
        };

        var traceResult = await _traceWriter.WriteAsync(trace, cancellationToken).ConfigureAwait(false);
        traceResult.IfFail(error => _logger.LogWarning("Failed to write shell execution trace {TraceId}: {Message}", response.TraceId, error.Message));
    }

    private static ShellExecutionResponse MapResponse(
        string traceId,
        DateTimeOffset createdAt,
        ShellExecutionPolicyDecision policy,
        ShellCommandExecutionResult execution)
    {
        var status = execution.TimedOut
            ? ShellExecutionStatusNames.TimedOut
            : execution.ExitCode == 0
                ? ShellExecutionStatusNames.Completed
                : ShellExecutionStatusNames.Failed;

        var summary = BuildSummary(policy.ResolvedCommand, execution);

        return new ShellExecutionResponse
        {
            Id = traceId,
            Status = status,
            Allowed = true,
            PolicyDecision = policy.Decision,
            Command = policy.ResolvedCommand,
            Arguments = policy.ResolvedArguments.ToArray(),
            WorkingDirectory = policy.WorkingDirectory,
            ExitCode = execution.ExitCode,
            TimedOut = execution.TimedOut,
            Stdout = execution.Stdout,
            Stderr = execution.Stderr,
            StdoutTruncated = execution.StdoutTruncated,
            StderrTruncated = execution.StderrTruncated,
            Summary = summary,
            TraceId = traceId,
            CreatedAt = createdAt,
            CompletedAt = DateTimeOffset.UtcNow,
            ElapsedMilliseconds = execution.ElapsedMilliseconds
        };
    }

    private static ShellExecutionResponse MapFailedResponse(
        string traceId,
        DateTimeOffset createdAt,
        ShellExecutionPolicyDecision policy,
        Error error,
        DateTimeOffset executionStartedAt,
        DateTimeOffset completedAt)
    {
        var elapsedMilliseconds = Math.Max(
            0,
            (long)(completedAt - executionStartedAt).TotalMilliseconds);

        return new ShellExecutionResponse
        {
            Id = traceId,
            Status = ShellExecutionStatusNames.Failed,
            Allowed = true,
            PolicyDecision = policy.Decision,
            Command = policy.ResolvedCommand,
            Arguments = policy.ResolvedArguments.ToArray(),
            WorkingDirectory = policy.WorkingDirectory,
            Stderr = error.Message,
            Summary = error.Message,
            TraceId = traceId,
            CreatedAt = createdAt,
            CompletedAt = completedAt,
            ElapsedMilliseconds = elapsedMilliseconds
        };
    }

    private static string BuildSummary(
        string command,
        ShellCommandExecutionResult execution)
    {
        if (execution.TimedOut)
        {
            return $"Command '{command}' timed out after {execution.ElapsedMilliseconds}ms with exit code {execution.ExitCode}.";
        }

        var output = !string.IsNullOrWhiteSpace(execution.Stdout)
            ? CompactText(execution.Stdout, 400)
            : CompactText(execution.Stderr, 400);

        var baseSummary = $"Command '{command}' exited with code {execution.ExitCode} in {execution.ElapsedMilliseconds}ms.";
        return string.IsNullOrWhiteSpace(output)
            ? baseSummary
            : $"{baseSummary} Output: {output}";
    }

    private void LogCompletion(ShellExecutionResponse response)
    {
        if (!string.Equals(response.Status, ShellExecutionStatusNames.Completed, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Approved shell command {Command} completed with status {Status} exit code {ExitCode} in {ElapsedMilliseconds}ms. Trace: {TraceId}",
                response.Command,
                response.Status,
                response.ExitCode,
                response.ElapsedMilliseconds,
                response.TraceId);
            return;
        }

        if (response.ElapsedMilliseconds >= 2_000)
        {
            _logger.LogInformation(
                "Approved shell command {Command} completed in {ElapsedMilliseconds}ms. Trace: {TraceId}",
                response.Command,
                response.ElapsedMilliseconds,
                response.TraceId);
        }
        else
        {
            _logger.LogDebug(
                "Approved shell command {Command} completed in {ElapsedMilliseconds}ms. Trace: {TraceId}",
                response.Command,
                response.ElapsedMilliseconds,
                response.TraceId);
        }
    }

    private static string CompactText(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var compacted = string.Join(
            " ",
            text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return compacted.Length <= maxLength
            ? compacted
            : compacted[..maxLength] + "...";
    }
}
