using Autofac;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.Runtime;
using McpServer.AgentRouter.Infrastructure.AgentLoops;
using McpServer.AgentRouter.Infrastructure.Mcp;
using McpServer.AgentRouter.Infrastructure.Ollama;
using McpServer.AgentRouter.Infrastructure.Shell;
using McpServer.AgentRouter.Infrastructure.Stores;
using McpServer.AgentRouter.Infrastructure.Ssh;
using McpServer.Infrastructure.Ssh;
using AgentRouterSshProfileStore = McpServer.AgentRouter.Infrastructure.Ssh.FileSystemSshProfileStore;

namespace McpServer.AgentRouter.Infrastructure.DependencyInjection;

public sealed class AgentRouterInfrastructureModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<OllamaChatModelClient>()
            .As<IChatModelClient>()
            .SingleInstance();

        builder.RegisterType<OllamaRuntimeManager>()
            .As<ILocalModelRuntimeManager>()
            .SingleInstance();

        builder.RegisterType<StdioMcpToolCatalogClient>()
            .As<IMcpToolCatalogClient>()
            .SingleInstance();

        builder.RegisterType<StdioMcpToolCallClient>()
            .As<IMcpToolCallClient>()
            .SingleInstance();

        builder.RegisterType<ProcessShellCommandExecutor>()
            .As<IShellCommandExecutor>()
            .SingleInstance();

        builder.RegisterType<FileSystemAgentRunStore>()
            .As<IAgentRunStore>()
            .SingleInstance();

        builder.RegisterType<FileSystemAgentTraceWriter>()
            .As<IAgentTraceWriter>()
            .SingleInstance();

        builder.RegisterType<FileSystemMcpToolCallTraceWriter>()
            .As<IMcpToolCallTraceWriter>()
            .SingleInstance();

        builder.RegisterType<FileSystemShellExecutionTraceWriter>()
            .As<IShellExecutionTraceWriter>()
            .SingleInstance();

        builder.RegisterType<AgentRouterSshProfileStore>()
            .As<ISshProfileStore>()
            .SingleInstance();

        builder.Register(ctx =>
            new SshCredentialVaultStore(
                ctx.Resolve<IAgentRouterRuntimePathResolver>().ResolveRelativeToContentRoot(ctx.Resolve<SshExecutionRuntimeSettings>().VaultPath),
                ctx.Resolve<IAgentRouterRuntimePathResolver>().ResolveRelativeToContentRoot(ctx.Resolve<SshExecutionRuntimeSettings>().VaultKeyPath),
                AppContext.BaseDirectory))
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<FileSystemSshExecutionTraceWriter>()
            .As<ISshExecutionTraceWriter>()
            .SingleInstance();

        builder.RegisterType<SshNetCommandExecutor>()
            .As<ISshCommandExecutor>()
            .SingleInstance();
    }
}
