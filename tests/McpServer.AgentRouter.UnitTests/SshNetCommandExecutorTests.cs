using LanguageExt;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Domain.Ssh;
using McpServer.AgentRouter.Infrastructure.Ssh;
using McpServer.Infrastructure.Ssh;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace McpServer.AgentRouter.UnitTests;

public sealed class SshNetCommandExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_Should_Fail_When_Username_Is_Missing_Before_Connection()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcpserver-ssh-executor-{Guid.NewGuid():N}");
        var vaultPath = Path.Combine(root, "ssh-vault.json");
        var vaultKeyPath = Path.Combine(root, "ssh-vault.key");
        var store = new SshCredentialVaultStore(vaultPath, vaultKeyPath, root);
        store.UpsertEntry("dev", "super-secret-password");

        var executor = new SshNetCommandExecutor(
            Substitute.For<IAgentRouterRuntimePathResolver>(),
            store,
            Substitute.For<ILogger<SshNetCommandExecutor>>());

        var result = await executor.ExecuteAsync(new SshExecutionCommand
        {
            ProfileName = "dev",
            Host = "127.0.0.1",
            Port = 22,
            Username = string.Empty,
            Command = "whoami",
            PasswordVaultItemName = "dev",
            AcceptUnknownHostKey = true
        }, CancellationToken.None);

        Assert.True(result.IsFail);
        var error = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected SSH execution to fail for a missing username."),
            Fail: failure => failure.Message);
        Assert.Contains("missing Username", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Fail_When_Password_Vault_Item_Resolves_To_Empty_Secret()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcpserver-ssh-executor-empty-{Guid.NewGuid():N}");
        var vaultPath = Path.Combine(root, "ssh-vault.json");
        var vaultKeyPath = Path.Combine(root, "ssh-vault.key");
        var store = new SshCredentialVaultStore(vaultPath, vaultKeyPath, root);
        store.UpsertEntry("empty", string.Empty);

        var executor = new SshNetCommandExecutor(
            Substitute.For<IAgentRouterRuntimePathResolver>(),
            store,
            Substitute.For<ILogger<SshNetCommandExecutor>>());

        var result = await executor.ExecuteAsync(new SshExecutionCommand
        {
            ProfileName = "dev",
            Host = "127.0.0.1",
            Port = 22,
            Username = "tester",
            Command = "whoami",
            PasswordVaultItemName = "empty",
            AcceptUnknownHostKey = true
        }, CancellationToken.None);

        Assert.True(result.IsFail);
        var error = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected SSH execution to fail for an empty vault secret."),
            Fail: failure => failure.Message);
        Assert.Contains("resolved to an empty secret", error, StringComparison.Ordinal);
    }
}
