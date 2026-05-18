using Autofac;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.AgentRuns;
using McpServer.AgentRouter.Application.AgentLoops;
using McpServer.AgentRouter.Application.Mcp;
using McpServer.AgentRouter.Application.Runtime;
using McpServer.AgentRouter.Application.Services;
using McpServer.AgentRouter.Application.Shell;
using McpServer.AgentRouter.Application.Ssh;
using McpServer.AgentRouter.Host.Configuration;
using Microsoft.Extensions.Options;

namespace McpServer.AgentRouter.Host.DependencyInjection;

public sealed class AgentRouterApplicationModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<AgentRouterRuntimePathResolver>()
            .As<IAgentRouterRuntimePathResolver>()
            .SingleInstance();

        builder.Register(context =>
            ConfiguredAgentRouterRuntimeSettingsFactory.Create(
                context.Resolve<IOptions<AgentRouterOptions>>().Value))
            .AsSelf()
            .SingleInstance();

        builder.Register(context => context.Resolve<AgentRouterRuntimeSettings>().RunStorage)
            .AsSelf()
            .SingleInstance();

        builder.Register(context => context.Resolve<AgentRouterRuntimeSettings>().McpServer)
            .AsSelf()
            .SingleInstance();

        builder.Register(context => context.Resolve<AgentRouterRuntimeSettings>().AgentLoop)
            .AsSelf()
            .SingleInstance();

        builder.Register(context => context.Resolve<AgentRouterRuntimeSettings>().ToolExecution)
            .AsSelf()
            .SingleInstance();

        builder.Register(context => context.Resolve<AgentRouterRuntimeSettings>().ShellExecution)
            .AsSelf()
            .SingleInstance();

        builder.Register(context => context.Resolve<AgentRouterRuntimeSettings>().SshExecution)
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ConfigurationModelProfileResolver>()
            .As<IModelProfileResolver>()
            .SingleInstance();

        builder.RegisterType<ModelRouter>()
            .As<IModelRouter>()
            .InstancePerLifetimeScope();

        builder.RegisterType<AgentRunService>()
            .As<IAgentRunService>()
            .InstancePerLifetimeScope();

        builder.RegisterType<McpAgentStepPlanner>()
            .As<IAgentStepPlanner>()
            .InstancePerLifetimeScope();

        builder.RegisterType<ExplicitAllowlistToolExecutionPolicy>()
            .As<IToolExecutionPolicy>()
            .SingleInstance();

        builder.RegisterType<McpAgentToolExecutor>()
            .AsSelf()
            .InstancePerLifetimeScope();

        builder.RegisterType<ShellAgentToolExecutor>()
            .AsSelf()
            .InstancePerLifetimeScope();

        builder.RegisterType<SshAgentToolExecutor>()
            .AsSelf()
            .InstancePerLifetimeScope();

        builder.RegisterType<CompositeAgentToolExecutor>()
            .As<IAgentToolExecutor>()
            .InstancePerLifetimeScope();

        builder.RegisterType<McpAgentResultInspector>()
            .As<IAgentResultInspector>()
            .InstancePerLifetimeScope();

        builder.RegisterType<BoundedAgentLoopValidator>()
            .As<IAgentLoopValidator>()
            .InstancePerLifetimeScope();

        builder.RegisterType<AutonomousLoopRunner>()
            .As<IAutonomousLoopRunner>()
            .InstancePerLifetimeScope();

        builder.RegisterType<McpToolCallPolicy>()
            .As<IMcpToolCallPolicy>()
            .SingleInstance();

        builder.RegisterType<McpToolCallService>()
            .As<IMcpToolCallService>()
            .InstancePerLifetimeScope();

        builder.RegisterType<ShellExecutionPolicy>()
            .As<IShellExecutionPolicy>()
            .SingleInstance();

        builder.RegisterType<ShellExecutionService>()
            .As<IShellExecutionService>()
            .InstancePerLifetimeScope();

        builder.RegisterType<SshExecutionPolicy>()
            .As<ISshExecutionPolicy>()
            .SingleInstance();

        builder.RegisterType<SshExecutionService>()
            .As<ISshExecutionService>()
            .InstancePerLifetimeScope();
    }
}
