using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Serilog;

namespace McpServer.AgentRouter.Tools;

internal sealed class CommandLineOptions
{
    private readonly Dictionary<string, string?> _values;

    private CommandLineOptions(Dictionary<string, string?> values)
    {
        _values = values;
    }

    public static CommandLineOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < args.Length; index++)
        {
            var raw = args[index];
            if (!raw.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument '{raw}'. Options must use --name value syntax.");
            }

            var name = raw[2..];
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Option name cannot be empty.");
            }

            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                values[name] = null;
                continue;
            }

            values[name] = args[index + 1];
            index++;
        }

        return new CommandLineOptions(values);
    }

    public string GetString(string name, string defaultValue)
    {
        return _values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;
    }

    public int GetInt(string name, int defaultValue)
    {
        if (!_values.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new ArgumentException($"Option --{name} must be an integer. Value was '{value}'.");
    }

    public int? GetNullableInt(string name)
    {
        if (!_values.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new ArgumentException($"Option --{name} must be an integer. Value was '{value}'.");
    }

    public double? GetNullableDouble(string name)
    {
        if (!_values.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new ArgumentException($"Option --{name} must be a number. Value was '{value}'.");
    }

    public bool GetBool(string name, bool defaultValue)
    {
        if (!_values.TryGetValue(name, out var value))
        {
            return defaultValue;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        throw new ArgumentException($"Option --{name} must be true or false. Value was '{value}'.");
    }

    public bool HasFlag(string name)
    {
        return _values.ContainsKey(name);
    }

    public string GetOutputMode(string defaultValue = "text")
    {
        return GetString("output", defaultValue);
    }
}

internal static class CliOutput
{
    private static bool _json;

    public static void Configure(string? outputMode)
    {
        _json = string.Equals(outputMode, "json", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsJson => _json;
}

internal static class ConsoleWriter
{
    public static void WriteSection(string name)
    {
        if (CliOutput.IsJson)
        {
            return;
        }

        Log.Information(string.Empty);
        Log.Information("============================================================");
        Log.Information(name);
        Log.Information("============================================================");
    }

    public static void WritePass(string message)
    {
        if (CliOutput.IsJson)
        {
            return;
        }

        Log.Information("[PASS] {Message}", message);
    }

    public static void WriteInfo(string message)
    {
        if (CliOutput.IsJson)
        {
            return;
        }

        Log.Information("[INFO] {Message}", message);
    }

    public static void WriteWarning(string message)
    {
        if (CliOutput.IsJson)
        {
            return;
        }

        Log.Warning("[WARN] {Message}", message);
    }

    public static void WriteError(string message)
    {
        Log.Error("[FAIL] {Message}", message);
    }
}

internal static class HelpWriter
{
    public static void WriteRootHelp()
    {
        Console.WriteLine("MCPServer AgentRouter Tools");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project tools/McpServer.AgentRouter.Tools -- chat [options]");
        Console.WriteLine("  dotnet run --project tools/McpServer.AgentRouter.Tools -- ollama ls [options]");
        Console.WriteLine("  dotnet run --project tools/McpServer.AgentRouter.Tools -- ollama bench [options]");
        Console.WriteLine("  dotnet run --project tools/McpServer.AgentRouter.Tools -- install-local-clients [options]");
        Console.WriteLine("  dotnet run --project tools/McpServer.AgentRouter.Tools -- verify [options]");
        Console.WriteLine("  dotnet run --project tools/McpServer.AgentRouter.Tools -- smoke [options]");
        Console.WriteLine("  dotnet run --project tools/McpServer.AgentRouter.Tools -- stress [options]");
        Console.WriteLine("  dotnet run --project tools/McpServer.AgentRouter.Tools -- provider-unavailable [options]");
        Console.WriteLine("  install-local-clients writes .codex/config.toml, .vscode/mcp.json, and .mcp.json.");
        Console.WriteLine();
        Console.WriteLine("Install-local-clients options:");
        Console.WriteLine("  --repo-root <path>                   Default: current directory");
        Console.WriteLine("  --default-model <model>              Default: qwen3-coder:30b");
        Console.WriteLine("  --allowed-models <a,b,c>             Default: qwen3-coder:30b,qwen2.5-coder:14b,devstral-small-2");
        Console.WriteLine("  --ollama-base-url <url>              Default: http://127.0.0.1:11434");
        Console.WriteLine("  --context-length <n>                 Default: 131072");
        Console.WriteLine("  --num-predict <n>                    Default: 32000");
        Console.WriteLine("  --max-prompt-chars <n>               Default: 500000");
        Console.WriteLine("  --max-output-chars <n>               Default: 32000");
        Console.WriteLine("  --tool-timeout-seconds <n>           Default: 900");
        Console.WriteLine("  --output <text|json>                Default: text");
        Console.WriteLine("  --build                              Build the solution before writing configs.");
        Console.WriteLine("  verify runs restore, build, and the full repo test suite in C#.");
        Console.WriteLine("  smoke runs the high-fidelity harness, including default MCP tool coverage.");
        Console.WriteLine();
        Console.WriteLine("Chat options:");
        Console.WriteLine("  --router-base-url <url>              Default: http://127.0.0.1:5177");
        Console.WriteLine("  --model <model>                      Default: fast-local");
        Console.WriteLine("  --chat-model <model>                 Alias for --model.");
        Console.WriteLine("  --system <text>                      Optional system prompt.");
        Console.WriteLine("  --system-file <path>                 Load the system prompt from a file.");
        Console.WriteLine("  --prompt <text>                      Optional one-shot prompt.");
        Console.WriteLine("  --prompt-file <path>                 Load the prompt from a file.");
        Console.WriteLine("  --temperature <value>                Optional request temperature.");
        Console.WriteLine("  --max-tokens <n>                     Optional max_tokens override.");
        Console.WriteLine("  --stream <true|false>                Default: true, forced off for --output json.");
        Console.WriteLine("  --tools <true|false>                 Default: true, forces non-streaming tool rounds when enabled.");
        Console.WriteLine("  --interactive                        Force an interactive console session.");
        Console.WriteLine("  --transcript <path>                  Write a JSON transcript to disk.");
        Console.WriteLine("  --session-name <name>                Label the transcript/session.");
        Console.WriteLine("  --timeout-seconds <n>                Default: 120");
        Console.WriteLine("  --output <text|json>                 Default: text");
        Console.WriteLine("  /search <query>                      Run the web.search MCP tool from the console.");
        Console.WriteLine();
        Console.WriteLine("Ollama ls options:");
        Console.WriteLine("  --base-url <url>                     Default: http://127.0.0.1:11434");
        Console.WriteLine("  --ollama-base-url <url>              Alias for --base-url.");
        Console.WriteLine("  --timeout-seconds <n>                Default: 15");
        Console.WriteLine("  --output <text|json>                 Default: text");
        Console.WriteLine();
        Console.WriteLine("Ollama bench options:");
        Console.WriteLine("  --base-url <url>                     Default: http://127.0.0.1:11434");
        Console.WriteLine("  --ollama-base-url <url>              Alias for --base-url.");
        Console.WriteLine("  --models <a,b,c>                     Optional model filter.");
        Console.WriteLine("  --prompt-set <name>                  Default: activity-routing");
        Console.WriteLine("  --temperature <value>                Default: 0");
        Console.WriteLine("  --timeout-seconds <n>                Default: 60");
        Console.WriteLine("  --max-output-chars <n>               Default: 512");
        Console.WriteLine("  --report-dir <path>                  Write summary.json and summary.csv to a directory.");
        Console.WriteLine("  --output <text|json>                 Default: text");
        Console.WriteLine();
        Console.WriteLine("Verification options:");
        Console.WriteLine("  --repo-root <path>                   Default: current directory");
        Console.WriteLine("  --solution <path>                    Default: McpServer.slnx");
        Console.WriteLine("  --configuration <name>               Default: Release");
        Console.WriteLine("  --dotnet <path>                      Default: dotnet");
        Console.WriteLine();
        Console.WriteLine("Common options:");
        Console.WriteLine("  --router-base-url <url>              Default: http://127.0.0.1:5177");
        Console.WriteLine("  --chat-model <model>                 Default: fast-local");
        Console.WriteLine("  --report-root <path>                 Default: workspace/artifacts/stress-runs");
        Console.WriteLine("  --timeout-seconds <seconds>          Default: 120");
        Console.WriteLine("  --output <text|json>                Default: text");
        Console.WriteLine();
        Console.WriteLine("Stress options:");
        Console.WriteLine("  --chat-requests <n>                  Default: 12");
        Console.WriteLine("  --chat-concurrency <n>               Default: 3");
        Console.WriteLine("  --agent-run-requests <n>             Default: 6");
        Console.WriteLine("  --agent-run-concurrency <n>          Default: 2");
        Console.WriteLine("  --agent-loop-requests <n>            Default: 6");
        Console.WriteLine("  --agent-loop-concurrency <n>         Default: 2");
        Console.WriteLine("  --mcp-catalog-requests <n>           Default: 20");
        Console.WriteLine("  --mcp-catalog-concurrency <n>        Default: 4");
        Console.WriteLine("  --mcp-tool-call-requests <n>         Default: 12");
        Console.WriteLine("  --mcp-tool-call-concurrency <n>      Default: 3");
        Console.WriteLine("  --enable-mcp-default-tool-coverage   Run the full default MCP tool suite once.");
        Console.WriteLine("  --shell-exec-requests <n>            Default: 6");
        Console.WriteLine("  --shell-exec-concurrency <n>         Default: 2");
        Console.WriteLine("  --enable-ssh                         Enable opt-in SSH workload.");
        Console.WriteLine("  --ssh-profile <name>                 Required when --enable-ssh is set.");
        Console.WriteLine("  --ssh-command <command>              Default: whoami");
        Console.WriteLine("  --ssh-working-directory <path>       Default: /tmp");
        Console.WriteLine("  --ssh-exec-requests <n>              Default: 3");
        Console.WriteLine("  --ssh-exec-concurrency <n>           Default: 1");
        Console.WriteLine("  --skip-chat");
        Console.WriteLine("  --skip-agent-runs");
        Console.WriteLine("  --skip-agent-loops");
        Console.WriteLine("  --skip-mcp-catalog");
        Console.WriteLine("  --skip-mcp-tool-calls");
        Console.WriteLine("  --skip-shell-exec");
        Console.WriteLine();
        Console.WriteLine("Provider-unavailable options:");
        Console.WriteLine("  --strict                            Return non-zero when provider is still reachable; default treats that as skipped/precondition-not-met.");
    }
}

internal static class JsonOptions
{
    public static JsonSerializerOptions CreateIndented()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
    }

    public static JsonSerializerOptions CreateCompact()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }
}
