using McpServer.AgentRouter.Host.Configuration;
using Xunit;

namespace McpServer.AgentRouter.UnitTests.Configuration;

public sealed class ConfiguredAgentRouterRuntimeSettingsFactoryTests
{
    [Fact]
    public void Create_Should_Normalize_Child_Environment_Keys_For_Stdio_Host()
    {
        var options = new AgentRouterOptions
        {
            McpServer = new McpServerClientOptions
            {
                Enabled = true,
                ExecutablePath = "dotnet",
                WorkingDirectory = ".",
                WorkspaceRoot = "workspace",
                TimeoutSeconds = 20,
                DisableHighRiskTools = true,
                Environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["MCPSERVER:WEBACCESS:ENABLED"] = "true",
                    ["MCPSERVER:WEBACCESS:ALLOWLOCALLOOPBACKHOSTS"] = "true",
                    ["MCPSERVER:WEBACCESS:ALLOWEDHOSTS:0"] = "127.0.0.1"
                }
            }
        };

        var runtime = ConfiguredAgentRouterRuntimeSettingsFactory.Create(options);

        Assert.Equal("true", runtime.McpServer.Environment["MCPSERVER__WEBACCESS__ENABLED"]);
        Assert.Equal("true", runtime.McpServer.Environment["MCPSERVER__WEBACCESS__ALLOWLOCALLOOPBACKHOSTS"]);
        Assert.Equal("127.0.0.1", runtime.McpServer.Environment["MCPSERVER__WEBACCESS__ALLOWEDHOSTS__0"]);
        Assert.DoesNotContain(runtime.McpServer.Environment.Keys, key => key.Contains(':', StringComparison.Ordinal));
    }
}
