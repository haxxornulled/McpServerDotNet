using McpServer.Infrastructure.Ssh;
using Xunit;

namespace McpServer.UnitTests.Infrastructure;

public sealed class SshCredentialVaultTests
{
    [Fact]
    public void Protect_And_Unprotect_RoundTrips_The_Secret()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcpserver-ssh-vault-{Guid.NewGuid():N}");
        var vaultPath = Path.Combine(root, "vault.key");
        var vault = new SshCredentialVault(vaultPath);

        var protectedSecret = vault.Protect("super-secret-password");
        var recovered = vault.Unprotect(protectedSecret);

        Assert.Equal("super-secret-password", recovered);
        Assert.True(File.Exists(vaultPath));
    }
}
