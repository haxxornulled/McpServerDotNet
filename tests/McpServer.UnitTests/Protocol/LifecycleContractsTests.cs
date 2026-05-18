using System.Text.Json;
using McpServer.Protocol.Lifecycle;
using Xunit;

namespace McpServer.UnitTests.Protocol;

public sealed class LifecycleContractsTests
{
    [Fact]
    public void InitializeRequestDto_Should_Normalize_Missing_Optional_Objects()
    {
        var dto = JsonSerializer.Deserialize<InitializeRequestDto>(
            """
            {
              "protocolVersion": "2025-11-25"
            }
            """);

        dto = dto ?? throw new InvalidOperationException("Expected initialize request DTO.");

        Assert.Equal("2025-11-25", dto.ProtocolVersion);
        Assert.Same(ClientCapabilitiesDto.None, dto.Capabilities);
        Assert.Same(ClientInfoDto.Unknown, dto.ClientInfo);
    }

    [Fact]
    public void ServerCapabilitiesDto_Should_Not_Expose_Mutable_Experimental_Dictionary()
    {
        var source = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["alpha"] = "one"
        };

        var dto = new ServerCapabilitiesDto(
            Tools: ToolsCapabilityDto.StaticList,
            Resources: ResourcesCapabilityDto.StaticListNoSubscribe,
            Prompts: PromptsCapabilityDto.StaticList,
            Experimental: source);

        source["alpha"] = "two";
        source["beta"] = "three";

        var experimental = dto.Experimental ?? throw new InvalidOperationException("Expected experimental capabilities.");

        Assert.Equal("one", experimental["alpha"]);
        Assert.False(experimental.ContainsKey("beta"));
    }

    [Fact]
    public void CapabilityProvider_Should_ReUse_Immutable_Server_Capability_Instance()
    {
        var provider = new CapabilityProvider();

        var first = provider.GetCapabilities();
        var second = provider.GetCapabilities();

        Assert.Same(first, second);
        Assert.Same(ToolsCapabilityDto.StaticList, first.Tools);
        Assert.Same(ResourcesCapabilityDto.StaticListNoSubscribe, first.Resources);
        Assert.Same(PromptsCapabilityDto.StaticList, first.Prompts);
    }
}
