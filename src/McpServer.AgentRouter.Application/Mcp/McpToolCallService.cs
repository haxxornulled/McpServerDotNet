using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.Runtime;
using McpServer.AgentRouter.Domain.Mcp;
using Microsoft.Extensions.Logging;

namespace McpServer.AgentRouter.Application.Mcp;

public sealed class McpToolCallService : IMcpToolCallService
{
    private static readonly JsonElement EmptyArguments = JsonSerializer.SerializeToElement(new { });

    private readonly IMcpToolCallPolicy _policy;
    private readonly IMcpToolCallClient _client;
    private readonly IMcpToolCallTraceWriter _traceWriter;
    private readonly AgentRouterRuntimeSettings _settings;
    private readonly ILogger<McpToolCallService> _logger;

    public McpToolCallService(
        IMcpToolCallPolicy policy,
        IMcpToolCallClient client,
        IMcpToolCallTraceWriter traceWriter,
        AgentRouterRuntimeSettings settings,
        ILogger<McpToolCallService> logger)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _traceWriter = traceWriter ?? throw new ArgumentNullException(nameof(traceWriter));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<Fin<McpToolCallResponse>> CallToolAsync(
        McpToolCallRequest? request,
        CancellationToken cancellationToken)
    {
        var commandResult = MapRequest(request);
        if (commandResult.IsFail)
        {
            return commandResult.Match<Fin<McpToolCallResponse>>(
                Succ: _ => throw new InvalidOperationException("Unexpected MCP tool call mapping success."),
                Fail: error => error);
        }

        var command = commandResult.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected MCP tool call mapping failure."));

        var startedAt = DateTimeOffset.UtcNow;
        var policyResult = await _policy.EvaluateAsync(command, cancellationToken).ConfigureAwait(false);
        if (policyResult.IsFail)
        {
            var error = policyResult.Match(
                Succ: _ => throw new InvalidOperationException("Unexpected MCP tool policy success while handling failure."),
                Fail: failure => failure);

            return error;
        }

        var policyDecision = policyResult.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected MCP tool policy failure while handling success."));

        if (!policyDecision.Allowed)
        {
            var completedAt = DateTimeOffset.UtcNow;
            var response = new McpToolCallResponse
            {
                Status = McpToolCallStatusNames.Denied,
                ToolName = command.ToolName,
                Allowed = false,
                PolicyDecision = policyDecision.Decision,
                PolicyReason = policyDecision.Reason,
                TraceId = command.TraceId,
                ElapsedMilliseconds = 0,
                ErrorMessage = policyDecision.Reason
            };

            await WriteTraceAsync(
                    command,
                    policyDecision,
                    response,
                    startedAt,
                    completedAt,
                    cancellationToken)
                .ConfigureAwait(false);

            return Fin<McpToolCallResponse>.Succ(response);
        }

        var clientResult = await _client.CallToolAsync(command, cancellationToken).ConfigureAwait(false);
        var finishedAt = DateTimeOffset.UtcNow;

        if (clientResult.IsFail)
        {
            var error = clientResult.Match(
                Succ: _ => throw new InvalidOperationException("Unexpected MCP tool call success while handling failure."),
                Fail: failure => failure);

            var response = new McpToolCallResponse
            {
                Status = McpToolCallStatusNames.Failed,
                ToolName = command.ToolName,
                Allowed = true,
                PolicyDecision = policyDecision.Decision,
                PolicyReason = policyDecision.Reason,
                TraceId = command.TraceId,
                ErrorMessage = error.Message
            };

            await WriteTraceAsync(
                    command,
                    policyDecision,
                    response,
                    startedAt,
                    finishedAt,
                    cancellationToken)
                .ConfigureAwait(false);

            return error;
        }

        var result = clientResult.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected MCP tool call failure while handling success."));

        var successResponse = new McpToolCallResponse
        {
            Status = result.Status,
            ToolName = result.ToolName,
            Allowed = true,
            PolicyDecision = policyDecision.Decision,
            PolicyReason = policyDecision.Reason,
            TraceId = command.TraceId,
            Transport = result.Transport,
            ElapsedMilliseconds = result.ElapsedMilliseconds,
            Result = result.Result,
            ErrorMessage = result.ErrorMessage
        };

        await WriteTraceAsync(
                command,
                policyDecision,
                successResponse,
                startedAt,
                finishedAt,
                cancellationToken)
            .ConfigureAwait(false);

        if (result.ElapsedMilliseconds >= 10_000)
        {
            _logger.LogWarning(
                "MCP tool call {TraceId} completed slowly for {ToolName} in {ElapsedMilliseconds}ms.",
                command.TraceId,
                command.ToolName,
                result.ElapsedMilliseconds);
        }
        else if (result.ElapsedMilliseconds >= 2_000)
        {
            _logger.LogInformation(
                "MCP tool call {TraceId} completed for {ToolName} in {ElapsedMilliseconds}ms.",
                command.TraceId,
                command.ToolName,
                result.ElapsedMilliseconds);
        }
        else
        {
            _logger.LogDebug(
                "MCP tool call {TraceId} completed for {ToolName} in {ElapsedMilliseconds}ms.",
                command.TraceId,
                command.ToolName,
                result.ElapsedMilliseconds);
        }

        return Fin<McpToolCallResponse>.Succ(successResponse);
    }

    private Fin<McpToolCallCommand> MapRequest(McpToolCallRequest? request)
    {
        if (request is null)
        {
            return Error.New("Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ToolName))
        {
            return Error.New("toolName is required.");
        }

        var options = _settings.ToolExecution;
        var timeoutSeconds = Math.Clamp(
            request.TimeoutSeconds ?? options.TimeoutSeconds,
            1,
            300);
        var maxOutputChars = Math.Clamp(
            request.MaxOutputChars ?? options.MaxOutputChars,
            1024,
            1_000_000);

        var arguments = request.Arguments.HasValue
            ? request.Arguments.Value.Clone()
            : EmptyArguments.Clone();

        return Fin<McpToolCallCommand>.Succ(new McpToolCallCommand
        {
            TraceId = "mcp-call-" + Guid.NewGuid().ToString("N"),
            ToolName = request.ToolName.Trim(),
            Arguments = arguments,
            TimeoutSeconds = timeoutSeconds,
            MaxOutputChars = maxOutputChars
        });
    }

    private async ValueTask WriteTraceAsync(
        McpToolCallCommand command,
        McpToolCallPolicyDecision policyDecision,
        McpToolCallResponse response,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        var traceResult = await _traceWriter.WriteAsync(
                new McpToolCallTraceRecord
                {
                    TraceId = command.TraceId,
                    ToolName = command.ToolName,
                    StartedAt = startedAt,
                    CompletedAt = completedAt,
                    Allowed = response.Allowed,
                    PolicyDecision = policyDecision.Decision,
                    PolicyReason = policyDecision.Reason,
                    Status = response.Status,
                    ElapsedMilliseconds = response.ElapsedMilliseconds,
                    ErrorMessage = response.ErrorMessage,
                    Arguments = command.Arguments.Clone(),
                    Result = response.Result?.Clone()
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (traceResult.IsFail)
        {
            var error = traceResult.Match(
                Succ: _ => throw new InvalidOperationException("Unexpected successful trace failure handling."),
                Fail: failure => failure);

            _logger.LogWarning(
                "Failed to write MCP tool call trace {TraceId}: {ErrorMessage}",
                command.TraceId,
                error.Message);
        }
    }
}
