using McpServer.Infrastructure.Ssh;
using Xunit;

namespace McpServer.UnitTests.Infrastructure;

public sealed class FileSystemSshProfileStoreTests
{
    [Fact]
    public void Save_And_Load_Profiles_Preserves_Vault_Item_References()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcpserver-ssh-profiles-{Guid.NewGuid():N}");
        var repoFile = "ssh-profiles.local.json";

        var profiles = new[]
        {
            new ConfiguredSshProfile(
                "dev",
                "127.0.0.1",
                22,
                "tester",
                PrivateKeyPath: null,
                PasswordVaultItemName: "dev",
                PrivateKeyPassphraseVaultItemName: "dev-passphrase",
                WorkingDirectory: "/tmp",
                HostKeySha256: "SHA256:dGVzdA",
                AcceptUnknownHostKey: false,
                AllowedCommands: ["pwd"],
                DeniedCommands: [],
                AllowedRemotePathPrefixes: ["/tmp"],
                AllowSudoCommand: false,
                AllowAllCommands: true)
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
        Assert.Equal("dev", loadedProfile.PasswordVaultItemName);
        Assert.Equal("dev-passphrase", loadedProfile.PrivateKeyPassphraseVaultItemName);
        Assert.True(loadedProfile.AllowAllCommands);
    }
}
