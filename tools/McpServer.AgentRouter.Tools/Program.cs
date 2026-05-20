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

        try
        {
            return command switch
            {
                "chat" => await RunChatAsync(ParseOptions(args, 1), cancellationToken).ConfigureAwait(false),
                "ollama-ls" => await RunOllamaListAsync(ParseOptions(args, 1), cancellationToken).ConfigureAwait(false),
                "ollama" => await RunOllamaAsync(args, cancellationToken).ConfigureAwait(false),
                "chat-status" => await RunChatStatusAsync(ParseOptions(args, 1), cancellationToken).ConfigureAwait(false),
                "install-local-clients" => await RunInstallLocalClientsAsync(ParseOptions(args, 1), cancellationToken).ConfigureAwait(false),
                "verify" => await RunVerifyAsync(ParseOptions(args, 1), cancellationToken).ConfigureAwait(false),
                "stress" => await RunStressAsync(ParseOptions(args, 1), cancellationToken).ConfigureAwait(false),
                "smoke" => await RunSmokeAsync(ParseOptions(args, 1), cancellationToken).ConfigureAwait(false),
                "provider-unavailable" => await RunProviderUnavailableAsync(ParseOptions(args, 1), cancellationToken).ConfigureAwait(false),
                _ => UnknownCommand(command)
            };
        }
        catch (OperationCanceledException)
        {
            ConsoleWriter.WriteWarning("Operation cancelled.");
            return 130;
        }
    }

    internal static void WarnIfChatJsonOutput(string command, TextWriter errorWriter)
    {
        if (!string.Equals(command, "chat", StringComparison.OrdinalIgnoreCase) || !CliOutput.IsJson)
        {
            return;
        }

        errorWriter.WriteLine("Chat is in JSON output mode. Stdout will be machine-readable JSON.");
    }

    private static async Task<int> RunStressAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        CliOutput.Configure(options.GetOutputMode());
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
        CliOutput.Configure(options.GetOutputMode());
        WarnIfChatJsonOutput("chat", Console.Error);
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

    private static async Task<int> RunOllamaAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length < 2 || !string.Equals(args[1], "ls", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length >= 2 && string.Equals(args[1], "bench", StringComparison.OrdinalIgnoreCase))
            {
                return await RunOllamaBenchAsync(ParseOptions(args, 2), cancellationToken).ConfigureAwait(false);
            }

            HelpWriter.WriteRootHelp();
            return 2;
        }

        var options = ParseOptions(args, 2);
        return await RunOllamaListAsync(options, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> RunOllamaBenchAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        CliOutput.Configure(options.GetOutputMode());
        var settings = OllamaBenchSettings.FromOptions(options);
        using var httpClient = HttpClientFactory.Create(settings.TimeoutSeconds);
        var runner = new OllamaBenchRunner(settings, httpClient, Console.Out, Console.Error);
        var result = await runner.RunAsync(cancellationToken).ConfigureAwait(false);

        if (CliOutput.IsJson)
        {
            WriteJson(new
            {
                command = "ollama bench",
                status = result.ServerReachable ? "completed" : "failed",
                baseUrl = result.BaseUrl,
                serverReachable = result.ServerReachable,
                message = result.Message,
                reportDirectory = string.IsNullOrWhiteSpace(settings.ReportDirectory) ? null : Path.GetFullPath(settings.ReportDirectory),
                modelCount = result.Models.Count,
                caseCount = result.Cases.Count,
                models = result.Models,
                cases = result.Cases
            });
        }

        return result.ServerReachable ? 0 : 1;
    }

    private static async Task<int> RunOllamaListAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        CliOutput.Configure(options.GetOutputMode());
        var settings = OllamaListSettings.FromOptions(options);
        using var httpClient = HttpClientFactory.Create(settings.TimeoutSeconds);
        var runner = new OllamaListRunner(settings, httpClient, Console.Out, Console.Error);
        var result = await runner.RunAsync(cancellationToken).ConfigureAwait(false);

        if (CliOutput.IsJson)
        {
            WriteJson(new
            {
                command = "ollama ls",
                status = result.ServerReachable ? "completed" : "failed",
                baseUrl = result.BaseUrl,
                serverReachable = result.ServerReachable,
                message = result.Message,
                modelCount = result.Models.Count,
                models = result.Models
            });
        }

        return result.ServerReachable ? 0 : 1;
    }

    private static async Task<int> RunChatStatusAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        CliOutput.Configure(options.GetOutputMode());
        var statusLockPath = options.GetString("status-lock", string.Empty);
        if (string.IsNullOrWhiteSpace(statusLockPath))
        {
            ConsoleWriter.WriteError("Chat status requires --status-lock <path>.");
            return 2;
        }

        await ChatStatusWindow.RunAsync(Path.GetFullPath(statusLockPath), cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> RunVerifyAsync(CommandLineOptions options, CancellationToken cancellationToken)
    {
        CliOutput.Configure(options.GetOutputMode());
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
        CliOutput.Configure(options.GetOutputMode());
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
        CliOutput.Configure(options.GetOutputMode());
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
        CliOutput.Configure(options.GetOutputMode());
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

    private static CommandLineOptions ParseOptions(string[] args, int startIndex)
    {
        return CommandLineOptions.Parse(SliceArguments(args, startIndex));
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

internal static class ChatStatusWindow
{
    private static readonly string[] SpinnerFrames = ["|", "/", "-", "\\"];

    public static async Task RunAsync(string statePath, CancellationToken cancellationToken)
    {
        if (Console.IsOutputRedirected)
        {
            return;
        }

        try
        {
            Console.Title = "McpServer Chat Status";
        }
        catch
        {
        }

        var frameIndex = 0;
        var lastRenderedState = string.Empty;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!File.Exists(statePath))
            {
                break;
            }

            var state = ReadState(statePath);
            if (!string.Equals(state, lastRenderedState, StringComparison.Ordinal))
            {
                RenderStatus(state);
                lastRenderedState = state;
                frameIndex = 0;
            }

            if (string.IsNullOrWhiteSpace(state))
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                continue;
            }

            RenderSpinner(state, SpinnerFrames[frameIndex]);
            frameIndex = (frameIndex + 1) % SpinnerFrames.Length;

            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
        }

        ClearLine();
    }

    private static string ReadState(string statePath)
    {
        try
        {
            return File.Exists(statePath) ? File.ReadAllText(statePath) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void RenderStatus(string? state)
    {
        var text = "Status: " + (string.IsNullOrWhiteSpace(state) ? "Ready." : state);
        WriteLine(text);
    }

    private static void RenderSpinner(string state, string frame)
    {
        var text = frame + " " + state;
        WriteLine(text);
    }

    private static void ClearLine()
    {
        try
        {
            if (Console.IsOutputRedirected)
            {
                return;
            }

            Console.Write('\r');
            Console.Write(new string(' ', Math.Max(0, Console.WindowWidth - 1)));
            Console.Write('\r');
        }
        catch
        {
        }
    }

    private static void WriteLine(string text)
    {
        try
        {
            ClearLine();
            Console.Write('\r');
            Console.Write(text);
            PadToWidth(text);
            Console.Out.Flush();
        }
        catch
        {
        }
    }

    private static void PadToWidth(string text)
    {
        try
        {
            var width = Console.WindowWidth;
            var padding = Math.Max(0, width - text.Length - 1);
            if (padding > 0)
            {
                Console.Write(new string(' ', padding));
            }
        }
        catch
        {
        }
    }
}
