using Autofac;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Mcp.Tools;

namespace McpServer.Host.DependencyInjection;

public sealed class WorkspaceToolModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        RegisterTool<WorkspaceSetRootToolHandler>(builder);
        RegisterTool<WorkspaceOpenToolHandler>(builder);
        RegisterTool<WorkspaceSelectFolderToolHandler>(builder);
        RegisterTool<WorkspaceStatusToolHandler>(builder);
        RegisterTool<WorkspaceInspectToolHandler>(builder);
    }

    private static void RegisterTool<TToolHandler>(ContainerBuilder builder)
        where TToolHandler : class, IToolHandler
    {
        builder.RegisterType<TToolHandler>()
            .AsSelf()
            .As<IToolHandler>()
            .SingleInstance();
    }
}
