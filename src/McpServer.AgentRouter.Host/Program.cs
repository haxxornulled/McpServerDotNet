using System.Net;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.Runtime;
using McpServer.AgentRouter.Host.DependencyInjection;
using McpServer.AgentRouter.Host.Endpoints;
using McpServer.AgentRouter.Host.Middleware;
using McpServer.AgentRouter.Host.Services;
using McpServer.AgentRouter.Host.Configuration;
using McpServer.AgentRouter.Infrastructure.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Bootstrapping MCPServer AgentRouter host");

    var contentRootPath = ResolveAgentRouterContentRoot();
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        ContentRootPath = contentRootPath
    });

    builder.Logging.ClearProviders();
    builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());

    builder.Host.UseSerilog((context, services, configuration) =>
    {
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext();
    });

    builder.Services.Configure<AgentRouterOptions>(
        builder.Configuration.GetSection(AgentRouterOptions.SectionName));

    var bindUrl = builder.Configuration[$"{AgentRouterOptions.SectionName}:BindUrl"]
        ?? "http://127.0.0.1:5177";

    if (!string.IsNullOrWhiteSpace(bindUrl))
    {
        builder.WebHost.UseUrls(bindUrl);
    }

    builder.Services.AddHttpClient("agent-router-ollama", client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        });

    builder.Services.AddRouting();
    builder.Services.AddHostedService<AgentRouterStartupLifecycleService>();

    builder.Host.ConfigureContainer<ContainerBuilder>(container =>
    {
        container.RegisterModule(new AgentRouterApplicationModule());
        container.RegisterModule(new AgentRouterInfrastructureModule());
    });

    Log.Information(
        "MCPServer AgentRouter host configured for {BindUrl}; building host",
        bindUrl);

    var app = builder.Build();
    using var contentRootScope = app.Services
        .GetRequiredService<IAgentRouterRuntimePathResolver>()
        .PushContentRoot(contentRootPath);

    _ = app.Services.GetRequiredService<AgentRouterRuntimeSettings>();

    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var listeningUrls = app.Urls.Count > 0
            ? string.Join(", ", app.Urls)
            : bindUrl;

        Log.Information(
            "MCPServer AgentRouter host started and listening on {ListeningUrls}",
            listeningUrls);
    });

    app.Lifetime.ApplicationStopping.Register(() =>
    {
        Log.Information("MCPServer AgentRouter host is stopping");
    });

    app.UseSerilogRequestLogging(options =>
    {
        options.GetLevel = (httpContext, elapsed, exception) =>
        {
            if (exception is not null)
            {
                return LogEventLevel.Error;
            }

            var statusCode = httpContext.Response.StatusCode;

            if (statusCode >= StatusCodes.Status500InternalServerError)
            {
                return LogEventLevel.Error;
            }

            if (statusCode >= StatusCodes.Status400BadRequest)
            {
                return LogEventLevel.Warning;
            }

            return LogEventLevel.Debug;
        };
    });
    app.UseAgentRouterErrorEnvelope();
    app.UseAgentRouterApiKeyGuard();
    app.MapAgentRouterEndpoints();

    await app.RunAsync().ConfigureAwait(false);
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "MCPServer AgentRouter host terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync().ConfigureAwait(false);
}

static string ResolveAgentRouterContentRoot()
{
    var candidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
    var projectFile = Path.Combine(candidate, "McpServer.AgentRouter.Host.csproj");

    return File.Exists(projectFile)
        ? candidate
        : AppContext.BaseDirectory;
}
