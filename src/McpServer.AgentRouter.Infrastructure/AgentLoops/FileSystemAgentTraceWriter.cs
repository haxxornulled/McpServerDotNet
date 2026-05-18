using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.Runtime;
using McpServer.AgentRouter.Domain.AgentLoops;

namespace McpServer.AgentRouter.Infrastructure.AgentLoops;

public sealed class FileSystemAgentTraceWriter : IAgentTraceWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AgentLoopRuntimeSettings _settings;
    private readonly IAgentRouterRuntimePathResolver _pathResolver;

    public FileSystemAgentTraceWriter(
        AgentLoopRuntimeSettings settings,
        IAgentRouterRuntimePathResolver pathResolver)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
    }

    public async ValueTask<Fin<Unit>> WriteStepAsync(
        AgentLoopRun run,
        AgentLoopStep step,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(step);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_settings.WriteTraceFiles || !_settings.TraceEveryStep)
        {
            return Prelude.unit;
        }

        try
        {
            var runDirectory = ResolveRunTraceDirectory(run.Id);
            var stepsDirectory = Path.Combine(runDirectory, "steps");
            Directory.CreateDirectory(stepsDirectory);

            var fileName = $"{step.Sequence:0000}-{SafeSegment(step.StepId)}.json";
            await WriteJsonFileAsync(
                    Path.Combine(stepsDirectory, fileName),
                    step,
                    cancellationToken)
                .ConfigureAwait(false);

            return Prelude.unit;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Error.New($"Failed to write agent loop step trace: {ex.Message}");
        }
    }

    public async ValueTask<Fin<Unit>> WriteRunAsync(
        AgentLoopRun run,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_settings.WriteTraceFiles)
        {
            return Prelude.unit;
        }

        try
        {
            var runDirectory = ResolveRunTraceDirectory(run.Id);
            Directory.CreateDirectory(runDirectory);

            await WriteJsonFileAsync(
                    Path.Combine(runDirectory, "run.json"),
                    run,
                    cancellationToken)
                .ConfigureAwait(false);

            return Prelude.unit;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Error.New($"Failed to write agent loop run trace: {ex.Message}");
        }
    }

    private string ResolveRunTraceDirectory(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new ArgumentException("Agent loop run id is required.", nameof(runId));
        }

        var root = ResolveTraceRootPath(_settings.TraceRootPath);
        var safeRunId = SafeSegment(runId.Trim());
        var directory = Path.GetFullPath(Path.Combine(root, safeRunId));
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!directory.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resolved agent loop trace directory escaped the configured trace root.");
        }

        return directory;
    }

    private string ResolveTraceRootPath(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            configuredPath = Path.Combine("workspace", "artifacts", "agent-loops");
        }

        return _pathResolver.ResolveRelativeToContentRoot(configuredPath);
    }

    private static async Task WriteJsonFileAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken)
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

    private static string SafeSegment(string value)
    {
        var trimmed = value.Trim();
        if (trimmed is "." or ".." || string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("Value is not a safe path segment.", nameof(value));
        }

        if (!trimmed.All(static character =>
                char.IsLetterOrDigit(character) ||
                character is '-' or '_' or '.'))
        {
            throw new ArgumentException("Value contains invalid path segment characters.", nameof(value));
        }

        return trimmed;
    }
}
