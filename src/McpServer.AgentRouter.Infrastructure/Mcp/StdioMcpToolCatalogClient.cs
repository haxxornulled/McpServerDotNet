using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.Mcp;
using McpServer.AgentRouter.Application.Runtime;
using Microsoft.Extensions.Logging;

namespace McpServer.AgentRouter.Infrastructure.Mcp;

public sealed class StdioMcpToolCatalogClient : IMcpToolCatalogClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly AgentRouterRuntimeSettings _settings;
    private readonly IAgentRouterRuntimePathResolver _pathResolver;
    private readonly ILogger<StdioMcpToolCatalogClient> _logger;

    public StdioMcpToolCatalogClient(
        AgentRouterRuntimeSettings settings,
        IAgentRouterRuntimePathResolver pathResolver,
        ILogger<StdioMcpToolCatalogClient> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<Fin<McpToolCatalogSnapshot>> ListToolsAsync(CancellationToken cancellationToken)
    {
        var options = _settings.McpServer;
        var validation = ValidateOptions(options);
        if (validation.IsFail)
        {
            return validation.Match<Fin<McpToolCatalogSnapshot>>(
                Succ: _ => throw new InvalidOperationException("Unexpected successful MCP client option validation."),
                Fail: error => error);
        }

        var executablePath = ResolvePath(options.ExecutablePath);
        var workingDirectory = ResolvePath(options.WorkingDirectory);
        var workspaceRoot = ResolvePath(options.WorkspaceRoot);

        if (!File.Exists(executablePath))
        {
            return Error.New($"MCP host executable was not found: {executablePath}");
        }

        if (!Directory.Exists(workingDirectory))
        {
            return Error.New($"MCP host working directory was not found: {workingDirectory}");
        }

        if (!Directory.Exists(workspaceRoot))
        {
            return Error.New($"MCP workspace root was not found: {workspaceRoot}");
        }

        var timeoutSeconds = Math.Clamp(options.TimeoutSeconds, 1, 300);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        var stopwatch = Stopwatch.StartNew();
        using var process = CreateProcess(options, executablePath, workingDirectory, workspaceRoot);

        try
        {
            if (!process.Start())
            {
                return Error.New("Failed to start MCP host process.");
            }

            var stderrTask = ReadStandardErrorAsync(process, timeoutCts.Token);
            var stdin = process.StandardInput.BaseStream;
            var stdout = process.StandardOutput.BaseStream;

            var initializeResponse = await SendRequestAndReadResponseAsync(
                    stdin,
                    stdout,
                    CreateInitializeRequest(),
                    timeoutCts.Token)
                .ConfigureAwait(false);

            if (initializeResponse.IsFail)
            {
                return await FailWithProcessContextAsync(
                        process,
                        stderrTask,
                        initializeResponse,
                        timeoutCts.Token)
                    .ConfigureAwait(false);
            }

            await WriteFramedJsonAsync(
                    stdin,
                    CreateInitializedNotification(),
                    timeoutCts.Token)
                .ConfigureAwait(false);

            var toolsResponse = await SendRequestAndReadResponseAsync(
                    stdin,
                    stdout,
                    CreateToolsListRequest(),
                    timeoutCts.Token)
                .ConfigureAwait(false);

            if (toolsResponse.IsFail)
            {
                return await FailWithProcessContextAsync(
                        process,
                        stderrTask,
                        toolsResponse,
                        timeoutCts.Token)
                    .ConfigureAwait(false);
            }

            stopwatch.Stop();

            var initialize = initializeResponse.Match(
                Succ: value => value,
                Fail: _ => throw new InvalidOperationException("Unexpected MCP initialize failure."));

            var tools = toolsResponse.Match(
                Succ: value => value,
                Fail: _ => throw new InvalidOperationException("Unexpected MCP tools/list failure."));

            var snapshotResult = CreateSnapshot(initialize, tools, stopwatch.ElapsedMilliseconds);
            if (snapshotResult.IsFail)
            {
                await StopProcessAsync(process, stderrTask, timeoutCts.Token).ConfigureAwait(false);
                return snapshotResult;
            }

            await TryGracefulShutdownAsync(stdin, stdout, timeoutCts.Token).ConfigureAwait(false);
            await StopProcessAsync(process, stderrTask, timeoutCts.Token).ConfigureAwait(false);

            return snapshotResult;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            await KillProcessIfRunningAsync(process).ConfigureAwait(false);
            return Error.New($"MCP host stdio request timed out after {timeoutSeconds} seconds.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            await KillProcessIfRunningAsync(process).ConfigureAwait(false);
            _logger.LogWarning(ex, "MCP host stdio tool catalog request failed.");
            return Error.New($"MCP host stdio request failed: {ex.Message}");
        }
    }

    private static Fin<Unit> ValidateOptions(McpServerClientRuntimeSettings options)
    {
        if (!options.Enabled)
        {
            return Error.New("MCP host stdio client is disabled by configuration.");
        }

        if (string.IsNullOrWhiteSpace(options.ExecutablePath))
        {
            return Error.New("AgentRouter:McpServer:ExecutablePath is required.");
        }

        if (string.IsNullOrWhiteSpace(options.WorkingDirectory))
        {
            return Error.New("AgentRouter:McpServer:WorkingDirectory is required.");
        }

        if (string.IsNullOrWhiteSpace(options.WorkspaceRoot))
        {
            return Error.New("AgentRouter:McpServer:WorkspaceRoot is required.");
        }

        return Prelude.unit;
    }

    private static Process CreateProcess(
        McpServerClientRuntimeSettings options,
        string executablePath,
        string workingDirectory,
        string workspaceRoot)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["MCPSERVER__WORKSPACE__ROOTPATH"] = workspaceRoot;
        startInfo.Environment["MCPSERVER__WORKSPACE__ALLOWEDROOTS__0"] = workspaceRoot;

        if (options.DisableHighRiskTools)
        {
            startInfo.Environment["MCPSERVER__SHELL__ENABLED"] = "false";
            startInfo.Environment["MCPSERVER__WEBACCESS__ENABLED"] = "false";
            startInfo.Environment["MCPSERVER__SSH__ENABLED"] = "false";
        }

        foreach (var item in options.Environment)
        {
            if (string.IsNullOrWhiteSpace(item.Key))
            {
                continue;
            }

            if (item.Value is null)
            {
                startInfo.Environment.Remove(item.Key);
                continue;
            }

            startInfo.Environment[item.Key] = item.Value;
        }

        return new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = false
        };
    }

    private static async ValueTask<Fin<JsonDocument>> SendRequestAndReadResponseAsync(
        Stream inputStream,
        Stream outputStream,
        string json,
        CancellationToken cancellationToken)
    {
        await WriteFramedJsonAsync(inputStream, json, cancellationToken).ConfigureAwait(false);
        return await ReadResponseAsync(outputStream, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask WriteFramedJsonAsync(
        Stream stream,
        string json,
        CancellationToken cancellationToken)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(json);
        var headerBytes = Encoding.ASCII.GetBytes(
            $"Content-Length: {bodyBytes.Length.ToString(CultureInfo.InvariantCulture)}\r\n\r\n");

        await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bodyBytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<Fin<JsonDocument>> ReadResponseAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var message = await ReadJsonRpcMessageAsync(stream, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(message))
            {
                return Error.New("MCP host closed stdout before returning a JSON-RPC response.");
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(message);
            }
            catch (JsonException ex)
            {
                return Error.New($"MCP host returned invalid JSON-RPC output: {ex.Message}");
            }

            if (!document.RootElement.TryGetProperty("id", out _))
            {
                document.Dispose();
                continue;
            }

            if (document.RootElement.TryGetProperty("error", out var errorElement) &&
                errorElement.ValueKind != JsonValueKind.Null)
            {
                var messageText = ExtractJsonRpcErrorMessage(errorElement);
                document.Dispose();
                return Error.New($"MCP host returned JSON-RPC error: {messageText}");
            }

            return Fin<JsonDocument>.Succ(document);
        }
    }

    private static async ValueTask<string?> ReadJsonRpcMessageAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var firstLine = await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false);
        if (firstLine is null)
        {
            return null;
        }

        if (firstLine.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
        {
            var lengthText = firstLine["Content-Length:".Length..].Trim();
            var contentLength = int.Parse(lengthText, CultureInfo.InvariantCulture);

            while (true)
            {
                var headerLine = await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false);
                if (headerLine is null || headerLine.Length == 0)
                {
                    break;
                }
            }

            var buffer = new byte[contentLength];
            var offset = 0;
            while (offset < contentLength)
            {
                var read = await stream.ReadAsync(
                        buffer.AsMemory(offset, contentLength - offset),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (read <= 0)
                {
                    throw new EndOfStreamException("MCP host closed stdout while reading framed response body.");
                }

                offset += read;
            }

            return Encoding.UTF8.GetString(buffer);
        }

        return firstLine;
    }

    private static async ValueTask<string?> ReadLineAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var single = new byte[1];

        while (true)
        {
            var read = await stream.ReadAsync(single.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                if (bytes.Count == 0)
                {
                    return null;
                }

                break;
            }

            if (single[0] == 10)
            {
                break;
            }

            bytes.Add(single[0]);
        }

        if (bytes.Count > 0 && bytes[^1] == 13)
        {
            bytes.RemoveAt(bytes.Count - 1);
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static Fin<McpToolCatalogSnapshot> CreateSnapshot(
        JsonDocument initializeResponse,
        JsonDocument toolsResponse,
        long elapsedMilliseconds)
    {
        using (initializeResponse)
        using (toolsResponse)
        {
            if (!initializeResponse.RootElement.TryGetProperty("result", out var initializeResult))
            {
                return Error.New("MCP initialize response did not contain result.");
            }

            if (!toolsResponse.RootElement.TryGetProperty("result", out var toolsResult))
            {
                return Error.New("MCP tools/list response did not contain result.");
            }

            var snapshot = new McpToolCatalogSnapshot
            {
                Transport = "stdio",
                ProtocolVersion = GetOptionalString(initializeResult, "protocolVersion") ?? string.Empty,
                ServerName = string.Empty,
                ServerVersion = string.Empty,
                ElapsedMilliseconds = elapsedMilliseconds
            };

            if (initializeResult.TryGetProperty("serverInfo", out var serverInfo))
            {
                snapshot.ServerName = GetOptionalString(serverInfo, "name") ?? string.Empty;
                snapshot.ServerVersion = GetOptionalString(serverInfo, "version") ?? string.Empty;
            }

            if (toolsResult.TryGetProperty("tools", out var toolsElement) &&
                toolsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var toolElement in toolsElement.EnumerateArray())
                {
                    var name = GetOptionalString(toolElement, "name");
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    JsonElement? inputSchema = null;
                    if (toolElement.TryGetProperty("inputSchema", out var inputSchemaElement) &&
                        inputSchemaElement.ValueKind != JsonValueKind.Null &&
                        inputSchemaElement.ValueKind != JsonValueKind.Undefined)
                    {
                        inputSchema = inputSchemaElement.Clone();
                    }

                    snapshot.Tools.Add(new McpToolCatalogItem
                    {
                        Name = name,
                        Title = GetOptionalString(toolElement, "title"),
                        Description = GetOptionalString(toolElement, "description"),
                        InputSchema = inputSchema
                    });
                }
            }

            return Fin<McpToolCatalogSnapshot>.Succ(snapshot);
        }
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Null || property.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        return property.GetString();
    }

    private static string ExtractJsonRpcErrorMessage(JsonElement errorElement)
    {
        if (errorElement.TryGetProperty("message", out var messageElement))
        {
            return messageElement.GetString() ?? "Unknown JSON-RPC error.";
        }

        return errorElement.ToString();
    }

    private static string CreateInitializeRequest()
    {
        return JsonSerializer.Serialize(
            new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2025-03-26",
                    capabilities = new { },
                    clientInfo = new
                    {
                        name = "mcpserver-agentrouter",
                        version = "1.0.0"
                    }
                }
            },
            SerializerOptions);
    }

    private static string CreateInitializedNotification()
    {
        return JsonSerializer.Serialize(
            new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized"
            },
            SerializerOptions);
    }

    private static string CreateToolsListRequest()
    {
        return JsonSerializer.Serialize(
            new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/list"
            },
            SerializerOptions);
    }

    private static async ValueTask TryGracefulShutdownAsync(
        Stream inputStream,
        Stream outputStream,
        CancellationToken cancellationToken)
    {
        try
        {
            await SendRequestAndReadResponseAsync(
                    inputStream,
                    outputStream,
                    JsonSerializer.Serialize(
                        new
                        {
                            jsonrpc = "2.0",
                            id = 3,
                            method = "shutdown",
                            @params = new { }
                        },
                        SerializerOptions),
                    cancellationToken)
                .ConfigureAwait(false);

            await WriteFramedJsonAsync(
                    inputStream,
                    JsonSerializer.Serialize(
                        new
                        {
                            jsonrpc = "2.0",
                            method = "exit"
                        },
                        SerializerOptions),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // Best-effort lifecycle cleanup only. The caller still kills the child if it remains alive.
        }
    }

    private static async ValueTask<Fin<McpToolCatalogSnapshot>> FailWithProcessContextAsync(
        Process process,
        Task<string> stderrTask,
        Fin<JsonDocument> failure,
        CancellationToken cancellationToken)
    {
        await StopProcessAsync(process, stderrTask, cancellationToken).ConfigureAwait(false);

        return failure.Match<Fin<McpToolCatalogSnapshot>>(
            Succ: _ => throw new InvalidOperationException("Expected failed JSON-RPC response."),
            Fail: error => error);
    }

    private static async ValueTask StopProcessAsync(
        Process process,
        Task<string> stderrTask,
        CancellationToken cancellationToken)
    {
        if (!process.HasExited)
        {
            try
            {
                process.StandardInput.Close();
            }
            catch
            {
                // Ignore shutdown cleanup errors.
            }

            using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            waitCts.CancelAfter(TimeSpan.FromSeconds(2));

            try
            {
                await process.WaitForExitAsync(waitCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await KillProcessIfRunningAsync(process).ConfigureAwait(false);
            }
        }

        try
        {
            var stderr = await stderrTask.ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                // Server logs are useful during manual diagnostics, but stderr is not a failure by itself.
            }
        }
        catch
        {
            // Ignore stderr read cleanup errors.
        }
    }

    private static async Task<string> ReadStandardErrorAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        try
        {
            return await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }
    }

    private static async ValueTask KillProcessIfRunningAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            // Ignore cleanup errors.
        }
    }

    private string ResolvePath(string path)
    {
        return _pathResolver.ResolveRelativeToContentRoot(path);
    }
}
