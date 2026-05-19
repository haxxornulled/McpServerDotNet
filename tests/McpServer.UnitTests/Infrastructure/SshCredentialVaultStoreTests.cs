using McpServer.Infrastructure.Ssh;
using McpServer.SshVaultCli;
using Xunit;

namespace McpServer.UnitTests.Infrastructure;

public sealed class SshCredentialVaultStoreTests
{
    [Fact]
    public async Task Concurrent_Add_And_Delete_Operations_Are_Atomic_And_Valid()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcpserver-ssh-vault-store-{Guid.NewGuid():N}");
        var vaultPath = Path.Combine(root, "ssh-vault.json");
        var vaultKeyPath = Path.Combine(root, "ssh-vault.key");
        var store = new SshCredentialVaultStore(vaultPath, vaultKeyPath, root);

        await Task.WhenAll(Enumerable.Range(0, 32).Select(index => Task.Run(() =>
            store.UpsertEntry(
                $"item-{index % 4}",
                $"secret-{index}",
                description: $"description-{index}"))));

        var entries = store.ListEntries();
        Assert.Equal(4, entries.Count);
        Assert.All(entries, entry =>
        {
            Assert.StartsWith("item-", entry.Name, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(entry.Secret);
        });

        await Task.WhenAll(Enumerable.Range(0, 4).Select(index => Task.Run(() =>
            store.DeleteEntry($"item-{index}"))));

        Assert.Empty(store.ListEntries());

        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(vaultPath));
        Assert.Equal("1", document.RootElement.GetProperty("version").GetString());
    }

    [Fact]
    public void Cli_Add_List_And_Delete_Mutates_Vault_File()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcpserver-ssh-vault-cli-{Guid.NewGuid():N}");
        var vaultPath = Path.Combine(root, "ssh-vault.json");
        var vaultKeyPath = Path.Combine(root, "ssh-vault.key");

        var addExit = VaultCli.Run([
            "add",
            "dev",
            "--secret",
            "super-secret-password",
            "--vault-path",
            vaultPath,
            "--vault-key-path",
            vaultKeyPath,
            "--base-directory",
            root
        ]);

        Assert.Equal(0, addExit);

        var store = new SshCredentialVaultStore(vaultPath, vaultKeyPath, root);
        Assert.True(store.ContainsEntry("dev"));
        Assert.Equal("super-secret-password", store.ResolveSecret("dev"));

        var listExit = VaultCli.Run([
            "list",
            "--vault-path",
            vaultPath,
            "--vault-key-path",
            vaultKeyPath,
            "--base-directory",
            root
        ]);

        Assert.Equal(0, listExit);

        var deleteExit = VaultCli.Run([
            "delete",
            "dev",
            "--vault-path",
            vaultPath,
            "--vault-key-path",
            vaultKeyPath,
            "--base-directory",
            root
        ]);

        Assert.Equal(0, deleteExit);
        Assert.False(store.ContainsEntry("dev"));
    }

    [Fact]
    public void Cli_Verify_Confirms_Stored_Secret_Without_Echoing_It()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcpserver-ssh-vault-verify-{Guid.NewGuid():N}");
        var vaultPath = Path.Combine(root, "ssh-vault.json");
        var vaultKeyPath = Path.Combine(root, "ssh-vault.key");

        var addExit = VaultCli.Run([
            "add",
            "dev",
            "--secret",
            "super-secret-password",
            "--vault-path",
            vaultPath,
            "--vault-key-path",
            vaultKeyPath,
            "--base-directory",
            root
        ]);

        Assert.Equal(0, addExit);

        var verifyExit = VaultCli.Run([
            "verify",
            "dev",
            "--expected",
            "super-secret-password",
            "--vault-path",
            vaultPath,
            "--vault-key-path",
            vaultKeyPath,
            "--base-directory",
            root
        ]);

        Assert.Equal(0, verifyExit);
    }

    [Fact]
    public void Cli_Verify_Fails_When_Expected_Secret_Does_Not_Match()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcpserver-ssh-vault-verify-fail-{Guid.NewGuid():N}");
        var vaultPath = Path.Combine(root, "ssh-vault.json");
        var vaultKeyPath = Path.Combine(root, "ssh-vault.key");

        var addExit = VaultCli.Run([
            "add",
            "dev",
            "--secret",
            "super-secret-password",
            "--vault-path",
            vaultPath,
            "--vault-key-path",
            vaultKeyPath,
            "--base-directory",
            root
        ]);

        Assert.Equal(0, addExit);

        var verifyExit = VaultCli.Run([
            "verify",
            "dev",
            "--expected",
            "definitely-wrong",
            "--vault-path",
            vaultPath,
            "--vault-key-path",
            vaultKeyPath,
            "--base-directory",
            root
        ]);

        Assert.Equal(1, verifyExit);
    }

    [Fact]
    public void Cli_Add_From_Secret_File_Trims_Trailing_Newlines()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcpserver-ssh-vault-cli-file-{Guid.NewGuid():N}");
        var vaultPath = Path.Combine(root, "ssh-vault.json");
        var vaultKeyPath = Path.Combine(root, "ssh-vault.key");
        var secretFile = Path.Combine(root, "secret.txt");
        Directory.CreateDirectory(root);
        File.WriteAllText(secretFile, "super-secret-password\r\n");

        var addExit = VaultCli.Run([
            "add",
            "dev",
            "--secret-file",
            secretFile,
            "--vault-path",
            vaultPath,
            "--vault-key-path",
            vaultKeyPath,
            "--base-directory",
            root
        ]);

        Assert.Equal(0, addExit);

        var store = new SshCredentialVaultStore(vaultPath, vaultKeyPath, root);
        Assert.Equal("super-secret-password", store.ResolveSecret("dev"));
    }
}
