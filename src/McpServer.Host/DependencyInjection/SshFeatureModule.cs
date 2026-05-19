using Autofac;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Abstractions.Ssh;
using McpServer.Application.Mcp.Tools;
using McpServer.Host.Configuration;
using McpServer.Infrastructure.Ssh;
using Microsoft.Extensions.Logging;

namespace McpServer.Host.DependencyInjection;

public sealed class SshFeatureModule(SshOptions options, string baseDirectory) : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterInstance(new SshCredentialVaultStore(
                options.VaultPath,
                options.VaultKeyPath,
                baseDirectory))
            .AsSelf()
            .SingleInstance();

        var configuredProfiles = FileSystemSshProfileStore.LoadProfiles(
            baseDirectory,
            options.LoadRepoProfilesFile,
            options.RepoProfilesFilePath,
            options.LoadUserProfilesFile,
            options.UserProfilesFilePath,
            options.AllowInlineProfiles,
            CreateConfiguredProfiles(options.Profiles));

        if (configuredProfiles.Count == 0)
        {
            return;
        }

        if (options.UseTestBackend)
        {
            builder.Register(ctx => new TestSshService(
                    configuredProfiles,
                    string.IsNullOrWhiteSpace(options.TestBackendRootPath)
                        ? Path.Combine(Path.GetTempPath(), "mcpserver-ssh-test-backend")
                        : options.TestBackendRootPath,
                    ctx.Resolve<ILogger<TestSshService>>()))
                .As<ISshService>()
                .SingleInstance();
        }
        else
        {
            builder.Register(ctx => new SshService(
                    configuredProfiles,
                    baseDirectory,
                    ctx.Resolve<ILogger<SshService>>(),
                    ctx.Resolve<SshCredentialVaultStore>()))
                .As<ISshService>()
                .SingleInstance();
        }

        RegisterTool<SshExecuteToolHandler>(builder);
        RegisterTool<SshWriteTextToolHandler>(builder);
    }

    private static void RegisterTool<TToolHandler>(ContainerBuilder builder)
        where TToolHandler : class, IToolHandler
    {
        builder.RegisterType<TToolHandler>()
            .AsSelf()
            .As<IToolHandler>()
            .SingleInstance();
    }

    private static ConfiguredSshProfile[] CreateConfiguredProfiles(IEnumerable<SshProfileOptions> profiles) =>
        profiles
            .Where(static profile => !string.IsNullOrWhiteSpace(profile.Name))
            .Select(static profile => new ConfiguredSshProfile(
                profile.Name,
                profile.Host,
                profile.Port,
                profile.Username,
                profile.PrivateKeyPath,
                profile.PasswordVaultItemName,
                profile.PrivateKeyPassphraseVaultItemName,
                profile.WorkingDirectory,
                profile.HostKeySha256,
                profile.AcceptUnknownHostKey,
                profile.AllowedCommands,
                profile.DeniedCommands,
                profile.AllowedRemotePathPrefixes,
                profile.AllowSudoCommand,
                profile.AllowAllCommands))
            .ToArray();
}
