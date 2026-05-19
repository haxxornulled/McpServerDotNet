using Autofac;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Mcp.Resources;

namespace McpServer.Host.DependencyInjection;

public sealed class FileResourceModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<FsFileTextResourceHandler>().As<IResourceHandler>().SingleInstance();
        builder.RegisterType<FsDirectoryResourceHandler>().As<IResourceHandler>().SingleInstance();
        builder.RegisterType<FsFileMetadataResourceHandler>().As<IResourceHandler>().SingleInstance();
        builder.RegisterType<WorkspaceTreeResourceHandler>().As<IResourceHandler>().SingleInstance();
        builder.RegisterType<WorkspaceChangesResourceHandler>().As<IResourceHandler>().SingleInstance();
    }
}
