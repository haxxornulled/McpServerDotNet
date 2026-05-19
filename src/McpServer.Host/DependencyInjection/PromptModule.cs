using Autofac;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Mcp.Prompts;

namespace McpServer.Host.DependencyInjection;

public sealed class PromptModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<SummarizeFilePromptHandler>().As<IPromptHandler>().SingleInstance();
        builder.RegisterType<ReviewDirectoryPromptHandler>().As<IPromptHandler>().SingleInstance();
    }
}
