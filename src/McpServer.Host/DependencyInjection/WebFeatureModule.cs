using Autofac;
using McpServer.Application.Abstractions.Mcp;
using McpServer.Application.Abstractions.Web;
using McpServer.Application.Mcp.Tools;
using McpServer.Host.Configuration;
using McpServer.Infrastructure.Web;

namespace McpServer.Host.DependencyInjection;

public sealed class WebFeatureModule(WebAccessOptions options) : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.Register(_ =>
            {
                var allowedHosts = new System.Collections.Generic.HashSet<string>(
                    options.AllowedHosts.Where(static x => !string.IsNullOrWhiteSpace(x)),
                    StringComparer.OrdinalIgnoreCase);

                return new WebPolicy(
                    allowedHosts,
                    options.AllowLocalLoopbackHosts,
                    options.SearchBaseUrl);
            })
            .As<IWebPolicy>()
            .SingleInstance();

        builder.RegisterType<DuckDuckGoHtmlSearchProvider>()
            .As<IWebSearchProvider>()
            .SingleInstance();

        builder.RegisterType<WebFetchService>().As<IWebFetchService>().SingleInstance();
        builder.RegisterType<WebSearchService>().As<IWebSearchService>().SingleInstance();
        builder.RegisterType<WebScrapeService>().As<IWebScrapeService>().SingleInstance();

        RegisterTool<WebFetchUrlToolHandler>(builder);
        RegisterTool<WebSearchToolHandler>(builder);
        RegisterTool<WebScrapeUrlToolHandler>(builder);
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
