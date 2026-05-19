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

    public bool HasFlag(string name)
    {
        return _values.ContainsKey(name);
    }
}

internal static class ConsoleWriter
{
    public static void WriteSection(string name)
    {
        Log.Information(string.Empty);
        Log.Information("============================================================");
        Log.Information(name);
        Log.Information("============================================================");
    }

    public static void WritePass(string message)
    {
        Log.Information("[PASS] {Message}", message);
    }

    public static void WriteInfo(string message)
    {
        Log.Information("[INFO] {Message}", message);
    }

    public static void WriteWarning(string message)
    {
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
        Console.WriteLine("  dotnet run --project tools/McpServer.AgentRouter.Tools -- smoke [options]");
        Console.WriteLine("  dotnet run --project tools/McpServer.AgentRouter.Tools -- stress [options]");
        Console.WriteLine("  dotnet run --project tools/McpServer.AgentRouter.Tools -- provider-unavailable [options]");
        Console.WriteLine("  smoke runs the high-fidelity harness, including default MCP tool coverage.");
        Console.WriteLine();
        Console.WriteLine("Common options:");
        Console.WriteLine("  --router-base-url <url>              Default: http://127.0.0.1:5177");
        Console.WriteLine("  --chat-model <model>                 Default: fast-local");
        Console.WriteLine("  --report-root <path>                 Default: workspace/artifacts/stress-runs");
        Console.WriteLine("  --timeout-seconds <seconds>          Default: 120");
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
