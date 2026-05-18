using McpServer.Infrastructure.Files;
using Xunit;

namespace McpServer.UnitTests.Infrastructure;

public sealed class PathComparisonTests
{
    [Fact]
    public void Comparer_Should_Follow_Current_OS_Semantics()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.True(PathComparison.Comparer.Equals("Alpha.txt", "alpha.txt"));
        }
        else
        {
            Assert.False(PathComparison.Comparer.Equals("Alpha.txt", "alpha.txt"));
        }
    }
}
