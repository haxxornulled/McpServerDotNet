using LanguageExt;
using LanguageExt.Common;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Domain.Ssh;
using Microsoft.Extensions.Logging;

namespace McpServer.AgentRouter.Application.Ssh;

/// <summary>
/// Coordinates SSH policy, execution, and tracing.
/// </summary>
public sealed class SshExecutionService : ISshExecutionService
{
    private readonly ISshExecutionPolicy _policy;
    private readonly ISshCommandExecutor _executor;
    private readonly ISshExecutionTraceWriter _traceWriter;
    private readonly ILogger<SshExecutionService> _logger;

    /// <summary>
    /// Initializes a new SSH execution service.
    /// </summary>
    public SshExecutionService(
        ISshExecutionPolicy policy,
        ISshCommandExecutor executor,
        ISshExecutionTraceWriter traceWriter,
        ILogger<SshExecutionService> logger)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _traceWriter = traceWriter ?? throw new ArgumentNullException(nameof(traceWriter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes the supplied SSH request end to end.
    /// </summary>
    public async ValueTask<Fin<SshExecutionResponse>> ExecuteAsync(
        SshExecutionRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return LanguageExt.Common.Error.New("Request body is required.");
        }

        var createdAt = DateTimeOffset.UtcNow;
        var traceId = "ssh-exec-" + Guid.NewGuid().ToString("N");

        var policyResult = await _policy.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
        if (policyResult.IsFail)
        {
            return policyResult.Match<Fin<SshExecutionResponse>>(
                Succ: _ => throw new InvalidOperationException("Unexpected SSH policy success while handling failure."),
                Fail: error => error);
        }

        var policy = policyResult.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected SSH policy failure while handling success."));

        if (!policy.Allowed)
        {
            var deniedResponse = new SshExecutionResponse
            {
                Id = traceId,
                Status = SshExecutionStatusNames.Denied,
                Allowed = false,
                PolicyDecision = policy.Decision,
                PolicyReason = policy.Reason,
                Profile = request.Profile?.Trim() ?? string.Empty,
                Command = request.Command?.Trim() ?? string.Empty,
                Arguments = request.Arguments.ToArray(),
                WorkingDirectory = request.WorkingDirectory?.Trim() ?? string.Empty,
                Summary = policy.Reason ?? "SSH execution was denied by policy.",
                TraceId = traceId,
                CreatedAt = createdAt,
                CompletedAt = DateTimeOffset.UtcNow
            };

            await WriteTraceAsync(deniedResponse, cancellationToken).ConfigureAwait(false);
            return Fin<SshExecutionResponse>.Succ(deniedResponse);
        }

        var command = new SshExecutionCommand
        {
            ProfileName = policy.ProfileName,
            Host = policy.Host,
            Port = policy.Port,
            Username = policy.Username,
            Command = policy.ResolvedCommand,
            Arguments = policy.ResolvedArguments.ToArray(),
            WorkingDirectory = policy.WorkingDirectory,
            TimeoutSeconds = policy.TimeoutSeconds,
            MaxOutputChars = policy.MaxOutputChars,
            PasswordEnvironmentVariable = policy.PasswordEnvironmentVariable,
            PrivateKeyPath = policy.PrivateKeyPath,
            PrivateKeyPassphraseEnvironmentVariable = policy.PrivateKeyPassphraseEnvironmentVariable,
            HostKeySha256 = policy.HostKeySha256,
            AcceptUnknownHostKey = policy.AcceptUnknownHostKey
        };

        var executionStartedAt = DateTimeOffset.UtcNow;
        var executionResult = await _executor.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
        if (executionResult.IsFail)
        {
            var error = executionResult.Match(
                Succ: _ => throw new InvalidOperationException("Unexpected SSH execution success while handling failure."),
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
            Fail: _ => throw new InvalidOperationException("Unexpected SSH execution failure while handling success."));

        var response = MapResponse(traceId, createdAt, policy, execution);
        await WriteTraceAsync(response, cancellationToken).ConfigureAwait(false);

        LogCompletion(response);
        return Fin<SshExecutionResponse>.Succ(response);
    }

    private async ValueTask WriteTraceAsync(
        SshExecutionResponse response,
        CancellationToken cancellationToken)
    {
        var trace = new SshExecutionTraceRecord
        {
            TraceId = response.TraceId,
            CreatedAt = response.CreatedAt,
            CompletedAt = response.CompletedAt,
            Status = response.Status,
            Allowed = response.Allowed,
            PolicyDecision = response.PolicyDecision,
            PolicyReason = response.PolicyReason,
            Profile = response.Profile,
            Host = response.Host,
            Port = response.Port,
            Username = response.Username,
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
        traceResult.IfFail(error => _logger.LogWarning("Failed to write SSH execution trace {TraceId}: {Message}", response.TraceId, error.Message));
    }

    private static SshExecutionResponse MapResponse(
        string traceId,
        DateTimeOffset createdAt,
        SshExecutionPolicyDecision policy,
        SshCommandExecutionResult execution)
    {
        var status = execution.TimedOut
            ? SshExecutionStatusNames.TimedOut
            : execution.ExitCode == 0
                ? SshExecutionStatusNames.Completed
                : SshExecutionStatusNames.Failed;

        var summary = BuildSummary(policy.ProfileName, policy.ResolvedCommand, execution);

        return new SshExecutionResponse
        {
            Id = traceId,
            Status = status,
            Allowed = true,
            PolicyDecision = policy.Decision,
            Profile = policy.ProfileName,
            Host = policy.Host,
            Port = policy.Port,
            Username = policy.Username,
            Command = policy.ResolvedCommand,
            Arguments = policy.ResolvedArguments.ToArray(),
            WorkingDirectory = string.IsNullOrWhiteSpace(policy.WorkingDirectory) ? "." : policy.WorkingDirectory,
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

    private static SshExecutionResponse MapFailedResponse(
        string traceId,
        DateTimeOffset createdAt,
        SshExecutionPolicyDecision policy,
        Error error,
        DateTimeOffset executionStartedAt,
        DateTimeOffset completedAt)
    {
        var elapsedMilliseconds = Math.Max(
            0,
            (long)(completedAt - executionStartedAt).TotalMilliseconds);

        return new SshExecutionResponse
        {
            Id = traceId,
            Status = SshExecutionStatusNames.Failed,
            Allowed = true,
            PolicyDecision = policy.Decision,
            Profile = policy.ProfileName,
            Host = policy.Host,
            Port = policy.Port,
            Username = policy.Username,
            Command = policy.ResolvedCommand,
            Arguments = policy.ResolvedArguments.ToArray(),
            WorkingDirectory = string.IsNullOrWhiteSpace(policy.WorkingDirectory) ? "." : policy.WorkingDirectory,
            Stderr = error.Message,
            Summary = error.Message,
            TraceId = traceId,
            CreatedAt = createdAt,
            CompletedAt = completedAt,
            ElapsedMilliseconds = elapsedMilliseconds
        };
    }

    private static string BuildSummary(
        string profile,
        string command,
        SshCommandExecutionResult execution)
    {
        if (execution.TimedOut)
        {
            return $"SSH command '{command}' via profile '{profile}' timed out after {execution.ElapsedMilliseconds}ms with exit code {execution.ExitCode}.";
        }

        var output = !string.IsNullOrWhiteSpace(execution.Stdout)
            ? CompactText(execution.Stdout, 400)
            : CompactText(execution.Stderr, 400);

        var baseSummary = $"SSH command '{command}' via profile '{profile}' exited with code {execution.ExitCode} in {execution.ElapsedMilliseconds}ms.";
        return string.IsNullOrWhiteSpace(output)
            ? baseSummary
            : $"{baseSummary} Output: {output}";
    }

    private void LogCompletion(SshExecutionResponse response)
    {
        if (!string.Equals(response.Status, SshExecutionStatusNames.Completed, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "SSH command {Command} via profile {Profile} completed with status {Status} exit code {ExitCode} in {ElapsedMilliseconds}ms. Trace: {TraceId}",
                response.Command,
                response.Profile,
                response.Status,
                response.ExitCode,
                response.ElapsedMilliseconds,
                response.TraceId);
            return;
        }

        if (response.ElapsedMilliseconds >= 2_000)
        {
            _logger.LogInformation(
                "SSH command {Command} via profile {Profile} completed in {ElapsedMilliseconds}ms. Trace: {TraceId}",
                response.Command,
                response.Profile,
                response.ElapsedMilliseconds,
                response.TraceId);
        }
        else
        {
            _logger.LogDebug(
                "SSH command {Command} via profile {Profile} completed in {ElapsedMilliseconds}ms. Trace: {TraceId}",
                response.Command,
                response.Profile,
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
