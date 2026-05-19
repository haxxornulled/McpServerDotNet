using System.Diagnostics;
using LanguageExt;
using LanguageExt.Common;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.Runtime;
using McpServer.AgentRouter.Domain.AgentLoops;
using Microsoft.Extensions.Logging;

namespace McpServer.AgentRouter.Application.AgentLoops;

/// <summary>
/// Runs the autonomous agent loop lifecycle.
/// </summary>
public sealed class AutonomousLoopRunner : IAutonomousLoopRunner
{
    private readonly IAgentStepPlanner _stepPlanner;
    private readonly IToolExecutionPolicy _toolExecutionPolicy;
    private readonly IAgentToolExecutor _toolExecutor;
    private readonly IAgentResultInspector _resultInspector;
    private readonly IAgentLoopValidator _loopValidator;
    private readonly IAgentTraceWriter _traceWriter;
    private readonly AgentRouterRuntimeSettings _settings;
    private readonly ILogger<AutonomousLoopRunner> _logger;

    /// <summary>
    /// Initializes a new autonomous loop runner.
    /// </summary>
    public AutonomousLoopRunner(
        IAgentStepPlanner stepPlanner,
        IToolExecutionPolicy toolExecutionPolicy,
        IAgentToolExecutor toolExecutor,
        IAgentResultInspector resultInspector,
        IAgentLoopValidator loopValidator,
        IAgentTraceWriter traceWriter,
        AgentRouterRuntimeSettings settings,
        ILogger<AutonomousLoopRunner> logger)
    {
        _stepPlanner = stepPlanner ?? throw new ArgumentNullException(nameof(stepPlanner));
        _toolExecutionPolicy = toolExecutionPolicy ?? throw new ArgumentNullException(nameof(toolExecutionPolicy));
        _toolExecutor = toolExecutor ?? throw new ArgumentNullException(nameof(toolExecutor));
        _resultInspector = resultInspector ?? throw new ArgumentNullException(nameof(resultInspector));
        _loopValidator = loopValidator ?? throw new ArgumentNullException(nameof(loopValidator));
        _traceWriter = traceWriter ?? throw new ArgumentNullException(nameof(traceWriter));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes the autonomous loop for the supplied request.
    /// </summary>
    public async ValueTask<Fin<AgentLoopRun>> RunAsync(
        AgentLoopRequest? request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateRequest(request);
        if (validation.IsFail)
        {
            return validation.Match<Fin<AgentLoopRun>>(
                Succ: _ => throw new InvalidOperationException("Unexpected loop validation success."),
                Fail: error => error);
        }

        var validatedRequest = validation.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected loop validation failure."));

        var runState = AgentLoopRunState.Start(validatedRequest.Goal!);
        var run = runState.Run;

        var loopSettings = ResolveLoopSettings(validatedRequest);
        var context = new AgentLoopExecutionContext(
            run,
            validatedRequest,
            loopSettings.MaxSteps,
            loopSettings.AllowedCapabilities,
            loopSettings.MaxToolCalls,
            loopSettings.MaxRuntimeSeconds);

        _logger.LogInformation(
            "Started autonomous loop {LoopId} with max steps {MaxSteps}, max tool calls {MaxToolCalls}, and max runtime {MaxRuntimeSeconds}s.",
            run.Id,
            loopSettings.MaxSteps,
            loopSettings.MaxToolCalls,
            loopSettings.MaxRuntimeSeconds);

        var loopStopwatch = Stopwatch.StartNew();
        var attemptedToolCalls = 0;

        for (var sequence = 1; sequence <= loopSettings.MaxSteps; sequence++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsRuntimeLimitExceeded(loopStopwatch, loopSettings.MaxRuntimeSeconds))
            {
                runState.MarkFailed(
                    $"Autonomous loop reached max runtime ({loopSettings.MaxRuntimeSeconds}s) before step {sequence}.",
                    "max_runtime_reached");
                await _traceWriter.WriteRunAsync(run, cancellationToken).ConfigureAwait(false);
                return Fin<AgentLoopRun>.Succ(run);
            }

            var plannedStepResult = await _stepPlanner.PlanNextStepAsync(context, cancellationToken)
                .ConfigureAwait(false);

            if (plannedStepResult.IsFail)
            {
                var error = plannedStepResult.Match(
                    Succ: _ => throw new InvalidOperationException("Unexpected planned step success while handling failure."),
                    Fail: failure => failure);

                runState.MarkFailed(error.Message, "planning_failed");
                await _traceWriter.WriteRunAsync(run, cancellationToken).ConfigureAwait(false);
                return Fin<AgentLoopRun>.Succ(run);
            }

            var plannedStep = plannedStepResult.Match(
                Succ: value => value,
                Fail: _ => throw new InvalidOperationException("Unexpected planned step failure while handling success."));

            var step = runState.BeginStep(sequence, plannedStep);

            var policyResult = await _toolExecutionPolicy.EvaluateAsync(context, plannedStep, cancellationToken)
                .ConfigureAwait(false);

            if (policyResult.IsFail)
            {
                var error = policyResult.Match(
                    Succ: _ => throw new InvalidOperationException("Unexpected policy success while handling failure."),
                    Fail: failure => failure);

                runState.CompleteDeniedStep(step, error.Message);
                await _traceWriter.WriteStepAsync(run, step, cancellationToken).ConfigureAwait(false);
                runState.MarkFailed(error.Message, "policy_failed");
                await _traceWriter.WriteRunAsync(run, cancellationToken).ConfigureAwait(false);
                return Fin<AgentLoopRun>.Succ(run);
            }

            var policyDecision = policyResult.Match(
                Succ: value => value,
                Fail: _ => throw new InvalidOperationException("Unexpected policy failure while handling success."));

            runState.ApplyPolicyDecision(step, policyDecision);

            if (!policyDecision.Allowed)
            {
                var reason = string.IsNullOrWhiteSpace(policyDecision.Reason)
                    ? "Tool execution policy denied the planned step."
                    : policyDecision.Reason;

                runState.CompleteDeniedStep(step, reason!);
                await _traceWriter.WriteStepAsync(run, step, cancellationToken).ConfigureAwait(false);
                runState.MarkFailed(reason!, "policy_denied");
                await _traceWriter.WriteRunAsync(run, cancellationToken).ConfigureAwait(false);
                return Fin<AgentLoopRun>.Succ(run);
            }

            if (attemptedToolCalls >= loopSettings.MaxToolCalls)
            {
                var reason = $"Autonomous loop reached max tool calls ({loopSettings.MaxToolCalls}) before executing step {sequence}.";
                runState.CompleteFailedStep(step, reason, 0);
                await _traceWriter.WriteStepAsync(run, step, cancellationToken).ConfigureAwait(false);
                runState.MarkFailed(reason, "max_tool_calls_reached");
                await _traceWriter.WriteRunAsync(run, cancellationToken).ConfigureAwait(false);
                return Fin<AgentLoopRun>.Succ(run);
            }

            var stopwatch = Stopwatch.StartNew();
            step.Status = ToolExecutionStatus.Running;
            attemptedToolCalls++;

            var executionResult = await _toolExecutor.ExecuteAsync(
                    new AgentToolExecutionRequest(context, plannedStep),
                    cancellationToken)
                .ConfigureAwait(false);

            stopwatch.Stop();

            if (executionResult.IsFail)
            {
                var error = executionResult.Match(
                    Succ: _ => throw new InvalidOperationException("Unexpected tool execution success while handling failure."),
                    Fail: failure => failure);

                runState.CompleteFailedStep(step, error.Message, stopwatch.ElapsedMilliseconds);
                await _traceWriter.WriteStepAsync(run, step, cancellationToken).ConfigureAwait(false);
                runState.MarkFailed(error.Message, "tool_execution_failed");
                await _traceWriter.WriteRunAsync(run, cancellationToken).ConfigureAwait(false);
                return Fin<AgentLoopRun>.Succ(run);
            }

            var toolResult = executionResult.Match(
                Succ: value => value,
                Fail: _ => throw new InvalidOperationException("Unexpected tool execution failure while handling success."));

            runState.CompleteExecutedStep(step, toolResult, stopwatch.ElapsedMilliseconds);

            var inspectionResult = await _resultInspector.InspectAsync(
                    context,
                    plannedStep,
                    toolResult,
                    cancellationToken)
                .ConfigureAwait(false);

            if (inspectionResult.IsFail)
            {
                var error = inspectionResult.Match(
                    Succ: _ => throw new InvalidOperationException("Unexpected inspection success while handling failure."),
                    Fail: failure => failure);

                runState.CompleteFailedStep(step, error.Message, stopwatch.ElapsedMilliseconds);
                await _traceWriter.WriteStepAsync(run, step, cancellationToken).ConfigureAwait(false);
                runState.MarkFailed(error.Message, "inspection_failed");
                await _traceWriter.WriteRunAsync(run, cancellationToken).ConfigureAwait(false);
                return Fin<AgentLoopRun>.Succ(run);
            }

            var inspection = inspectionResult.Match(
                Succ: value => value,
                Fail: _ => throw new InvalidOperationException("Unexpected inspection failure while handling success."));

            runState.ApplyInspection(step, inspection);

            await _traceWriter.WriteStepAsync(run, step, cancellationToken).ConfigureAwait(false);

            if (toolResult.Status == ToolExecutionStatus.Failed)
            {
                runState.MarkFailed(toolResult.ErrorMessage ?? "Tool execution failed.", "tool_reported_failure");
                await _traceWriter.WriteRunAsync(run, cancellationToken).ConfigureAwait(false);
                return Fin<AgentLoopRun>.Succ(run);
            }

            var loopValidationResult = await _loopValidator.ValidateAsync(context, cancellationToken)
                .ConfigureAwait(false);

            if (loopValidationResult.IsFail)
            {
                var error = loopValidationResult.Match(
                    Succ: _ => throw new InvalidOperationException("Unexpected loop validation success while handling failure."),
                    Fail: failure => failure);

                runState.MarkFailed(error.Message, "loop_validation_failed");
                await _traceWriter.WriteRunAsync(run, cancellationToken).ConfigureAwait(false);
                return Fin<AgentLoopRun>.Succ(run);
            }

            var loopValidation = loopValidationResult.Match(
                Succ: value => value,
                Fail: _ => throw new InvalidOperationException("Unexpected loop validation failure while handling success."));

            if (inspection.Decision == AgentDecisionType.StopSucceeded || loopValidation.Decision == AgentDecisionType.StopSucceeded)
            {
                runState.MarkCompleted(inspection.FinalResult ?? loopValidation.Message ?? step.OutputSummary);
                await _traceWriter.WriteRunAsync(run, cancellationToken).ConfigureAwait(false);
                return Fin<AgentLoopRun>.Succ(run);
            }

            if (inspection.Decision == AgentDecisionType.StopFailed || loopValidation.Decision == AgentDecisionType.StopFailed)
            {
                runState.MarkFailed(inspection.FinalResult ?? loopValidation.Message ?? "Loop validator stopped the run as failed.", "loop_stopped_failed");
                await _traceWriter.WriteRunAsync(run, cancellationToken).ConfigureAwait(false);
                return Fin<AgentLoopRun>.Succ(run);
            }

            runState.Touch();

            if (IsRuntimeLimitExceeded(loopStopwatch, loopSettings.MaxRuntimeSeconds))
            {
                runState.MarkFailed(
                    $"Autonomous loop reached max runtime ({loopSettings.MaxRuntimeSeconds}s) after step {sequence}.",
                    "max_runtime_reached");
                await _traceWriter.WriteRunAsync(run, cancellationToken).ConfigureAwait(false);
                return Fin<AgentLoopRun>.Succ(run);
            }
        }

        runState.MarkFailed($"Autonomous loop reached max steps ({loopSettings.MaxSteps}) without a stop decision.", "max_steps_reached");
        await _traceWriter.WriteRunAsync(run, cancellationToken).ConfigureAwait(false);
        return Fin<AgentLoopRun>.Succ(run);
    }

