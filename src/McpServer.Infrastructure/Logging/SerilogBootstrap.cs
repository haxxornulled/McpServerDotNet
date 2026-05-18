using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Debugging;
using Serilog.Events;

namespace McpServer.Infrastructure.Logging;

public static class SerilogBootstrap
{
    private const int DefaultAsyncBufferSize = 8192;
    private const int DefaultRetainedFileCount = 14;
    private const long DefaultFileSizeLimitBytes = 32L * 1024L * 1024L;
    private const string DefaultLogDirectory = "logs";

    private const string ConsoleOutputTemplate =
        "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}";

    private const string FileOutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{Exception}";

    public static Serilog.ILogger CreateBootstrapLogger()
    {
        EnableSelfLogIfRequested();

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.WithProperty("Application", "McpServer")
            .WriteTo.Console(
                outputTemplate: ConsoleOutputTemplate,
                standardErrorFromLevel: LogEventLevel.Verbose)
            .CreateBootstrapLogger();
    }

    public static void Configure(LoggerConfiguration configuration, IConfiguration appConfiguration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(appConfiguration);

        EnableSelfLogIfRequested();

        var logPath = ResolveLogPath(appConfiguration);
        var asyncBufferSize = GetPositiveInt(
            appConfiguration,
            "McpServer:Logging:AsyncBufferSize",
            DefaultAsyncBufferSize);
        var retainedFileCount = GetPositiveInt(
            appConfiguration,
            "McpServer:Logging:RetainedFileCountLimit",
            DefaultRetainedFileCount);
        var fileSizeLimitBytes = GetPositiveLong(
            appConfiguration,
            "McpServer:Logging:FileSizeLimitBytes",
            DefaultFileSizeLimitBytes);

        configuration
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .ReadFrom.Configuration(appConfiguration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "McpServer")
            .WriteTo.Console(
                outputTemplate: ConsoleOutputTemplate,
                standardErrorFromLevel: LogEventLevel.Verbose)
            .WriteTo.Async(
                sinks => sinks.File(
                    path: logPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: retainedFileCount,
                    fileSizeLimitBytes: fileSizeLimitBytes,
                    rollOnFileSizeLimit: true,
                    shared: false,
                    buffered: false,
                    outputTemplate: FileOutputTemplate),
                bufferSize: asyncBufferSize,
                blockWhenFull: false);
    }

    private static string ResolveLogPath(IConfiguration configuration)
    {
        var configuredDirectory = configuration["McpServer:Logging:Directory"];
        var environmentDirectory = Environment.GetEnvironmentVariable("MCPSERVER_LOG_DIRECTORY");
        var directory = FirstNonEmpty(environmentDirectory, configuredDirectory, DefaultLogDirectory);

        directory = Environment.ExpandEnvironmentVariables(directory);

        if (!Path.IsPathFullyQualified(directory))
        {
            directory = Path.GetFullPath(directory);
        }

        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "mcp-server-.log");
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return DefaultLogDirectory;
    }

    private static int GetPositiveInt(IConfiguration configuration, string key, int fallback)
    {
        var value = configuration[key];
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private static long GetPositiveLong(IConfiguration configuration, string key, long fallback)
    {
        var value = configuration[key];
        return long.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private static void EnableSelfLogIfRequested()
    {
        var enabled = Environment.GetEnvironmentVariable("MCPSERVER_SERILOG_SELFLOG");
        if (!string.Equals(enabled, "1", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SelfLog.Enable(message => Console.Error.WriteLine($"[SerilogSelfLog] {message}"));
    }
}
