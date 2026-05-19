using Autofac;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Mcp.Tools;

namespace McpServer.Host.DependencyInjection;

public sealed class ActivityToolModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        RegisterTool<ActivityRouteToolHandler>(builder);
        RegisterTool<ActivitySchemasListToolHandler>(builder);
        RegisterTool<ActivityContextPreviewToolHandler>(builder);
        RegisterTool<ActivityRunToolHandler>(builder);
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
