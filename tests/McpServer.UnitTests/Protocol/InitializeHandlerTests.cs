using McpServer.Protocol.Lifecycle;
using McpServer.Protocol.Session;
using Xunit;

namespace McpServer.UnitTests.Protocol;

public sealed class InitializeHandlerTests
{
    [Fact]
    public void Handle_Should_Return_Typed_InitializeResultDto()
    {
        var provider = new CapabilityProvider();
        var handler = new InitializeHandler(provider);
        var session = new McpSession();

        var request = new InitializeRequestDto(
            ProtocolVersion: "2025-03-26",
            Capabilities: new ClientCapabilitiesDto(),
            ClientInfo: new ClientInfoDto("xunit", "1.0.0"));

        var result = handler.Handle(request, session);

        Assert.True(result.IsSucc);
        var dto = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));
        Assert.Equal("2025-03-26", dto.ProtocolVersion);
        Assert.Equal("McpServer.FileSystem", dto.ServerInfo.Name);
        Assert.False(session.SupportsRoots);
    }

    [Fact]
    public void Handle_Should_Record_Client_Roots_Capability()
    {
        var provider = new CapabilityProvider();
        var handler = new InitializeHandler(provider);
        var session = new McpSession();

        var request = new InitializeRequestDto(
            ProtocolVersion: "2025-03-26",
            Capabilities: new ClientCapabilitiesDto(Roots: new RootsClientCapabilityDto(ListChanged: true)),
            ClientInfo: new ClientInfoDto("xunit", "1.0.0"));

        var result = handler.Handle(request, session);

        Assert.True(result.IsSucc);
        Assert.True(session.SupportsRoots);
    }

    [Fact]
    public void Handle_Should_Fall_Back_To_Compatible_Server_Version_When_Client_Version_Is_Unknown()
    {
        var provider = new CapabilityProvider();
        var handler = new InitializeHandler(provider);
        var session = new McpSession();

        var request = new InitializeRequestDto(
            ProtocolVersion: "2026-01-01",
            Capabilities: new ClientCapabilitiesDto(),
            ClientInfo: new ClientInfoDto("lmstudio", "1.0.0"));

        var result = handler.Handle(request, session);

        Assert.True(result.IsSucc);
        var dto = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        Assert.Equal("2025-03-26", dto.ProtocolVersion);
        Assert.Equal("2025-03-26", session.ProtocolVersion);
    }

    [Fact]
    public void Handle_Should_Keep_Exact_2025_11_25_When_Client_Requests_It()
    {
        var provider = new CapabilityProvider();
        var handler = new InitializeHandler(provider);
        var session = new McpSession();

        var request = new InitializeRequestDto(
            ProtocolVersion: "2025-11-25",
            Capabilities: new ClientCapabilitiesDto(),
            ClientInfo: new ClientInfoDto("exact-client", "1.0.0"));

        var result = handler.Handle(request, session);

        Assert.True(result.IsSucc);
        var dto = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        Assert.Equal("2025-11-25", dto.ProtocolVersion);
        Assert.Equal("2025-11-25", session.ProtocolVersion);
    }
}
