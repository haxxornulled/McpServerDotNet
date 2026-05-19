using Autofac;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Mcp.Tools;

namespace McpServer.Host.DependencyInjection;

public sealed class FileToolModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        RegisterTool<FsWriteTextToolHandler>(builder);
        RegisterTool<FsAppendTextToolHandler>(builder);
        RegisterTool<FsReadFileToolHandler>(builder);
        RegisterTool<FsReadTextToolHandler>(builder);
        RegisterTool<FsGetMetadataToolHandler>(builder);
        RegisterTool<FsListDirectoryToolHandler>(builder);
        RegisterTool<FsCreateDirectoryToolHandler>(builder);
        RegisterTool<FsMovePathToolHandler>(builder);
        RegisterTool<FsCopyPathToolHandler>(builder);
        RegisterTool<FsDeletePathToolHandler>(builder);
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