    private static Fin<AgentLoopRequest> ValidateRequest(AgentLoopRequest? request)
    {
        if (request is null)
        {
            return Error.New("Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Goal))
        {
            return Error.New("goal is required.");
        }

        if (request.MaxSteps is <= 0)
        {
            return Error.New("max_steps must be greater than zero when supplied.");
        }

        return Fin<AgentLoopRequest>.Succ(request);
    }

    private AgentLoopExecutionSettings ResolveLoopSettings(AgentLoopRequest request)
    {
        var loopOptions = _settings.AgentLoop;
        var configuredMaxSteps = loopOptions.MaxSteps;
        var maxSteps = request.MaxSteps is null
            ? configuredMaxSteps
            : Math.Min(request.MaxSteps.Value, configuredMaxSteps);

        var configuredCapabilities = NormalizeCapabilities(loopOptions.AllowedCapabilities);
        var effectiveCapabilities = ResolveEffectiveCapabilities(
            configuredCapabilities,
            request.AllowedCapabilities);

        return new AgentLoopExecutionSettings(
            maxSteps,
            loopOptions.MaxToolCalls,
            loopOptions.MaxRuntimeSeconds,
            effectiveCapabilities);
    }

    private static IReadOnlyList<string> ResolveEffectiveCapabilities(
        IReadOnlyList<string> configuredCapabilities,
        IEnumerable<string> requestedCapabilities)
    {
        var normalizedRequestedCapabilities = NormalizeCapabilities(requestedCapabilities);

        if (normalizedRequestedCapabilities.Count == 0)
        {
            return configuredCapabilities;
        }

        var configured = new System.Collections.Generic.HashSet<string>(
            configuredCapabilities,
            StringComparer.OrdinalIgnoreCase);
        var effective = new List<string>(normalizedRequestedCapabilities.Count);
        for (var i = 0; i < normalizedRequestedCapabilities.Count; i++)
        {
            var capability = normalizedRequestedCapabilities[i];
            if (configured.Contains(capability))
            {
                effective.Add(capability);
            }
        }

        effective.Sort(StringComparer.OrdinalIgnoreCase);
        return effective.ToArray();
    }

    private static IReadOnlyList<string> NormalizeCapabilities(IEnumerable<string> capabilities)
    {
        var normalized = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var initialCapacity = capabilities is ICollection<string> collection ? collection.Count : 0;

        foreach (var capability in capabilities)
        {
            if (string.IsNullOrWhiteSpace(capability))
            {
                continue;
            }

            normalized.Add(capability.Trim());
        }

        var sorted = initialCapacity > 0
            ? new List<string>(Math.Min(initialCapacity, normalized.Count))
            : new List<string>(normalized.Count);
        foreach (var capability in normalized)
        {
            sorted.Add(capability);
        }
        sorted.Sort(StringComparer.OrdinalIgnoreCase);
        return sorted.ToArray();
    }

    private sealed record AgentLoopExecutionSettings(
        int MaxSteps,
        int MaxToolCalls,
        int MaxRuntimeSeconds,
        IReadOnlyList<string> AllowedCapabilities);

    private static bool IsRuntimeLimitExceeded(
        Stopwatch stopwatch,
        int maxRuntimeSeconds)
    {
        return stopwatch.Elapsed >= TimeSpan.FromSeconds(Math.Max(1, maxRuntimeSeconds));
    }

}
