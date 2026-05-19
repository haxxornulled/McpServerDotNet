using Autofac;
using McpServer.Application.Abstractions.Execution;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Execution;
using McpServer.Application.Mcp.Tools;
using McpServer.Host.Configuration;

namespace McpServer.Host.DependencyInjection;

public sealed class ShellFeatureModule(ShellOptions options) : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterInstance(new ShellExecutionPolicyOptions(
                options.AllowShellFallback,
                options.AllowedCommands ?? [],
                options.DeniedCommands ?? ShellExecutionPolicyOptions.DefaultDeniedCommands,
                Math.Max(1, options.MaxTimeoutSeconds),
                Math.Max(256, options.MaxOutputChars)))
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ShellExecutionPolicy>()
            .As<IShellExecutionPolicy>()
            .SingleInstance();

        builder.RegisterType<ShellExecToolHandler>()
            .AsSelf()
            .As<IToolHandler>()
            .SingleInstance();
    }
}
