using McpServer.Infrastructure.Ssh;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace McpServer.UnitTests.Infrastructure;

public sealed class SshServiceTests
{
    [Fact]
    public async Task ExecuteAsync_Should_Fail_For_Unknown_Profile_Before_Attempting_Connection()
    {
        var logger = Substitute.For<ILogger<SshService>>();
        var sut = new SshService(Array.Empty<ConfiguredSshProfile>(), AppContext.BaseDirectory, logger);

        var result = await sut.ExecuteAsync(new("missing", "hostname", null, null, 30, 4096), CancellationToken.None);

        Assert.True(result.IsFail);
        var error = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected SSH execution to fail for an unknown profile."),
            Fail: failure => failure.Message);
        Assert.Contains("Unknown SSH profile", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Reject_Unsafe_Raw_Command_Text_Before_Attempting_Connection()
    {
        var logger = Substitute.For<ILogger<SshService>>();
        var sut = new SshService(
            [
                new ConfiguredSshProfile(
                    "lab",
                    "127.0.0.1",
                    22,
                    "tester",
                    PasswordEnvironmentVariable: null,
                    PrivateKeyPath: null,
                    PrivateKeyPassphraseEnvironmentVariable: null,
                    WorkingDirectory: null,
                    HostKeySha256: "SHA256:dGVzdA",
                    AcceptUnknownHostKey: false,
                    AllowedCommands: ["hostname"],
                    DeniedCommands: ["sh", "bash", "pwsh", "powershell"],
                    AllowedRemotePathPrefixes: [])
            ],
            AppContext.BaseDirectory,
            logger);

        var result = await sut.ExecuteAsync(new("lab", "hostname && whoami"), CancellationToken.None);

        Assert.True(result.IsFail);
        var error = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected SSH command validation to fail."),
            Fail: failure => failure.Message);
        Assert.Contains("shell control characters", error, StringComparison.Ordinal);
    }
    [Fact]
    public async Task ExecuteAsync_Should_Require_Command_Allowlist_Before_Attempting_Connection()
    {
        var logger = Substitute.For<ILogger<SshService>>();
        var sut = new SshService(
            [
                new ConfiguredSshProfile(
                    "lab",
                    "127.0.0.1",
                    22,
                    "tester",
                    PasswordEnvironmentVariable: null,
                    PrivateKeyPath: null,
                    PrivateKeyPassphraseEnvironmentVariable: null,
                    WorkingDirectory: null,
                    HostKeySha256: "SHA256:dGVzdA",
                    AcceptUnknownHostKey: false,
                    AllowedCommands: [],
                    DeniedCommands: [],
                    AllowedRemotePathPrefixes: [])
            ],
            AppContext.BaseDirectory,
            logger);

        var result = await sut.ExecuteAsync(new("lab", "hostname"), CancellationToken.None);

        Assert.True(result.IsFail);
        var error = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected SSH command validation to fail."),
            Fail: failure => failure.Message);
        Assert.Contains("requires an explicit command allowlist", error, StringComparison.Ordinal);
    }

}
