using McpServer.Infrastructure.Ssh;
using Xunit;

namespace McpServer.UnitTests.Infrastructure;

public sealed class FileSystemSshProfileStoreTests
{
    [Fact]
    public void Save_And_Load_Profiles_Preserves_Encrypted_Credentials()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcpserver-ssh-profiles-{Guid.NewGuid():N}");
        var repoFile = "ssh-profiles.local.json";
        var vaultPath = Path.Combine(root, "vault.key");
        var vault = new SshCredentialVault(vaultPath);
        var secret = vault.Protect("super-secret-password");

        var profiles = new[]
        {
            new ConfiguredSshProfile(
                "dev",
                "127.0.0.1",
                22,
                "tester",
                PasswordEnvironmentVariable: null,
                PrivateKeyPath: null,
                PrivateKeyPassphraseEnvironmentVariable: null,
                WorkingDirectory: "/tmp",
                HostKeySha256: "SHA256:dGVzdA",
                AcceptUnknownHostKey: false,
                AllowedCommands: ["pwd"],
                DeniedCommands: [],
                AllowedRemotePathPrefixes: ["/tmp"],
                AllowSudoCommand: false)
            {
                PasswordSecret = secret,
                PasswordVaultItemName = "dev"
            }
        };

        FileSystemSshProfileStore.SaveProfiles(root, repoFile, profiles);

        var loaded = FileSystemSshProfileStore.LoadProfiles(
            root,
            loadRepoProfilesFile: true,
            repoProfilesFilePath: repoFile,
            loadUserProfilesFile: false,
            userProfilesFilePath: null,
            allowInlineProfiles: false,
            inlineProfiles: null);

        Assert.Single(loaded);
        var loadedProfile = loaded[0];
        Assert.NotNull(loadedProfile.PasswordSecret);
        Assert.Equal("dev", loadedProfile.PasswordVaultItemName);
        Assert.Equal("super-secret-password", vault.Unprotect(loadedProfile.PasswordSecret!));
    }
}
