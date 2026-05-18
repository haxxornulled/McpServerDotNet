using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.Runtime;
using McpServer.AgentRouter.Domain.Mcp;

namespace McpServer.AgentRouter.Infrastructure.Mcp;

public sealed class FileSystemMcpToolCallTraceWriter : IMcpToolCallTraceWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly McpToolExecutionRuntimeSettings _settings;
    private readonly IAgentRouterRuntimePathResolver _pathResolver;

    public FileSystemMcpToolCallTraceWriter(
        McpToolExecutionRuntimeSettings settings,
        IAgentRouterRuntimePathResolver pathResolver)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
    }

    public async ValueTask<Fin<Unit>> WriteAsync(
        McpToolCallTraceRecord traceRecord,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(traceRecord);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_settings.WriteTraceFiles)
        {
            return Prelude.unit;
        }

        if (string.IsNullOrWhiteSpace(traceRecord.TraceId))
        {
            return Error.New("trace id is required.");
        }

        if (traceRecord.TraceId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return Error.New($"trace id '{traceRecord.TraceId}' is not a valid path segment.");
        }

        try
        {
            var rootPath = ResolveTraceRootPath(_settings.TraceRootPath);
            var traceDirectory = Path.Combine(rootPath, traceRecord.TraceId);
            Directory.CreateDirectory(traceDirectory);

            var tracePath = Path.Combine(traceDirectory, "trace.json");
            var json = JsonSerializer.Serialize(traceRecord, SerializerOptions);
            await File.WriteAllTextAsync(tracePath, json, cancellationToken).ConfigureAwait(false);

            return Prelude.unit;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Error.New($"Failed to write MCP tool call trace: {ex.Message}");
        }
    }

    private string ResolveTraceRootPath(string configuredPath)
    {
        return _pathResolver.ResolveConfiguredPath(
            configuredPath,
            Path.Combine("workspace", "artifacts", "mcp-tool-calls"));
    }
}
