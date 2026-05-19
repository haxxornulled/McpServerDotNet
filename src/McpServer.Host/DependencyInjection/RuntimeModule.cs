using Autofac;
using McpServer.Protocol.Lifecycle;
using McpServer.Protocol.Routing;
using McpServer.Protocol.Session;

namespace McpServer.Host.DependencyInjection;

public sealed class RuntimeModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<McpSession>().SingleInstance();
        builder.RegisterType<CapabilityProvider>().SingleInstance();
        builder.RegisterType<InitializeHandler>().SingleInstance();
        builder.RegisterType<ShutdownHandler>().SingleInstance();
        builder.RegisterType<ExitHandler>().SingleInstance();
        builder.RegisterType<ToolCallRouter>().SingleInstance();
        builder.RegisterType<ResourceReadRouter>().SingleInstance();
        builder.RegisterType<PromptRouter>().SingleInstance();
    }
}
