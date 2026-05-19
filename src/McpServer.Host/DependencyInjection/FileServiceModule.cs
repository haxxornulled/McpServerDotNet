using Autofac;
using McpServer.Application.Abstractions.Execution;
using McpServer.Application.Abstractions.Files;
using McpServer.Infrastructure.Execution;
using McpServer.Infrastructure.Files;

namespace McpServer.Host.DependencyInjection;

public sealed class FileServiceModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<FileMutationLockProvider>()
            .As<IFileMutationLockProvider>()
            .SingleInstance();

        builder.RegisterType<DestructiveFileOperationPolicy>()
            .As<IDestructiveFileOperationPolicy>()
            .SingleInstance();

        builder.RegisterType<FileSystemService>()
            .As<IFileSystemService>()
            .SingleInstance();

        builder.RegisterType<McpServer.Infrastructure.Execution.ProcessExecutionService>()
            .As<IProcessExecutionService>()
            .SingleInstance();
    }
}
