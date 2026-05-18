using LanguageExt;
using LanguageExt.Common;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.Shell;
using McpServer.AgentRouter.Domain.Shell;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace McpServer.AgentRouter.UnitTests;

public sealed class ShellExecutionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_ExecutesAllowedCommand_AndWritesTrace()
    {
        var policy = Substitute.For<IShellExecutionPolicy>();
        policy.EvaluateAsync(Arg.Any<ShellExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<ShellExecutionPolicyDecision>>(Fin<ShellExecutionPolicyDecision>.Succ(new ShellExecutionPolicyDecision
            {
                Allowed = true,
                Decision = "allowed",
                ResolvedCommand = "dotnet",
                ResolvedArguments = ["--info"],
                WorkingDirectory = Directory.GetCurrentDirectory(),
                TimeoutSeconds = 60,
                MaxOutputChars = 200000
            })));

        var executor = Substitute.For<IShellCommandExecutor>();
        executor.ExecuteAsync(Arg.Any<ShellExecutionCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<ShellCommandExecutionResult>>(Fin<ShellCommandExecutionResult>.Succ(new ShellCommandExecutionResult
            {
                ExitCode = 0,
                Stdout = ".NET SDK info",
                ElapsedMilliseconds = 12
            })));

        var traceWriter = new RecordingShellExecutionTraceWriter();
        var service = new ShellExecutionService(
            policy,
            executor,
            traceWriter,
            NullLogger<ShellExecutionService>.Instance);

        var result = await service.ExecuteAsync(new ShellExecutionRequest
        {
            Command = "dotnet",
            Arguments = ["--info"],
            WorkingDirectory = "."
        }, CancellationToken.None);

        Assert.True(result.IsSucc);
        var response = result.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Expected successful shell execution."));

        Assert.True(response.Allowed);
        Assert.Equal(ShellExecutionStatusNames.Completed, response.Status);
        Assert.Equal(0, response.ExitCode);
        Assert.StartsWith("shell-exec-", response.TraceId, StringComparison.Ordinal);
        Assert.Single(traceWriter.Records);
        Assert.Equal(response.TraceId, traceWriter.Records[0].TraceId);
        await executor.Received(1).ExecuteAsync(Arg.Any<ShellExecutionCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotExecute_WhenPolicyDenies()
    {
        var policy = Substitute.For<IShellExecutionPolicy>();
        policy.EvaluateAsync(Arg.Any<ShellExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<ShellExecutionPolicyDecision>>(Fin<ShellExecutionPolicyDecision>.Succ(new ShellExecutionPolicyDecision
            {
                Allowed = false,
                Decision = "denied",
                Reason = "command not allowed"
            })));

        var executor = Substitute.For<IShellCommandExecutor>();
        var traceWriter = new RecordingShellExecutionTraceWriter();
        var service = new ShellExecutionService(
            policy,
            executor,
            traceWriter,
            NullLogger<ShellExecutionService>.Instance);

        var result = await service.ExecuteAsync(new ShellExecutionRequest
        {
            Command = "cmd",
            Arguments = ["/c", "echo no"]
        }, CancellationToken.None);

        Assert.True(result.IsSucc);
        var response = result.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Expected denied shell execution response."));

        Assert.False(response.Allowed);
        Assert.Equal(ShellExecutionStatusNames.Denied, response.Status);
        Assert.Equal("command not allowed", response.PolicyReason);
        Assert.Single(traceWriter.Records);
        await executor.DidNotReceive().ExecuteAsync(Arg.Any<ShellExecutionCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_FailsValidation_WhenRequestIsMissing()
    {
        var service = new ShellExecutionService(
            Substitute.For<IShellExecutionPolicy>(),
            Substitute.For<IShellCommandExecutor>(),
            new RecordingShellExecutionTraceWriter(),
            NullLogger<ShellExecutionService>.Instance);

        var result = await service.ExecuteAsync(null, CancellationToken.None);

        Assert.True(result.IsFail);
    }

    [Fact]
    public async Task ExecuteAsync_WritesTrace_WhenExecutorFails()
    {
        var policy = Substitute.For<IShellExecutionPolicy>();
        policy.EvaluateAsync(Arg.Any<ShellExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<ShellExecutionPolicyDecision>>(Fin<ShellExecutionPolicyDecision>.Succ(new ShellExecutionPolicyDecision
            {
                Allowed = true,
                Decision = "allowed",
                ResolvedCommand = "dotnet",
                ResolvedArguments = ["--info"],
                WorkingDirectory = Directory.GetCurrentDirectory(),
                TimeoutSeconds = 60,
                MaxOutputChars = 200000
            })));

        var executor = Substitute.For<IShellCommandExecutor>();
        executor.ExecuteAsync(Arg.Any<ShellExecutionCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<ShellCommandExecutionResult>>(Error.New("dotnet executable failed to launch")));

        var traceWriter = new RecordingShellExecutionTraceWriter();
        var service = new ShellExecutionService(
            policy,
            executor,
            traceWriter,
            NullLogger<ShellExecutionService>.Instance);

        var result = await service.ExecuteAsync(new ShellExecutionRequest
        {
            Command = "dotnet",
            Arguments = ["--info"],
            WorkingDirectory = "."
        }, CancellationToken.None);

        Assert.True(result.IsFail);
        Assert.Single(traceWriter.Records);
        Assert.Equal(ShellExecutionStatusNames.Failed, traceWriter.Records[0].Status);
        Assert.Equal("dotnet executable failed to launch", traceWriter.Records[0].Summary);
        Assert.Equal("dotnet executable failed to launch", traceWriter.Records[0].Stderr);
    }

    private sealed class RecordingShellExecutionTraceWriter : IShellExecutionTraceWriter
    {
        private readonly object _syncRoot = new();
        private readonly List<ShellExecutionTraceRecord> _records = [];

        public IReadOnlyList<ShellExecutionTraceRecord> Records
        {
            get
            {
                lock (_syncRoot)
                {
                    return _records.ToArray();
                }
            }
        }

        public ValueTask<Fin<Unit>> WriteAsync(
            ShellExecutionTraceRecord traceRecord,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_syncRoot)
            {
                _records.Add(traceRecord);
            }

            return new ValueTask<Fin<Unit>>(Prelude.unit);
        }
    }
}
