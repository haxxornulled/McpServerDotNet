using System.Text.Json;
using McpServer.IntegrationTests.Infrastructure;
using Xunit;

namespace McpServer.IntegrationTests.Protocol;

public sealed class McpOptionalToolCoverageIntegrationTests
{
    private static string HostProjectPath =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "McpServer.Host", "McpServer.Host.csproj"));

    [Fact]
    public async Task Shell_Tool_Should_Execute_Approved_Command_End_To_End()
    {
        var workspaceRoot = CreateWorkspaceRoot();
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["MCPSERVER__SHELL__ENABLED"] = "true",
            ["MCPSERVER__SHELL__ALLOWSHELLFALLBACK"] = "false",
            ["MCPSERVER__SHELL__ALLOWWINDOWSCOMPATIBILITYSHELL"] = "false",
            ["MCPSERVER__SHELL__ALLOWEDCOMMANDS__0"] = "dotnet",
            ["MCPSERVER__SHELL__MAXTIMEOUTSECONDS"] = "60",
            ["MCPSERVER__SHELL__MAXOUTPUTCHARS"] = "200000"
        };

        await using var session = await McpStdioIntegrationTestSession.StartInitializedAsync(HostProjectPath, workspaceRoot, environment);

        using var listResponse = await session.ListToolsAsync();
        Assert.Contains("shell.exec", McpStdioIntegrationTestSession.GetToolNames(listResponse));

        using var response = await session.CallToolAsync("shell.exec", new
        {
            command = "dotnet",
            args = new[] { "--info" },
            workingDirectory = ".",
            timeoutSeconds = 60,
            maxOutputChars = 200000
        });

        McpStdioIntegrationTestSession.AssertToolSucceeded(response, "shell.exec");
    }

    [Fact]
    public async Task Local_Inference_Tools_Should_Execute_With_Fake_Ollama_Server()
    {
        var workspaceRoot = CreateWorkspaceRoot();
        var model = "qwen3-coder:30b";

        await using var ollama = await FakeOllamaServer.StartAsync(
            availableModels: new[] { model },
            chatResponseText: "fake response");

        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["MCPSERVER__OLLAMA__ENABLED"] = "true",
            ["MCPSERVER__OLLAMA__BASEURL"] = ollama.BaseAddress.GetLeftPart(UriPartial.Authority),
            ["MCPSERVER__OLLAMA__DEFAULTMODEL"] = model,
            ["MCPSERVER__OLLAMA__ALLOWEDMODELS__0"] = model,
            ["MCPSERVER__OLLAMA__TIMEOUTSECONDS"] = "120",
            ["MCPSERVER__OLLAMA__MAXTIMEOUTSECONDS"] = "180",
            ["MCPSERVER__OLLAMA__MAXPROMPTCHARS"] = "20000",
            ["MCPSERVER__OLLAMA__MAXOUTPUTCHARS"] = "4096",
            ["MCPSERVER__OLLAMA__NUMPREDICT"] = "128"
        };

        await using var session = await McpStdioIntegrationTestSession.StartInitializedAsync(HostProjectPath, workspaceRoot, environment);

        using var listResponse = await session.ListToolsAsync();
        var toolNames = McpStdioIntegrationTestSession.GetToolNames(listResponse);
        Assert.Contains("inference.local_status", toolNames);
        Assert.Contains("inference.local_complete", toolNames);
        Assert.Contains("inference.local_summarize", toolNames);
        Assert.Contains("inference.local_code_review", toolNames);
        Assert.Contains("inference.local_plan", toolNames);

        using (var statusResponse = await session.CallToolAsync("inference.local_status", new { }))
        {
            McpStdioIntegrationTestSession.AssertToolSucceeded(statusResponse, "inference.local_status");
            var statusText = ReadSingleTextContent(statusResponse);
            using var statusJson = JsonDocument.Parse(statusText);
            Assert.Equal("ollama", statusJson.RootElement.GetProperty("Provider").GetString());
            Assert.True(statusJson.RootElement.GetProperty("Enabled").GetBoolean());
            Assert.Equal(ollama.BaseAddress.AbsoluteUri, statusJson.RootElement.GetProperty("BaseUrl").GetString());
            Assert.Equal(model, statusJson.RootElement.GetProperty("DefaultModel").GetString());
            Assert.True(statusJson.RootElement.GetProperty("ServerReachable").GetBoolean());
            Assert.Contains(
                statusJson.RootElement.GetProperty("AvailableModels").EnumerateArray().Select(static item => item.GetString()),
                value => string.Equals(value, model, StringComparison.OrdinalIgnoreCase));
        }

        await AssertInferenceResponseAsync(
            session,
            "inference.local_complete",
            new
            {
                prompt = "Reply with exactly: ok",
                model,
                maxOutputChars = 256,
                timeoutSeconds = 120
            },
            expectedOperation: "complete",
                expectedResponse: "fake response",
            expectedModel: model);
        await AssertInferenceResponseAsync(
            session,
            "inference.local_summarize",
            new
            {
                text = "This repository contains an MCP server and an AgentRouter host.",
                focus = "one sentence",
                model,
                maxOutputChars = 256,
                timeoutSeconds = 120
            },
                expectedOperation: "summarize",
            expectedResponse: "fake response",
            expectedModel: model);

        await AssertInferenceResponseAsync(
            session,
            "inference.local_plan",
            new
            {
                goal = "Add a small integration test.",
                context = "Use a safe test-only change.",
                model,
                maxOutputChars = 256,
                timeoutSeconds = 120
            },
                expectedOperation: "plan",
            expectedResponse: "fake response",
            expectedModel: model);

        await AssertInferenceResponseAsync(
            session,
            "inference.local_code_review",
            new
            {
                content = "public sealed class Sample { public int Value { get; init; } }",
                filePath = "Sample.cs",
                instructions = "Return one short observation.",
                model,
                maxOutputChars = 256,
                timeoutSeconds = 120
            },
            expectedOperation: "code_review",
            expectedResponse: "fake response",
            expectedModel: model);

        Assert.Contains(ollama.RequestPaths, static path => string.Equals(path, "/api/tags", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(4, ollama.RequestBodies.Count);
        Assert.All(ollama.RequestBodies, body =>
        {
            using var document = JsonDocument.Parse(body);
            Assert.False(document.RootElement.GetProperty("stream").GetBoolean());
            Assert.Equal(model, document.RootElement.GetProperty("model").GetString());
        });
    }

    [Fact]
    public async Task Web_Tools_Should_Execute_End_To_End_On_Loopback()
    {
        var workspaceRoot = CreateWorkspaceRoot();
        await using var web = await LoopbackWebServer.StartAsync();

        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["MCPSERVER__WEBACCESS__ENABLED"] = "true",
            ["MCPSERVER__WEBACCESS__ALLOWLOCALLOOPBACKHOSTS"] = "true",
            ["MCPSERVER__WEBACCESS__ALLOWEDHOSTS__0"] = "127.0.0.1",
            ["MCPSERVER__WEBACCESS__SEARCHBASEURL"] = web.SearchBaseUrl
        };

        await using var session = await McpStdioIntegrationTestSession.StartInitializedAsync(HostProjectPath, workspaceRoot, environment);

        using var listResponse = await session.ListToolsAsync();
        var toolNames = McpStdioIntegrationTestSession.GetToolNames(listResponse);
        Assert.Contains("web.fetch_url", toolNames);
        Assert.Contains("web.search", toolNames);

        using var fetchResponse = await session.CallToolAsync("web.fetch_url", new
        {
            url = web.FetchUrl,
            timeout_seconds = 30
        });
        McpStdioIntegrationTestSession.AssertToolSucceeded(fetchResponse, "web.fetch_url");
        var fetchText = ReadSingleTextContent(fetchResponse);
        Assert.Contains("Loopback Fetch", fetchText, StringComparison.Ordinal);

        using var searchResponse = await session.CallToolAsync("web.search", new
        {
            query = "MCP server protocol",
            maxResults = 1
        });
        McpStdioIntegrationTestSession.AssertToolSucceeded(searchResponse, "web.search");
        var searchText = ReadSingleTextContent(searchResponse);
        using var searchJson = JsonDocument.Parse(searchText);
        Assert.Equal("MCP server protocol", searchJson.RootElement.GetProperty("query").GetString());
        Assert.Equal(1, searchJson.RootElement.GetProperty("result_count").GetInt32());
        Assert.Contains(web.RequestPaths, static path => path.StartsWith("/search", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Ssh_Tools_Should_Execute_End_To_End_On_Test_Backend()
    {
        var workspaceRoot = CreateWorkspaceRoot();
        var profileName = Environment.GetEnvironmentVariable("MCPSERVER_INTEGRATION_SSH_PROFILE") ?? "integration";
        var command = Environment.GetEnvironmentVariable("MCPSERVER_INTEGRATION_SSH_COMMAND") ?? "pwd";
        var remoteWritePath = Environment.GetEnvironmentVariable("MCPSERVER_INTEGRATION_SSH_WRITE_PATH") ?? "/workspace/integration-output.txt";
        var backendRoot = CreateBackendRoot();

        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["MCPSERVER__SSH__ENABLED"] = "true",
            ["MCPSERVER__SSH__USETESTBACKEND"] = "true",
            ["MCPSERVER__SSH__TESTBACKENDROOTPATH"] = backendRoot,
            ["MCPSERVER__SSH__PROFILES__0__NAME"] = profileName,
            ["MCPSERVER__SSH__PROFILES__0__HOST"] = "loopback",
            ["MCPSERVER__SSH__PROFILES__0__PORT"] = "22",
            ["MCPSERVER__SSH__PROFILES__0__USERNAME"] = "integration",
            ["MCPSERVER__SSH__PROFILES__0__WORKINGDIRECTORY"] = "/workspace",
            ["MCPSERVER__SSH__PROFILES__0__ALLOWEDCOMMANDS__0"] = command,
            ["MCPSERVER__SSH__PROFILES__0__ALLOWEDREMOTEPATHPREFIXES__0"] = "/workspace"
        };

        await using var session = await McpStdioIntegrationTestSession.StartInitializedAsync(HostProjectPath, workspaceRoot, environment);

        using var listResponse = await session.ListToolsAsync();
        var toolNames = McpStdioIntegrationTestSession.GetToolNames(listResponse);
        Assert.Contains("ssh.execute", toolNames);
        Assert.Contains("ssh.write_text", toolNames);

        using var executeResponse = await session.CallToolAsync("ssh.execute", new
        {
            profile = profileName,
            command
        });
        McpStdioIntegrationTestSession.AssertToolSucceeded(executeResponse, "ssh.execute");
        var executeText = ReadSingleTextContent(executeResponse);
        Assert.Contains($"profile={profileName}", executeText, StringComparison.Ordinal);
        Assert.Contains($"command={command}", executeText, StringComparison.Ordinal);

        var content = $"McpServer integration test {DateTimeOffset.UtcNow:O}{Environment.NewLine}";
        using var writeResponse = await session.CallToolAsync("ssh.write_text", new
        {
            profile = profileName,
            path = remoteWritePath,
            content
        });
        McpStdioIntegrationTestSession.AssertToolSucceeded(writeResponse, "ssh.write_text");

        var localWritePath = Path.Combine(
            backendRoot,
            remoteWritePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(localWritePath), $"Expected test backend file to exist at '{localWritePath}'.");
        Assert.Contains(content, await File.ReadAllTextAsync(localWritePath));
    }

    private static async Task AssertToolSucceeds(
        McpStdioIntegrationTestSession session,
        string toolName,
        object arguments)
    {
        using var response = await session.CallToolAsync(toolName, arguments);
        McpStdioIntegrationTestSession.AssertToolSucceeded(response, toolName);
    }

    private static async Task AssertInferenceResponseAsync(
        McpStdioIntegrationTestSession session,
        string toolName,
        object arguments,
        string expectedOperation,
        string expectedResponse,
        string expectedModel)
    {
        using var response = await session.CallToolAsync(toolName, arguments);
        McpStdioIntegrationTestSession.AssertToolSucceeded(response, toolName);

        var text = ReadSingleTextContent(response);
        using var document = JsonDocument.Parse(text);
        Assert.Equal("ollama", document.RootElement.GetProperty("Provider").GetString());
        Assert.Equal(expectedModel, document.RootElement.GetProperty("Model").GetString());
        Assert.Equal(expectedOperation, document.RootElement.GetProperty("Operation").GetString());
        Assert.Equal(expectedResponse, document.RootElement.GetProperty("Response").GetString());
        Assert.False(document.RootElement.GetProperty("Truncated").GetBoolean());
    }

    private static string ReadSingleTextContent(JsonDocument response)
    {
        var result = response.RootElement.GetProperty("result");
        var content = result.GetProperty("content").EnumerateArray().Single();
        return content.GetProperty("text").GetString()
            ?? throw new InvalidOperationException("Tool response text content was missing.");
    }

    private static string CreateWorkspaceRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "mcpserver-optional-tool-integration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateBackendRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "mcpserver-ssh-test-backend", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string RequiredEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        Assert.False(string.IsNullOrWhiteSpace(value), $"Environment variable '{name}' is required for this opt-in integration test.");
        return value;
    }
}
