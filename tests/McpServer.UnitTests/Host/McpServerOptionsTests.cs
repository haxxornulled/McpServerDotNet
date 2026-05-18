using McpServer.Host.Configuration;
using Xunit;

namespace McpServer.UnitTests.Host;

public sealed class McpServerOptionsTests
{
    [Fact]
    public void OllamaOptions_Should_Default_To_128k_Context_With_Bounded_Output()
    {
        var options = new McpServerOptions();

        Assert.False(options.Ollama.Enabled);
        Assert.Equal("http://127.0.0.1:11434", options.Ollama.BaseUrl);
        Assert.Equal(131_072, options.Ollama.ContextLength);
        Assert.Equal(32_000, options.Ollama.NumPredict);
        Assert.Equal(32_000, options.Ollama.MaxOutputChars);
        Assert.False(options.Ollama.AllowNonLoopbackBaseUrl);
    }

    [Fact]
    public void WorkspaceOptions_Should_Default_To_Application_Default_Workspace_Path()
    {
        var options = new McpServerOptions();

        Assert.Equal(string.Empty, options.Workspace.RootPath);
        Assert.Empty(options.Workspace.AllowedRoots);
        Assert.Empty(options.Workspace.AdditionalAllowedRoots);
        Assert.True(options.Workspace.AllowRuntimeWorkspaceOpen);
    }
}
