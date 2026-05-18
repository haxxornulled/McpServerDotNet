using System.Text.Json;
using System.Text.Json.Serialization;
using LanguageExt;
using LanguageExt.Common;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.Runtime;
using McpServer.AgentRouter.Domain.AgentRuns;
using Microsoft.Extensions.Logging;

namespace McpServer.AgentRouter.Infrastructure.Stores;

public sealed class FileSystemAgentRunStore : IAgentRunStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AgentRouterRuntimeSettings _settings;
    private readonly IAgentRouterRuntimePathResolver _pathResolver;
    private readonly ILogger<FileSystemAgentRunStore> _logger;

    public FileSystemAgentRunStore(
        AgentRouterRuntimeSettings settings,
        IAgentRouterRuntimePathResolver pathResolver,
        ILogger<FileSystemAgentRunStore> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask SaveAsync(
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

        var rootPath = ResolveStorageRootPath();
        var runDirectory = ResolveRunDirectory(rootPath, run.Id);

        Directory.CreateDirectory(runDirectory);

        if (request is not null)
        {
            await WriteJsonFileAsync(
                    Path.Combine(runDirectory, "request.json"),
                    MapToRecord(request),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await WriteJsonFileAsync(
                Path.Combine(runDirectory, "response.json"),
                MapToRecord(run),
                cancellationToken)
            .ConfigureAwait(false);

        if (_settings.RunStorage.WriteArtifactFiles)
        {
            await WriteArtifactFilesAsync(run, runDirectory, cancellationToken)
                .ConfigureAwait(false);
        }

        _logger.LogDebug(
            "Persisted AgentRouter run {RunId} to {RunDirectory}.",
            run.Id,
            runDirectory);
    }

    public async ValueTask<Fin<AgentRun>> GetAsync(
        string runId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(runId))
        {
            return Error.New("run id is required.");
        }

        if (!IsSafePathSegment(runId.Trim()))
        {
            return Error.New($"Agent run '{runId}' was not found.");
        }

        var rootPath = ResolveStorageRootPath();
        var runDirectory = ResolveRunDirectory(rootPath, runId.Trim());
        var responsePath = Path.Combine(runDirectory, "response.json");

        if (!File.Exists(responsePath))
        {
            return Error.New($"Agent run '{runId}' was not found.");
        }

        try
        {
            await using var stream = File.OpenRead(responsePath);
            var runRecord = await JsonSerializer.DeserializeAsync<AgentRunRecord>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            if (runRecord is null)
            {
                return Error.New($"Agent run '{runId}' could not be read.");
            }

            return Fin<AgentRun>.Succ(MapToDomain(runRecord));
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Could not deserialize AgentRouter run {RunId} from {ResponsePath}.",
                runId,
                responsePath);

            return Error.New($"Agent run '{runId}' could not be read.");
        }
        catch (IOException ex)
        {
            _logger.LogWarning(
                ex,
                "Could not read AgentRouter run {RunId} from {ResponsePath}.",
                runId,
                responsePath);

            return Error.New($"Agent run '{runId}' could not be read.");
        }
    }

    private string ResolveStorageRootPath()
    {
        var configuredPath = _settings.RunStorage.RootPath;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            configuredPath = Path.Combine("workspace", "artifacts", "agent-runs");
        }

        return _pathResolver.ResolveRelativeToContentRoot(configuredPath);
    }

    private static string ResolveRunDirectory(
        string rootPath,
        string runId)
    {
        var trimmedRunId = runId.Trim();
        if (!IsSafePathSegment(trimmedRunId))
        {
            throw new ArgumentException("Agent run id may only contain letters, numbers, dash, underscore, and period.", nameof(runId));
        }

        var normalizedRoot = Path.GetFullPath(rootPath);
        var runDirectory = Path.GetFullPath(Path.Combine(normalizedRoot, trimmedRunId));
        var rootWithSeparator = normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!runDirectory.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resolved agent run directory escaped the configured storage root.");
        }

        return runDirectory;
    }

    private static async Task WriteArtifactFilesAsync(
        AgentRun run,
        string runDirectory,
        CancellationToken cancellationToken)
    {
        if (run.Artifacts.Count == 0)
        {
            return;
        }

        await WriteJsonFileAsync(
                Path.Combine(runDirectory, "artifacts.json"),
                run.Artifacts.Select(MapToRecord).ToArray(),
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var artifact in run.Artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var artifactPath = Path.Combine(runDirectory, GetArtifactFileName(artifact));
            if (artifact.Type.Equals("trace", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonFileAsync(artifactPath, MapToRecord(artifact), cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            await WriteTextFileAsync(
                    artifactPath,
                    artifact.Content,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static string GetArtifactFileName(AgentRunArtifact artifact)
    {
        if (artifact.Type.Equals("plan", StringComparison.OrdinalIgnoreCase))
        {
            return "plan.md";
        }

        if (artifact.Type.Equals("generation", StringComparison.OrdinalIgnoreCase))
        {
            return "generation.md";
        }

        if (artifact.Type.Equals("trace", StringComparison.OrdinalIgnoreCase))
        {
            return "trace.json";
        }

        var baseName = string.IsNullOrWhiteSpace(artifact.Name)
            ? artifact.Type
            : artifact.Name;

        return SanitizeFileName($"{artifact.Type}-{baseName}-{artifact.Id}") + ".txt";
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(character => invalid.Contains(character) ? '-' : character).ToArray();
        return new string(chars).Trim('-', ' ', '.');
    }

    private static async Task WriteJsonFileAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        await WriteTextFileAsync(path, json, cancellationToken).ConfigureAwait(false);
    }

    private static AgentRunRequestRecord MapToRecord(AgentRunRequest request)
    {
        return new AgentRunRequestRecord
        {
            Model = request.Model,
            Goal = request.Goal,
            Instructions = request.Instructions,
            Temperature = request.Temperature,
            MaxTokens = request.MaxTokens
        };
    }

    private static AgentRunRecord MapToRecord(AgentRun run)
    {
        return new AgentRunRecord
        {
            Id = run.Id,
            Object = run.Object,
            Status = run.Status,
            Model = run.Model,
            Goal = run.Goal,
            CreatedAt = run.CreatedAt,
            UpdatedAt = run.UpdatedAt,
            CompletedAt = run.CompletedAt,
            Result = run.Result,
            Error = run.Error is null ? null : MapToRecord(run.Error),
            Artifacts = run.Artifacts.Select(MapToRecord).ToArray()
        };
    }

    private static AgentRunArtifactRecord MapToRecord(AgentRunArtifact artifact)
    {
        return new AgentRunArtifactRecord
        {
            Id = artifact.Id,
            Type = artifact.Type,
            Name = artifact.Name,
            Content = artifact.Content,
            CreatedAt = artifact.CreatedAt
        };
    }

    private static AgentRunErrorRecord MapToRecord(AgentRunError error)
    {
        return new AgentRunErrorRecord
        {
            Message = error.Message,
            Type = error.Type,
            Code = error.Code
        };
    }

    private static AgentRun MapToDomain(AgentRunRecord run)
    {
        return new AgentRun
        {
            Id = run.Id,
            Object = run.Object,
            Status = run.Status,
            Model = run.Model,
            Goal = run.Goal,
            CreatedAt = run.CreatedAt,
            UpdatedAt = run.UpdatedAt,
            CompletedAt = run.CompletedAt,
            Result = run.Result,
            Error = run.Error is null ? null : MapToDomain(run.Error),
            Artifacts = run.Artifacts.Select(MapToDomain).ToList()
        };
    }

    private static AgentRunArtifact MapToDomain(AgentRunArtifactRecord artifact)
    {
        return new AgentRunArtifact
        {
            Id = artifact.Id,
            Type = artifact.Type,
            Name = artifact.Name,
            Content = artifact.Content,
            CreatedAt = artifact.CreatedAt
        };
    }

    private static AgentRunError MapToDomain(AgentRunErrorRecord error)
    {
        return new AgentRunError
        {
            Message = error.Message,
            Type = error.Type,
            Code = error.Code
        };
    }

    private static async Task WriteTextFileAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(
                    temporaryPath,
                    content,
                    cancellationToken)
                .ConfigureAwait(false);

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool IsSafePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value is "." or "..")
        {
            return false;
        }

        return value.All(static character =>
            char.IsLetterOrDigit(character) ||
            character is '-' or '_' or '.');
    }
}

internal sealed class AgentRunRequestRecord
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("goal")]
    public string? Goal { get; set; }

    [JsonPropertyName("instructions")]
    public string? Instructions { get; set; }

    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; set; }
}

internal sealed class AgentRunRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("object")]
    public string Object { get; set; } = "agent.run";

    [JsonPropertyName("status")]
    public string Status { get; set; } = AgentRunStatusNames.Queued;

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("goal")]
    public string Goal { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("completed_at")]
    public DateTimeOffset? CompletedAt { get; set; }

    [JsonPropertyName("result")]
    public string? Result { get; set; }

    [JsonPropertyName("error")]
    public AgentRunErrorRecord? Error { get; set; }

    [JsonPropertyName("artifacts")]
    public IList<AgentRunArtifactRecord> Artifacts { get; set; } = new List<AgentRunArtifactRecord>();
}

internal sealed class AgentRunArtifactRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class AgentRunErrorRecord
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "agent_run_error";

    [JsonPropertyName("code")]
    public string Code { get; set; } = "agent_run_failed";
}
