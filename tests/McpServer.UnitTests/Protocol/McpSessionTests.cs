using McpServer.Protocol.Lifecycle;
using McpServer.Protocol.Roots;
using McpServer.Protocol.Session;
using Xunit;

namespace McpServer.UnitTests.Protocol;

public sealed class McpSessionTests
{
    [Fact]
    public void UpdateClientRoots_Should_Fail_Before_Initialize()
    {
        var session = new McpSession();

        var result = session.UpdateClientRoots([new RootDto("file:///repo", "repo")]);

        Assert.True(result.IsFail);
        var error = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected roots update to fail before initialization."),
            Fail: failure => failure.Message);
        Assert.Contains("Initialize must complete first", error, StringComparison.Ordinal);
    }

    [Fact]
    public void CompleteInitialize_Should_Set_State_Before_Reporting_Initialized()
    {
        var session = new McpSession();

        var result = session.CompleteInitialize(
            "2025-11-25",
            new ClientCapabilitiesDto(Roots: new RootsClientCapabilityDto(ListChanged: true)));

        Assert.True(result.IsSucc);
        Assert.True(session.IsInitialized);
        Assert.Equal("2025-11-25", session.ProtocolVersion);
        Assert.NotNull(session.ClientCapabilities);
        Assert.True(session.SupportsRoots);
    }

    [Fact]
    public void UpdateClientRoots_Should_Defensively_Copy_Roots()
    {
        var session = new McpSession();
        var initialize = session.CompleteInitialize("2025-11-25", ClientCapabilitiesDto.None);
        Assert.True(initialize.IsSucc);

        var roots = new List<RootDto>
        {
            new("file:///repo-a", "repo-a")
        };

        var result = session.UpdateClientRoots(roots);
        roots.Add(new RootDto("file:///repo-b", "repo-b"));

        Assert.True(result.IsSucc);
        Assert.Single(session.ClientRoots);
        Assert.Equal("file:///repo-a", session.ClientRoots[0].Uri);
    }
}
