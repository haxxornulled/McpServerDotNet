using McpServer.Infrastructure.Ssh;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace McpServer.UnitTests.Infrastructure;

public sealed class SshProfileManagerTests
{
    [Fact]
    public void Upsert_And_List_Should_Persist_Profile_Credential_Link()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcpserver-ssh-profiles-{Guid.NewGuid():N}");
        var logger = Substitute.For<ILogger<SshProfileManager>>();
        var sut = new SshProfileManager(logger);

        var profile = new ConfiguredSshProfile(
            "root",
            "173.255.205.169",
            22,
            "root",
            PrivateKeyPath: null,
            PasswordVaultItemName: "root",
            PrivateKeyPassphraseVaultItemName: null,
            WorkingDirectory: "/root",
            HostKeySha256: "SHA256:Q7mMEDNG2w/v+PBa0ogNmW3ECGDGapU2NFgKRX5/5yI",
            AcceptUnknownHostKey: false,
            AllowedCommands: ["pwd", "whoami"],
            DeniedCommands: [],
            AllowedRemotePathPrefixes: ["/root", "/tmp"],
            AllowSudoCommand: true,
            AllowAllCommands: true);

        var saved = sut.UpsertProfile(root, profile, "ssh-profiles.local.json");

        Assert.Equal("root", saved.Name);
        Assert.Equal("root", saved.PasswordVaultItemName);
        Assert.Equal("root", saved.Username);

        var listed = sut.ListProfiles(root, "ssh-profiles.local.json");
        Assert.Single(listed);
        var loaded = listed[0];
        Assert.Equal("root", loaded.Name);
        Assert.Equal("root", loaded.PasswordVaultItemName);
        Assert.Equal("root", loaded.Username);
        Assert.True(loaded.AllowSudoCommand);
        Assert.True(loaded.AllowAllCommands);
    }

    [Fact]
    public void Delete_Should_Remove_Profile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcpserver-ssh-profiles-{Guid.NewGuid():N}");
        var logger = Substitute.For<ILogger<SshProfileManager>>();
        var sut = new SshProfileManager(logger);

        sut.UpsertProfile(root, new ConfiguredSshProfile(
            "dev",
            "127.0.0.1",
            22,
            "tester",
            PrivateKeyPath: null,
            PasswordVaultItemName: "dev",
            PrivateKeyPassphraseVaultItemName: null,
            WorkingDirectory: "/tmp",
            HostKeySha256: null,
            AcceptUnknownHostKey: false,
            AllowedCommands: [],
            DeniedCommands: [],
            AllowedRemotePathPrefixes: ["/tmp"],
            AllowSudoCommand: false,
            AllowAllCommands: false), "ssh-profiles.local.json");

        var deleted = sut.DeleteProfile(root, "dev", "ssh-profiles.local.json");

        Assert.True(deleted);
        Assert.Empty(sut.ListProfiles(root, "ssh-profiles.local.json"));
    }

    [Fact]
    public void Link_And_Unlink_Should_Update_Credential_References()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcpserver-ssh-profiles-{Guid.NewGuid():N}");
        var logger = Substitute.For<ILogger<SshProfileManager>>();
        var sut = new SshProfileManager(logger);

        sut.UpsertProfile(root, new ConfiguredSshProfile(
            "dev",
            "127.0.0.1",
            22,
            "tester",
            PrivateKeyPath: null,
            PasswordVaultItemName: null,
            PrivateKeyPassphraseVaultItemName: null,
            WorkingDirectory: "/tmp",
            HostKeySha256: null,
            AcceptUnknownHostKey: false,
            AllowedCommands: [],
            DeniedCommands: [],
            AllowedRemotePathPrefixes: ["/tmp"],
            AllowSudoCommand: false,
            AllowAllCommands: false), "ssh-profiles.local.json");

        var linked = sut.LinkCredential(root, "dev", "ssh-profiles.local.json", passwordVaultItemName: "dev-password");
        Assert.Equal("dev-password", linked.PasswordVaultItemName);

        var unlinked = sut.UnlinkCredential(root, "dev", "ssh-profiles.local.json");
        Assert.Null(unlinked.PasswordVaultItemName);
        Assert.Null(unlinked.PrivateKeyPassphraseVaultItemName);
    }

    [Fact]
    public async Task Concurrent_Upserts_Should_Persist_All_Profiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcpserver-ssh-profiles-{Guid.NewGuid():N}");
        var logger = Substitute.For<ILogger<SshProfileManager>>();
        var sut = new SshProfileManager(logger);

        var tasks = new List<Task>();
        for (var i = 0; i < 12; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                sut.UpsertProfile(root, new ConfiguredSshProfile(
                    $"dev-{index}",
                    "127.0.0.1",
                    22,
                    $"user-{index}",
                    PrivateKeyPath: null,
                    PasswordVaultItemName: $"vault-{index}",
                    PrivateKeyPassphraseVaultItemName: null,
                    WorkingDirectory: "/tmp",
                    HostKeySha256: null,
                    AcceptUnknownHostKey: false,
                    AllowedCommands: [],
                    DeniedCommands: [],
                    AllowedRemotePathPrefixes: ["/tmp"],
                    AllowSudoCommand: false,
                    AllowAllCommands: false), "ssh-profiles.local.json");
            }));
        }

        await Task.WhenAll(tasks);

        var listed = sut.ListProfiles(root, "ssh-profiles.local.json");
        Assert.Equal(12, listed.Count);
        for (var i = 0; i < 12; i++)
        {
            Assert.Contains(listed, profile => profile.Name == $"dev-{i}");
        }
    }
}
