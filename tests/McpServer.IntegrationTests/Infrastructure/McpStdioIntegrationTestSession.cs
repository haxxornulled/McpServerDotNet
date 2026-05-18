using System.Text.Json;

namespace McpServer.IntegrationTests.Infrastructure;

public sealed class McpStdioIntegrationTestSession : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private int _nextId = 1;
    private readonly StdioTestServerProcess _server;
    private readonly FramedJsonRpcTestClient _client;

    private McpStdioIntegrationTestSession(StdioTestServerProcess server)
    {
        _server = server;
        _client = new FramedJsonRpcTestClient(server.Input.BaseStream, server.Output.BaseStream);
    }

    public IReadOnlyCollection<string> StandardErrorLines => _server.StandardErrorLines;

    public static async Task<McpStdioIntegrationTestSession> StartInitializedAsync(
        string hostProjectPath,
        string workspaceRoot,
        IReadOnlyDictionary<string, string?>? additionalEnvironment = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostProjectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        Directory.CreateDirectory(workspaceRoot);

        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["MCPSERVER_DEFAULT_WORKSPACE_ROOT"] = workspaceRoot,
            ["MCPSERVER__WORKSPACE__ROOTPATH"] = workspaceRoot,
            ["MCPSERVER__WORKSPACE__ALLOWEDROOTS__0"] = workspaceRoot
        };

        if (additionalEnvironment is not null)
        {
            foreach (var item in additionalEnvironment)
            {
                environment[item.Key] = item.Value;
            }
        }

        var server = await StdioTestServerProcess.StartAsync(
                hostProjectPath,
                workspaceRoot,
                environment,
                cancellationToken)
            .ConfigureAwait(false);

        var session = new McpStdioIntegrationTestSession(server);
        await session.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    public async ValueTask DisposeAsync() => await _server.DisposeAsync().ConfigureAwait(false);

    public async Task<JsonDocument> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _client.SendRequestAsync(new
        {
            jsonrpc = "2.0",
            id = NextId(),
            method = "tools/list"
        }, cancellationToken).ConfigureAwait(false);

        return RequireResponse(response, "tools/list");
    }

    public async Task<JsonDocument> CallToolAsync(
        string toolName,
        object arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        var response = await _client.SendRequestAsync(new
        {
            jsonrpc = "2.0",
            id = NextId(),
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments = JsonSerializer.SerializeToElement(arguments, JsonOptions)
            }
        }, cancellationToken).ConfigureAwait(false);

        return RequireResponse(response, toolName);
    }

    public static void AssertJsonRpcSucceeded(JsonDocument response, string operationName)
    {
        if (response.RootElement.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException($"JSON-RPC operation '{operationName}' returned error: {error.GetRawText()}");
        }
    }

    public static void AssertToolSucceeded(JsonDocument response, string toolName)
    {
        AssertJsonRpcSucceeded(response, toolName);

        var result = response.RootElement.GetProperty("result");
        if (result.TryGetProperty("isError", out var isError) && isError.ValueKind is JsonValueKind.True)
        {
            throw new InvalidOperationException($"Tool '{toolName}' returned isError=true: {result.GetRawText()}");
        }

        if (!result.TryGetProperty("content", out var content) || content.ValueKind is not JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Tool '{toolName}' did not return a content array: {result.GetRawText()}");
        }
    }

    public static IReadOnlySet<string> GetToolNames(JsonDocument toolsResponse)
    {
        AssertJsonRpcSucceeded(toolsResponse, "tools/list");

        return toolsResponse.RootElement
            .GetProperty("result")
            .GetProperty("tools")
            .EnumerateArray()
            .Select(static tool => tool.GetProperty("name").GetString() ?? string.Empty)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var initializeResponse = await _client.SendRequestAsync(new
        {
            jsonrpc = "2.0",
            id = NextId(),
            method = "initialize",
            @params = new
            {
                protocolVersion = "2025-03-26",
                capabilities = new { },
                clientInfo = new { name = "mcp-tools-integration-tests", version = "1.0.0" }
            }
        }, cancellationToken).ConfigureAwait(false);

        var initialize = RequireResponse(initializeResponse, "initialize");
        AssertJsonRpcSucceeded(initialize, "initialize");
        initialize.Dispose();

        await _client.SendNotificationAsync(new
        {
            jsonrpc = "2.0",
            method = "notifications/initialized"
        }, cancellationToken).ConfigureAwait(false);
    }

    private int NextId() => Interlocked.Increment(ref _nextId);

    private static JsonDocument RequireResponse(JsonDocument? response, string operationName) =>
        response ?? throw new InvalidOperationException($"No JSON-RPC response was received for '{operationName}'.");
}
