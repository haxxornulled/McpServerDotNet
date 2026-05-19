using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace McpServer.AgentRouter.Tools;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var jsonOutputRequested = IsJsonOutputRequested(args);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(jsonOutputRequested ? Serilog.Events.LogEventLevel.Warning : Serilog.Events.LogEventLevel.Information)
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

    private static bool IsJsonOutputRequested(string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (!string.Equals(args[index], "--output", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return string.Equals(args[index + 1], "json", StringComparison.OrdinalIgnoreCase);
        }

        return false;
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
        var options = CommandLineOptions.Parse(SliceArguments(args, 1));
        CliOutput.Configure(options.GetOutputMode());

        try
        {
            return command switch
            {
                "chat" => await RunChatAsync(options, cancellationToken).ConfigureAwait(false),
                "install-local-clients" => await RunInstallLocalClientsAsync(options, cancellationToken).ConfigureAwait(false),
                "verify" => await RunVerifyAsync(options, cancellationToken).ConfigureAwait(false),
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
        if (CliOutput.IsJson)
        {
            WriteJson(new
            {
                command = "stress",
                status = "completed",
                totalFailures = result.TotalFailures,
                reportDirectory = result.ReportDirectory,
                runId = result.StressRunId,
                results = result.Results,
                summaries = result.Summaries
            });
        }
        return result.TotalFailures == 0 ? 0 : 1;
    }

    private static async Task<int> RunChatAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        var settings = ChatConsoleSettings.FromOptions(options);
        using var httpClient = HttpClientFactory.Create(settings.TimeoutSeconds);
        var runner = new ChatConsoleRunner(
            settings,
            httpClient,
            Console.In,
            Console.Out,
            Console.Error,
            Console.IsInputRedirected);

        var result = await runner.RunAsync(cancellationToken).ConfigureAwait(false);
        if (CliOutput.IsJson)
        {
            WriteJson(new
            {
                command = "chat",
                status = result.ExitCode == 0 ? "completed" : "failed",
                exitCode = result.ExitCode,
                routerBaseUrl = result.RouterBaseUrl,
                model = result.Model,
                systemPrompt = result.SystemPrompt,
                prompt = result.Prompt,
                response = result.Response,
                finishReason = result.FinishReason,
                promptTokens = result.PromptTokens,
                completionTokens = result.CompletionTokens,
                totalTokens = result.TotalTokens,
                streamed = result.Streamed,
                interactive = result.Interactive,
                errorMessage = result.ErrorMessage,
                turns = result.Turns
            });
        }

        return result.ExitCode;
    }

    private static async Task<int> RunVerifyAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        var settings = RepositoryVerificationSettings.FromOptions(options);
        var runner = new RepositoryVerificationRunner(settings);
        var exitCode = await runner.RunAsync(cancellationToken).ConfigureAwait(false);
        if (CliOutput.IsJson)
        {
            WriteJson(new
            {
                command = "verify",
                status = exitCode == 0 ? "completed" : "failed",
                exitCode,
                repositoryRoot = Path.GetFullPath(settings.RepositoryRootPath),
                solution = Path.GetFullPath(Path.Combine(Path.GetFullPath(settings.RepositoryRootPath), settings.SolutionPath)),
                configuration = settings.Configuration,
                unitTestsProject = settings.UnitTestsProjectPath,
                integrationTestsProject = settings.IntegrationTestsProjectPath,
                agentRouterUnitTestsProject = settings.AgentRouterUnitTestsProjectPath
            });
        }

        return exitCode;
    }

    private static async Task<int> RunInstallLocalClientsAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        var settings = LocalMcpClientConfigSettings.FromOptions(options);
        var runner = new LocalMcpClientConfigRunner(settings);
        var exitCode = await runner.RunAsync(cancellationToken).ConfigureAwait(false);
        if (CliOutput.IsJson)
        {
            var repoRoot = Path.GetFullPath(settings.RepositoryRootPath);
            WriteJson(new
            {
                command = "install-local-clients",
                status = exitCode == 0 ? "completed" : "failed",
                exitCode,
                repositoryRoot = repoRoot,
                hostExecutable = Path.Combine(repoRoot, "src", "McpServer.Host", "bin", "Release", "net10.0", OperatingSystem.IsWindows() ? "McpServer.Host.exe" : "McpServer.Host"),
                codexConfig = Path.Combine(repoRoot, ".codex", "config.toml"),
                vscodeConfig = Path.Combine(repoRoot, ".vscode", "mcp.json"),
                mcpConfig = Path.Combine(repoRoot, ".mcp.json"),
                logsDirectory = Path.Combine(repoRoot, "logs"),
                defaultModel = settings.DefaultModel,
                allowedModels = settings.AllowedModels
            });
        }

        return exitCode;
    }

    private static async Task<int> RunSmokeAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        var settings = StressSettings.FromOptions(options).AsSmokeProfile();
        using var httpClient = HttpClientFactory.Create(settings.TimeoutSeconds);
        var runner = new StressRunner(httpClient, settings, new ConsoleStressReporter());
        var result = await runner.RunAsync(cancellationToken).ConfigureAwait(false);
        if (CliOutput.IsJson)
        {
            WriteJson(new
            {
                command = "smoke",
                status = "completed",
                totalFailures = result.TotalFailures,
                reportDirectory = result.ReportDirectory,
                runId = result.StressRunId,
                results = result.Results,
                summaries = result.Summaries
            });
        }
        return result.TotalFailures == 0 ? 0 : 1;
    }

    private static async Task<int> RunProviderUnavailableAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        var settings = StressSettings.FromOptions(options).AsProviderUnavailableProfile();
        var strict = options.HasFlag("strict");
        using var httpClient = HttpClientFactory.Create(settings.TimeoutSeconds);
        var probe = new ProviderUnavailableProbe(httpClient, settings.RouterBaseUrl, settings.ChatModel, strict);
        var available = await probe.RunAsync(cancellationToken).ConfigureAwait(false);
        if (CliOutput.IsJson)
        {
            WriteJson(new
            {
                command = "provider-unavailable",
                status = available ? "available" : "unavailable",
                available,
                strict,
                routerBaseUrl = settings.RouterBaseUrl.ToString(),
                chatModel = settings.ChatModel
            });
        }

        return available ? 0 : 1;
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

    private static bool IsJsonOutputRequested(string[] args)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (!string.Equals(args[index], "--output", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return string.Equals(args[index + 1], "json", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string[] SliceArguments(string[] args, int startIndex)
    {
        if (startIndex <= 0)
        {
            return args;
        }

        if (startIndex >= args.Length)
        {
            return Array.Empty<string>();
        }

        var slice = new string[args.Length - startIndex];
        Array.Copy(args, startIndex, slice, 0, slice.Length);
        return slice;
    }

    private static void WriteJson(object value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        }));
    }
}
