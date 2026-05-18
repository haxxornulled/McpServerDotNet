using McpServer.AgentRouter.Application.Services;
using McpServer.AgentRouter.Domain.Inference;
using Xunit;

namespace McpServer.AgentRouter.UnitTests;

public sealed class ConfigurationModelProfileResolverTests
{
    [Fact]
    public async Task ResolveAsync_UsesDefaultProfile_WhenNameIsMissing()
    {
        var resolver = new ConfigurationModelProfileResolver(TestRuntimeSettings.Create());

        var result = await resolver.ResolveAsync(null, CancellationToken.None);

        Assert.True(result.IsSucc);
        result.IfSucc(profile =>
        {
            Assert.Equal("local-code", profile.Name);
            Assert.Equal("Ollama", profile.Provider);
            Assert.Equal("qwen3-coder:30b", profile.Model);
        });
    }

    [Fact]
    public async Task ResolveAsync_RejectsNonLoopbackBaseUrl_ByDefault()
    {
        var profiles = new Dictionary<string, ModelProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["local-code"] = new(
                "local-code",
                "Ollama",
                "qwen3-coder:30b",
                new Uri("http://127.0.0.1:11434/"),
                131072,
                32000,
                0.15d,
                false,
                false,
                900)
        };
        var resolver = new ConfigurationModelProfileResolver(TestRuntimeSettings.Create(
            modelProfiles: profiles));

        var result = await resolver.ResolveAsync("remote", CancellationToken.None);

        Assert.True(result.IsFail);
    }

    [Fact]
    public void ListProfiles_ReturnsConfiguredProfiles_WithDefaultProfileFirst()
    {
        var resolver = new ConfigurationModelProfileResolver(TestRuntimeSettings.Create());

        var profiles = resolver.ListProfiles();

        Assert.Equal("local-code", profiles[0].Name);
        Assert.Contains(profiles, profile => profile.Name == "local-code");
        Assert.Contains(profiles, profile => profile.Name == "fast-local");
    }

}
