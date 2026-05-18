using LanguageExt;
using McpServer.Infrastructure.Execution;
using McpServer.Application.Abstractions.Files;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace McpServer.UnitTests.Infrastructure;

public sealed class ProcessExecutionServiceTests
{
    [Fact]
    public async Task RunAsync_Should_Default_Working_Directory_To_Project_Root()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "mcpserver-process-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var pathPolicy = Substitute.For<IPathPolicy>();
            Fin<string> projectRoot = tempRoot;
            pathPolicy.NormalizeAndValidateWritePath("project").Returns(projectRoot);
            var logger = Substitute.For<ILogger<ProcessExecutionService>>();
            var sut = new ProcessExecutionService(pathPolicy, logger);

            var result = await sut.RunAsync(
                new McpServer.Application.Execution.Commands.RunProcessCommand(
                    OperatingSystem.IsWindows() ? "cmd" : "/bin/sh",
                    OperatingSystem.IsWindows()
                        ? ["/c", "echo", "hello"]
                        : ["-lc", "printf hello"],
                    null,
                    TimeoutSeconds: 30,
                    MaxOutputChars: 12000),
                CancellationToken.None);

            Assert.True(result.IsSucc);
            pathPolicy.Received(1).NormalizeAndValidateWritePath("project");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
