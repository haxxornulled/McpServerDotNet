using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace McpServer.AgentRouter.Tools;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(outputTemplate: "{Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            using var cancellation = new CancellationTokenSource();

            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };

            return await AgentRouterToolProgram.RunAsync(args, cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "AgentRouter tools failed.");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}

internal static class AgentRouterToolProgram
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            HelpWriter.WriteRootHelp();
            return 0;
        }

        var command = args[0].Trim().ToLowerInvariant();
        var options = CommandLineOptions.Parse(args.Skip(1).ToArray());

        try
        {
            return command switch
            {
                "stress" => await RunStressAsync(options, cancellationToken).ConfigureAwait(false),
                "smoke" => await RunSmokeAsync(options, cancellationToken).ConfigureAwait(false),
                "provider-unavailable" => await RunProviderUnavailableAsync(options, cancellationToken).ConfigureAwait(false),
                _ => UnknownCommand(command)
            };
        }
        catch (OperationCanceledException)
        {
            ConsoleWriter.WriteWarning("Operation cancelled.");
            return 130;
        }
    }

    private static async Task<int> RunStressAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        var settings = StressSettings.FromOptions(options);
        using var httpClient = HttpClientFactory.Create(settings.TimeoutSeconds);
        var runner = new StressRunner(httpClient, settings, new ConsoleStressReporter());
        var result = await runner.RunAsync(cancellationToken).ConfigureAwait(false);
        return result.TotalFailures == 0 ? 0 : 1;
    }

    private static async Task<int> RunSmokeAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        var settings = StressSettings.FromOptions(options).AsSmokeProfile();
        using var httpClient = HttpClientFactory.Create(settings.TimeoutSeconds);
        var runner = new StressRunner(httpClient, settings, new ConsoleStressReporter());
        var result = await runner.RunAsync(cancellationToken).ConfigureAwait(false);
        return result.TotalFailures == 0 ? 0 : 1;
    }

    private static async Task<int> RunProviderUnavailableAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        var settings = StressSettings.FromOptions(options).AsProviderUnavailableProfile();
        var strict = options.HasFlag("strict");
        using var httpClient = HttpClientFactory.Create(settings.TimeoutSeconds);
        var probe = new ProviderUnavailableProbe(httpClient, settings.RouterBaseUrl, settings.ChatModel, strict);
        return await probe.RunAsync(cancellationToken).ConfigureAwait(false) ? 0 : 1;
    }

    private static int UnknownCommand(string command)
    {
        ConsoleWriter.WriteError($"Unknown command '{command}'.");
        HelpWriter.WriteRootHelp();
        return 2;
    }

    private static bool IsHelp(string value)
    {
        return string.Equals(value, "help", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "/?", StringComparison.OrdinalIgnoreCase);
    }
}
