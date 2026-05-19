using LanguageExt;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.Ssh;
using McpServer.AgentRouter.Domain.Ssh;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace McpServer.AgentRouter.UnitTests;

public sealed class SshExecutionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_ExecutesAllowedProfileCommand_AndWritesTrace()
    {
        var policy = Substitute.For<ISshExecutionPolicy>();
        policy.EvaluateAsync(Arg.Any<SshExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<SshExecutionPolicyDecision>>(Fin<SshExecutionPolicyDecision>.Succ(new SshExecutionPolicyDecision
            {
                Allowed = true,
                Decision = "allowed",
                ProfileName = "dev",
                Host = "127.0.0.1",
                Port = 22,
                Username = "tester",
                ResolvedCommand = "pwd",
                WorkingDirectory = "/tmp",
                TimeoutSeconds = 60,
                MaxOutputChars = 200000,
                PasswordVaultItemName = "dev",
                AcceptUnknownHostKey = true
            })));

        var executor = Substitute.For<ISshCommandExecutor>();
        executor.ExecuteAsync(Arg.Any<SshExecutionCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<SshCommandExecutionResult>>(Fin<SshCommandExecutionResult>.Succ(new SshCommandExecutionResult
            {
                ExitCode = 0,
                Stdout = "/tmp\n",
                ElapsedMilliseconds = 12
            })));

        var traceWriter = new RecordingSshExecutionTraceWriter();
        var service = new SshExecutionService(
            policy,
            executor,
            traceWriter,
            NullLogger<SshExecutionService>.Instance);

        var result = await service.ExecuteAsync(new SshExecutionRequest
        {
            Profile = "dev",
            Command = "pwd"
        }, CancellationToken.None);

        Assert.True(result.IsSucc);
        var response = result.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Expected successful SSH execution."));

        Assert.True(response.Allowed);
        Assert.Equal(SshExecutionStatusNames.Completed, response.Status);
        Assert.Equal(0, response.ExitCode);
        Assert.StartsWith("ssh-exec-", response.TraceId, StringComparison.Ordinal);
        Assert.Single(traceWriter.Records);
        Assert.Equal(response.TraceId, traceWriter.Records[0].TraceId);
        await executor.Received(1).ExecuteAsync(Arg.Any<SshExecutionCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotExecute_WhenPolicyDenies()
    {
        var policy = Substitute.For<ISshExecutionPolicy>();
        policy.EvaluateAsync(Arg.Any<SshExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<SshExecutionPolicyDecision>>(Fin<SshExecutionPolicyDecision>.Succ(new SshExecutionPolicyDecision
            {
                Allowed = false,
                Decision = "denied",
                Reason = "unknown profile"
            })));

        var executor = Substitute.For<ISshCommandExecutor>();
        var traceWriter = new RecordingSshExecutionTraceWriter();
        var service = new SshExecutionService(
            policy,
            executor,
            traceWriter,
            NullLogger<SshExecutionService>.Instance);

        var result = await service.ExecuteAsync(new SshExecutionRequest
        {
            Profile = "prod",
            Command = "pwd"
        }, CancellationToken.None);

        Assert.True(result.IsSucc);
        var response = result.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Expected denied SSH execution response."));

        Assert.False(response.Allowed);
        Assert.Equal(SshExecutionStatusNames.Denied, response.Status);
        Assert.Equal("unknown profile", response.PolicyReason);
        Assert.Single(traceWriter.Records);
        await executor.DidNotReceive().ExecuteAsync(Arg.Any<SshExecutionCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_FailsValidation_WhenRequestIsMissing()
    {
        var service = new SshExecutionService(
            Substitute.For<ISshExecutionPolicy>(),
            Substitute.For<ISshCommandExecutor>(),
            new RecordingSshExecutionTraceWriter(),
            NullLogger<SshExecutionService>.Instance);

        var result = await service.ExecuteAsync(null, CancellationToken.None);

        Assert.True(result.IsFail);
    }

    [Fact]
    public async Task ExecuteAsync_WritesTrace_WhenExecutorFails()
    {
        var policy = Substitute.For<ISshExecutionPolicy>();
        policy.EvaluateAsync(Arg.Any<SshExecutionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<SshExecutionPolicyDecision>>(Fin<SshExecutionPolicyDecision>.Succ(new SshExecutionPolicyDecision
            {
                Allowed = true,
                Decision = "allowed",
                ProfileName = "dev",
                Host = "127.0.0.1",
                Port = 22,
                Username = "tester",
                ResolvedCommand = "pwd",
                WorkingDirectory = "/tmp",
                TimeoutSeconds = 60,
                MaxOutputChars = 200000,
                PasswordVaultItemName = "dev",
                AcceptUnknownHostKey = true
            })));

        var executor = Substitute.For<ISshCommandExecutor>();
        executor.ExecuteAsync(Arg.Any<SshExecutionCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<SshCommandExecutionResult>>(LanguageExt.Common.Error.New("SSH connection failed for profile 'dev': actively refused")));

        var traceWriter = new RecordingSshExecutionTraceWriter();
        var service = new SshExecutionService(
            policy,
            executor,
            traceWriter,
            NullLogger<SshExecutionService>.Instance);

        var result = await service.ExecuteAsync(new SshExecutionRequest
        {
            Profile = "dev",
            Command = "pwd"
        }, CancellationToken.None);

        Assert.True(result.IsFail);
        Assert.Single(traceWriter.Records);
        Assert.Equal(SshExecutionStatusNames.Failed, traceWriter.Records[0].Status);
        Assert.Equal("SSH connection failed for profile 'dev': actively refused", traceWriter.Records[0].Summary);
        Assert.Equal("SSH connection failed for profile 'dev': actively refused", traceWriter.Records[0].Stderr);
    }

    private sealed class RecordingSshExecutionTraceWriter : ISshExecutionTraceWriter
    {
        private readonly object _syncRoot = new();
        private readonly List<SshExecutionTraceRecord> _records = [];

        public IReadOnlyList<SshExecutionTraceRecord> Records
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
            SshExecutionTraceRecord traceRecord,
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
