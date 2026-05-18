using System.Net;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using McpServer.Host.Configuration;
using McpServer.Host.DependencyInjection;
using McpServer.Host.Transport.Stdio;
using McpServer.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using VapeCache.Abstractions.Caching;
using VapeCache.Extensions.DependencyInjection;

Log.Logger = SerilogBootstrap.CreateBootstrapLogger();

try
{
    Log.Information("Bootstrapping MCPServer host");

    var builder = Host.CreateDefaultBuilder(args)
        .UseContentRoot(AppContext.BaseDirectory)
        .UseServiceProviderFactory(new AutofacServiceProviderFactory())
        .UseSerilog((context, services, configuration) =>
        {
            SerilogBootstrap.Configure(configuration, context.Configuration);
        })
        .ConfigureServices((context, services) =>
        {
            services.Configure<McpServerOptions>(
                context.Configuration.GetSection(McpServerOptions.SectionName));

            services.AddHostedService<StdioServerLifecycleService>();

            services.AddHttpClient("web-access")
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    MaxAutomaticRedirections = 5,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                });

            services.AddHttpClient("web-search")
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    MaxAutomaticRedirections = 5,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                });

            services.AddHttpClient("ollama", client =>
                {
                    client.Timeout = Timeout.InfiniteTimeSpan;
                })
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                });

            RegisterVapeCache(context, services);
        })
        .ConfigureContainer<ContainerBuilder>((context, container) =>
        {
            container.RegisterModule(new AutofacRootModule(context.Configuration));
        });

    await builder.Build().RunAsync().ConfigureAwait(false);
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "MCPServer host terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync().ConfigureAwait(false);
}

static void RegisterVapeCache(HostBuilderContext context, IServiceCollection services)
{
    var redisSection = context.Configuration.GetSection("RedisConnection");
    var envConnStr = Environment.GetEnvironmentVariable("VAPECACHE_REDIS_CONNECTIONSTRING");
    var redisConfigured = false;

    if (!string.IsNullOrWhiteSpace(envConnStr))
    {
        redisConfigured = true;
    }
    else if (redisSection.Exists())
    {
        var host = redisSection["Host"];
        var connectionString = redisSection["ConnectionString"];
        redisConfigured = !string.IsNullOrWhiteSpace(host) || !string.IsNullOrWhiteSpace(connectionString);
    }

    if (redisConfigured)
    {
        services.AddVapeCache(context.Configuration);
        return;
    }

    services.AddVapeCacheInMemory(context.Configuration)
        .WithCacheStampedeProfile(CacheStampedeProfile.Balanced);
}
