using Autofac;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Files;
using McpServer.Domain.Workspace;
using McpServer.Infrastructure.Files;

namespace McpServer.Host.DependencyInjection;

public sealed class WorkspaceStateModule(
    string workspaceRoot,
    string[] allowedRoots) : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<WorkspaceChangeFeed>()
            .As<IWorkspaceChangeFeed>()
            .SingleInstance();

        builder.RegisterType<WorkspaceMutationService>()
            .As<IWorkspaceMutationService>()
            .SingleInstance();

        builder.Register(ctx =>
            {
                var watcher = new WorkspaceFileWatcher(ctx.Resolve<WorkspacePathState>(), ctx.Resolve<IWorkspaceChangeFeed>());
                watcher.SetProjectRoot(workspaceRoot);
                return watcher;
            })
            .As<IWorkspaceFileWatcher>()
            .SingleInstance();

        builder.Register(_ => new WorkspacePathState(allowedRoots))
            .AsSelf()
            .SingleInstance();

        builder.Register(ctx => new ResourcePathTranslator(ctx.Resolve<WorkspacePathState>()))
            .AsSelf()
            .As<IResourcePathTranslator>()
            .SingleInstance();

        builder.Register(ctx => new PathPolicy(ctx.Resolve<WorkspacePathState>()))
            .AsSelf()
            .As<IPathPolicy>()
            .SingleInstance();
    }
}
