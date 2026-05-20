using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using McpServer.AgentRouter.Tools;
using Xunit;

namespace McpServer.AgentRouter.UnitTests;

public sealed class RepositoryVerificationRunnerTests
{
    [Fact]
    public async Task RunAsync_Should_Stop_When_Cancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var runner = new RepositoryVerificationRunner(
            new RepositoryVerificationSettings
            {
                RepositoryRootPath = Path.GetTempPath()
            },
            new BlockingProcessRunner());

        var runTask = runner.RunAsync(cancellation.Token);

        await Task.Yield();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await runTask);
    }

    [Fact]
    public async Task Help_Command_Should_Return_Zero()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var originalOut = Console.Out;
        var originalErr = Console.Error;

        try
        {
            Console.SetOut(output);
            Console.SetError(error);

            var exitCode = await AgentRouterToolProgram.RunAsync(["help"], CancellationToken.None);

            Assert.Equal(0, exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }

    [Fact]
    public async Task Unknown_Command_Should_Return_Two()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var originalOut = Console.Out;
        var originalErr = Console.Error;

        try
        {
            Console.SetOut(output);
            Console.SetError(error);

            var exitCode = await AgentRouterToolProgram.RunAsync(["not-a-command"], CancellationToken.None);

            Assert.Equal(2, exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }

    [Fact]
    public void Chat_Command_In_Json_Mode_Should_Write_Notice_To_Stderr()
    {
        using var error = new StringWriter();
        CliOutput.Configure("json");

        try
        {
            AgentRouterToolProgram.WarnIfChatJsonOutput("chat", error);

            Assert.Contains("Chat is in JSON output mode", error.ToString());
        }
        finally
        {
            CliOutput.Configure("text");
        }
    }

    private sealed class BlockingProcessRunner : IProcessRunner
    {
        public async Task<int> RunAsync(string fileName, IReadOnlyCollection<string> arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(fileName);
            ArgumentNullException.ThrowIfNull(arguments);
            ArgumentNullException.ThrowIfNull(workingDirectory);

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
