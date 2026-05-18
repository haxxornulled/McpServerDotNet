using McpServer.Host.Transport.Stdio;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace McpServer.UnitTests.Host;

public sealed class StdioServerLifecycleServiceTests
{
    [Fact]
    public void StdioServerLifecycleService_Should_Use_Explicit_Hosted_Lifecycle_Service_Contract()
    {
        Assert.True(typeof(IHostedLifecycleService).IsAssignableFrom(typeof(StdioServerLifecycleService)));
        Assert.False(typeof(BackgroundService).IsAssignableFrom(typeof(StdioServerLifecycleService)));
    }
}
