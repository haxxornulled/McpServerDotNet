using Autofac;
using McpServer.Application.Activities;
using McpServer.Application.Abstractions.Mcp;

namespace McpServer.Host.DependencyInjection;

public sealed class ActivityServiceModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<ActivityProfileRegistry>()
            .As<IActivityProfileRegistry>()
            .SingleInstance();

        builder.RegisterType<RuleFirstActivityRouter>()
            .As<IActivityRouter>()
            .SingleInstance();

        builder.RegisterType<StructuredOutputSchemaRegistry>()
            .As<IStructuredOutputSchemaRegistry>()
            .SingleInstance();

        builder.RegisterType<ActivityContextBuilder>()
            .As<IActivityContextBuilder>()
            .SingleInstance();

        builder.RegisterType<InMemoryActivitySessionStateStore>()
            .As<IActivitySessionStateStore>()
            .SingleInstance();
    }
}
