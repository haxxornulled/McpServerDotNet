using System.Text.Json;
using LanguageExt;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.Runtime;
using McpServer.AgentRouter.Domain.Ssh;

namespace McpServer.AgentRouter.Infrastructure.Ssh;

public sealed class FileSystemSshExecutionTraceWriter : ISshExecutionTraceWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SshExecutionRuntimeSettings _settings;
    private readonly IAgentRouterRuntimePathResolver _pathResolver;

    public FileSystemSshExecutionTraceWriter(
        SshExecutionRuntimeSettings settings,
        IAgentRouterRuntimePathResolver pathResolver)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
    }

    public async ValueTask<Fin<Unit>> WriteAsync(
        SshExecutionTraceRecord traceRecord,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(traceRecord);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            if (!_settings.WriteTraceFiles)
            {
                return Prelude.unit;
            }

            var traceId = string.IsNullOrWhiteSpace(traceRecord.TraceId)
                ? "ssh-exec-unknown"
                : SanitizePathSegment(traceRecord.TraceId);

            var root = ResolveConfiguredPath(_settings.TraceRootPath, Path.Combine("workspace", "artifacts", "ssh-exec"));
            var directory = Path.Combine(root, traceId);
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, "trace.json");
            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, traceRecord, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            return Prelude.unit;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return LanguageExt.Common.Error.New($"Failed to write SSH execution trace: {ex.Message}");
        }
    }

    private string ResolveConfiguredPath(
        string configuredPath,
        string defaultPath)
    {
        return _pathResolver.ResolveConfiguredPath(configuredPath, defaultPath);
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value
            .Select(character => invalid.Contains(character) ? '-' : character)
            .ToArray();

        return new string(chars);
    }
}
