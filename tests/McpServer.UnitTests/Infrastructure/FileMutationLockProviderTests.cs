using McpServer.Infrastructure.Files;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace McpServer.UnitTests.Infrastructure;

public sealed class FileMutationLockProviderTests
{
    [Fact]
    public async Task AcquireAsync_Should_Allow_Reacquire_After_Release()
    {
        var provider = new FileMutationLockProvider(Substitute.For<ILogger<FileMutationLockProvider>>());

        await using (await provider.AcquireAsync("workspace/note.txt", CancellationToken.None))
        {
        }

        await using (await provider.AcquireAsync("workspace/note.txt", CancellationToken.None))
        {
        }
    }
}
