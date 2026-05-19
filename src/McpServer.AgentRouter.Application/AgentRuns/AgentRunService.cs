using LanguageExt;
using LanguageExt.Common;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.Runtime;
using Microsoft.Extensions.Logging;
using McpServer.AgentRouter.Domain.AgentRuns;
using McpServer.AgentRouter.Domain.Inference;

namespace McpServer.AgentRouter.Application.AgentRuns;

/// <summary>
/// Coordinates bounded single-turn agent runs.
/// </summary>
public sealed class AgentRunService : IAgentRunService
{
    private const string SystemPrompt = "You are the local MCPServer AgentRouter. Execute the requested development task as a bounded planning/generation run. Tool execution is not enabled in this milestone, so produce a concise, actionable result using only the supplied request context.";

    private readonly IModelRouter _modelRouter;
    private readonly IAgentRunStore _runStore;
    private readonly AgentRouterRuntimeSettings _settings;
    private readonly ILogger<AgentRunService> _logger;

    /// <summary>
    /// Initializes a new agent run service.
    /// </summary>
    public AgentRunService(
        IModelRouter modelRouter,
        IAgentRunStore runStore,
        AgentRouterRuntimeSettings settings,
        ILogger<AgentRunService> logger)
    {
        _modelRouter = modelRouter ?? throw new ArgumentNullException(nameof(modelRouter));
        _runStore = runStore ?? throw new ArgumentNullException(nameof(runStore));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Starts a new run, invokes the selected model, and persists the outcome.
    /// </summary>
    public async ValueTask<Fin<AgentRun>> StartRunAsync(
        AgentRunRequest? request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateRequest(request);
        if (validation.IsFail)
        {
            return validation.Match<Fin<AgentRun>>(
                Succ: _ => throw new InvalidOperationException("Unexpected agent run validation success."),
                Fail: error => error);
        }

        var validatedRequest = validation.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected agent run validation failure."));

        var state = AgentRunState.Start(
            ResolveModelProfileName(validatedRequest),
            validatedRequest.Goal!);
        var run = state.Run;

        await _runStore.SaveAsync(run, validatedRequest, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Started AgentRouter run {RunId} using model profile {ModelProfile}.",
            run.Id,
            run.Model);

        var invocation = new ModelInvocationRequest(
            modelProfileName: run.Model,
            messages: BuildRunMessages(validatedRequest),
            temperature: validatedRequest.Temperature,
            maxOutputTokens: validatedRequest.MaxTokens);

        var turnResult = await _modelRouter.CompleteAsync(invocation, cancellationToken)
            .ConfigureAwait(false);

        if (turnResult.IsFail)
        {
            var error = turnResult.Match(
                Succ: _ => throw new InvalidOperationException("Unexpected successful model turn while handling run failure."),
                Fail: failure => failure);

            MarkFailed(state, error.Message);
            await _runStore.SaveAsync(run, validatedRequest, cancellationToken).ConfigureAwait(false);

            _logger.LogWarning(
                "AgentRouter run {RunId} failed. Error: {ErrorMessage}",
                run.Id,
                error.Message);

            return Fin<AgentRun>.Succ(run);
        }

        var turn = turnResult.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Unexpected failed model turn while handling run success."));

        MarkCompleted(state, turn);
        await _runStore.SaveAsync(run, validatedRequest, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Completed AgentRouter run {RunId} using provider {Provider} model {Model} in {ElapsedMilliseconds}ms.",
            run.Id,
            turn.Provider,
            turn.Model,
            turn.ElapsedMilliseconds);

        return Fin<AgentRun>.Succ(run);
    }

    /// <summary>
    /// Retrieves a previously stored run.
    /// </summary>
    public ValueTask<Fin<AgentRun>> GetRunAsync(
        string runId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return new ValueTask<Fin<AgentRun>>(Error.New("run id is required."));
        }

        return _runStore.GetAsync(runId.Trim(), cancellationToken);
    }

    private static Fin<AgentRunRequest> ValidateRequest(AgentRunRequest? request)
    {
        if (request is null)
        {
            return Error.New("Request body is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Goal))
        {
            return Error.New("goal is required.");
        }

        return Fin<AgentRunRequest>.Succ(request);
    }

    private string ResolveModelProfileName(AgentRunRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Model))
        {
            return request.Model.Trim();
        }

        return _settings.DefaultProfile;
    }

    private static IReadOnlyList<ChatTurnMessage> BuildRunMessages(AgentRunRequest request)
    {
        var userPrompt = string.IsNullOrWhiteSpace(request.Instructions)
            ? $"Goal:\n{request.Goal!.Trim()}"
            : $"Goal:\n{request.Goal!.Trim()}\n\nAdditional instructions:\n{request.Instructions.Trim()}";

        return new[]
        {
            new ChatTurnMessage("system", SystemPrompt),
            new ChatTurnMessage("user", userPrompt)
        };
    }

    private static void MarkCompleted(
        AgentRunState state,
        ModelTurnResult turn)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(turn);

        state.MarkCompleted(turn.Content);
        state.AddArtifact(
            AgentRunArtifactTypes.Plan,
            AgentRunArtifactTypes.Plan,
            "Single-turn local model run. MCP tool execution is intentionally disabled in this milestone.");
        state.AddArtifact(
            AgentRunArtifactTypes.Generation,
            AgentRunArtifactTypes.Generation,
            turn.Content);
        state.AddArtifact(
            AgentRunArtifactTypes.Trace,
            AgentRunArtifactTypes.Trace,
            $"Provider: {turn.Provider}\nModel: {turn.Model}\nFinishReason: {turn.FinishReason}\nPromptTokens: {turn.PromptTokens}\nCompletionTokens: {turn.CompletionTokens}\nElapsedMilliseconds: {turn.ElapsedMilliseconds}");
    }

    private static void MarkFailed(
        AgentRunState state,
        string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        state.MarkFailed(errorMessage, "agent_run_failed");
        state.AddArtifact(
            AgentRunArtifactTypes.Trace,
            AgentRunArtifactTypes.Trace,
            errorMessage);
    }
}
