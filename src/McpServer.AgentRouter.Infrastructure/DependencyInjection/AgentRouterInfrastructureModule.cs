using Autofac;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Infrastructure.AgentLoops;
using McpServer.AgentRouter.Infrastructure.Mcp;
using McpServer.AgentRouter.Infrastructure.Ollama;
using McpServer.AgentRouter.Infrastructure.Shell;
using McpServer.AgentRouter.Infrastructure.Stores;
using McpServer.AgentRouter.Infrastructure.Ssh;

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

        builder.RegisterType<FileSystemSshProfileStore>()
            .As<ISshProfileStore>()
            .SingleInstance();

        builder.RegisterType<FileSystemSshExecutionTraceWriter>()
            .As<ISshExecutionTraceWriter>()
            .SingleInstance();

        builder.RegisterType<SshNetCommandExecutor>()
            .As<ISshCommandExecutor>()
            .SingleInstance();
    }
}
