using System.Collections.Concurrent;
using LanguageExt;
using LanguageExt.Common;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Domain.AgentRuns;

namespace McpServer.AgentRouter.Application.Stores;

public sealed class InMemoryAgentRunStore : IAgentRunStore
{
    private readonly ConcurrentDictionary<string, AgentRun> _runs = new(StringComparer.OrdinalIgnoreCase);

    public ValueTask SaveAsync(
        AgentRun run,
        AgentRunRequest? request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(run.Id))
        {
            throw new ArgumentException("Agent run id is required.", nameof(run));
        }

        _runs[run.Id] = Clone(run);
        return ValueTask.CompletedTask;
    }

    public ValueTask<Fin<AgentRun>> GetAsync(
        string runId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(runId))
        {
            return new ValueTask<Fin<AgentRun>>(Error.New("run id is required."));
        }

        if (!_runs.TryGetValue(runId.Trim(), out var run))
        {
            return new ValueTask<Fin<AgentRun>>(Error.New($"Agent run '{runId}' was not found."));
        }

        return new ValueTask<Fin<AgentRun>>(Fin<AgentRun>.Succ(Clone(run)));
    }

    private static AgentRun Clone(AgentRun source)
    {
        var clone = new AgentRun
        {
            Id = source.Id,
            Object = source.Object,
            Status = source.Status,
            Model = source.Model,
            Goal = source.Goal,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt,
            CompletedAt = source.CompletedAt,
            Result = source.Result,
            Error = source.Error is null
                ? null
                : new AgentRunError
                {
                    Message = source.Error.Message,
                    Type = source.Error.Type,
                    Code = source.Error.Code
                }
        };

        foreach (var artifact in source.Artifacts)
        {
            clone.Artifacts.Add(new AgentRunArtifact
            {
                Id = artifact.Id,
                Type = artifact.Type,
                Name = artifact.Name,
                Content = artifact.Content,
                CreatedAt = artifact.CreatedAt
            });
        }

        return clone;
    }
}
