using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace McpServer.AgentRouter.Tools;

internal sealed class LocalMcpClientConfigSettings
{
    public string RepositoryRootPath { get; init; } = Directory.GetCurrentDirectory();

    public bool BuildSolution { get; init; }

    public string SolutionPath { get; init; } = "McpServer.slnx";

    public string DefaultModel { get; init; } = "qwen3-coder:30b";

    public IReadOnlyList<string> AllowedModels { get; init; } = new[] { "qwen3-coder:30b", "qwen2.5-coder:14b", "devstral-small-2" };

    public string OllamaBaseUrl { get; init; } = "http://127.0.0.1:11434";

    public int ContextLength { get; init; } = 131072;

    public int NumPredict { get; init; } = 32000;

    public int MaxPromptChars { get; init; } = 500000;

    public int MaxOutputChars { get; init; } = 32000;

    public int ToolTimeoutSeconds { get; init; } = 900;

    public static LocalMcpClientConfigSettings FromOptions(CommandLineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var allowedModels = options
            .GetString("allowed-models", "qwen3-coder:30b,qwen2.5-coder:14b,devstral-small-2")
            .Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new LocalMcpClientConfigSettings
        {
            RepositoryRootPath = options.GetString("repo-root", Directory.GetCurrentDirectory()),
            BuildSolution = options.HasFlag("build"),
            SolutionPath = options.GetString("solution", "McpServer.slnx"),
            DefaultModel = options.GetString("default-model", "qwen3-coder:30b"),
            AllowedModels = allowedModels.Length == 0
                ? new[] { "qwen3-coder:30b", "qwen2.5-coder:14b", "devstral-small-2" }
                : allowedModels,
            OllamaBaseUrl = options.GetString("ollama-base-url", "http://127.0.0.1:11434"),
            ContextLength = options.GetInt("context-length", 131072),
            NumPredict = options.GetInt("num-predict", 32000),
            MaxPromptChars = options.GetInt("max-prompt-chars", 500000),
            MaxOutputChars = options.GetInt("max-output-chars", 32000),
            ToolTimeoutSeconds = options.GetInt("tool-timeout-seconds", 900)
        };
    }
}

internal sealed class LocalMcpClientConfigRunner
{
    private readonly LocalMcpClientConfigSettings _settings;

    public LocalMcpClientConfigRunner(LocalMcpClientConfigSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var repoRoot = Path.GetFullPath(_settings.RepositoryRootPath);
        var hostExe = GetHostExecutablePath(repoRoot);
        var codexPath = Path.Combine(repoRoot, ".codex", "config.toml");
        var vscodePath = Path.Combine(repoRoot, ".vscode", "mcp.json");
        var mcpPath = Path.Combine(repoRoot, ".mcp.json");
        var logsPath = Path.Combine(repoRoot, "logs");

        ConsoleWriter.WriteSection("Local MCP client config generation");
        ConsoleWriter.WriteInfo($"Repo root: {repoRoot}");
        ConsoleWriter.WriteInfo($"Host executable: {hostExe}");
        ConsoleWriter.WriteInfo($"Build solution: {_settings.BuildSolution}");

        if (_settings.BuildSolution)
        {
            if (await ProcessRunner.RunAsync(
                    "dotnet",
                    new[]
                    {
                        "build",
                        _settings.SolutionPath,
                        "-c",
                        "Release",
                        "-v",
                        "minimal"
                    },
                    repoRoot,
                    cancellationToken).ConfigureAwait(false) != 0)
            {
                ConsoleWriter.WriteError("dotnet build failed.");
                return 1;
            }
        }

        if (!File.Exists(hostExe))
        {
            ConsoleWriter.WriteWarning($"Host executable was not found at '{hostExe}'. Run with --build or build the solution first.");
        }

        Directory.CreateDirectory(Path.Combine(repoRoot, ".codex"));
        Directory.CreateDirectory(Path.Combine(repoRoot, ".vscode"));
        Directory.CreateDirectory(logsPath);

        var env = CreateEnvironment(repoRoot, logsPath);
        var server = new Dictionary<string, object?>
        {
            ["type"] = "stdio",
            ["command"] = hostExe,
            ["args"] = Array.Empty<string>(),
            ["cwd"] = repoRoot,
            ["env"] = env
        };

        WriteCodexConfig(codexPath, hostExe, repoRoot, env, _settings.ToolTimeoutSeconds);
        WriteJson(vscodePath, new Dictionary<string, object?>
        {
            ["servers"] = new Dictionary<string, object?> { ["mcpserver"] = server }
        });
        WriteJson(mcpPath, new Dictionary<string, object?>
        {
            ["inputs"] = Array.Empty<object?>(),
            ["servers"] = new Dictionary<string, object?> { ["mcpserver"] = server }
        });

        ConsoleWriter.WritePass($"Generated Codex config: {codexPath}");
        ConsoleWriter.WritePass($"Generated VS Code MCP: {vscodePath}");
        ConsoleWriter.WritePass($"Generated generic MCP: {mcpPath}");
        ConsoleWriter.WriteInfo($"Default local model: {_settings.DefaultModel}");
        ConsoleWriter.WriteInfo($"Context length: {_settings.ContextLength}");
        return 0;
    }

    private static string GetHostExecutablePath(string repoRoot)
    {
        var fileName = OperatingSystem.IsWindows() ? "McpServer.Host.exe" : "McpServer.Host";
        return Path.Combine(repoRoot, "src", "McpServer.Host", "bin", "Release", "net10.0", fileName);
    }

    private Dictionary<string, string> CreateEnvironment(string repoRoot, string logsPath)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["MCPSERVER__WORKSPACE__ROOTPATH"] = repoRoot,
            ["MCPSERVER__WORKSPACE__ALLOWEDROOTS__0"] = repoRoot,
            ["MCPSERVER__WORKSPACE__ALLOWRUNTIMEWORKSPACEOPEN"] = "true",
            ["MCPSERVER__SHELL__ENABLED"] = "true",
            ["MCPSERVER__SHELL__ALLOWEDSHELLFALLBACK"] = "false",
            ["MCPSERVER__SHELL__ALLOWWINDOWSCOMPATIBILITYSHELL"] = "false",
            ["MCPSERVER__SHELL__ALLOWEDCOMMANDS__0"] = "dotnet",
            ["MCPSERVER__SHELL__ALLOWEDCOMMANDS__1"] = "git",
            ["MCPSERVER__SHELL__MAXTIMEOUTSECONDS"] = "300",
            ["MCPSERVER__SHELL__MAXOUTPUTCHARS"] = "200000",
            ["MCPSERVER__WEBACCESS__ENABLED"] = "false",
            ["MCPSERVER__SSH__ENABLED"] = "false",
            ["MCPSERVER__OLLAMA__ENABLED"] = "true",
            ["MCPSERVER__OLLAMA__BASEURL"] = _settings.OllamaBaseUrl,
            ["MCPSERVER__OLLAMA__DEFAULTMODEL"] = _settings.DefaultModel,
            ["MCPSERVER__OLLAMA__TIMEOUTSECONDS"] = "240",
            ["MCPSERVER__OLLAMA__MAXTIMEOUTSECONDS"] = _settings.ToolTimeoutSeconds.ToString(),
            ["MCPSERVER__OLLAMA__MAXPROMPTCHARS"] = _settings.MaxPromptChars.ToString(),
            ["MCPSERVER__OLLAMA__MAXOUTPUTCHARS"] = _settings.MaxOutputChars.ToString(),
            ["MCPSERVER__OLLAMA__CONTEXTLENGTH"] = _settings.ContextLength.ToString(),
            ["MCPSERVER__OLLAMA__NUMPREDICT"] = _settings.NumPredict.ToString(),
            ["MCPSERVER__OLLAMA__TEMPERATURE"] = "0.15",
            ["MCPSERVER__OLLAMA__ALLOWNONLOOPBACKBASEURL"] = "false",
            ["MCPSERVER_LOG_DIRECTORY"] = logsPath
        };

        for (var index = 0; index < _settings.AllowedModels.Count; index++)
        {
            env[$"MCPSERVER__OLLAMA__ALLOWEDMODELS__{index}"] = _settings.AllowedModels[index];
        }

        return env;
    }

    private static void WriteCodexConfig(string codexPath, string hostExe, string repoRoot, Dictionary<string, string> env, int toolTimeoutSeconds)
    {
        var disabledTools = new[]
        {
            "fs.delete_path",
            "ssh.execute",
            "web.fetch_url",
            "web.search",
            "web.scrape_url"
        };

        var lines = new List<string>
        {
            "# Generated by tools/McpServer.AgentRouter.Tools -- install-local-clients",
            "# Repo-local Codex MCP configuration for MCPServer.",
            string.Empty,
            "[mcp_servers.mcpserver]",
            "command = " + ToTomlLiteral(hostExe),
            "args = []",
            "cwd = " + ToTomlLiteral(repoRoot),
            "startup_timeout_sec = 30",
            "tool_timeout_sec = " + toolTimeoutSeconds,
            "enabled = true",
            "required = true",
            "disabled_tools = ["
        };

        foreach (var tool in disabledTools)
        {
            lines.Add("  " + ToTomlLiteral(tool) + ",");
        }

        lines.Add("]");
        lines.Add(string.Empty);
        lines.Add("[mcp_servers.mcpserver.env]");

        foreach (var pair in env)
        {
            lines.Add(pair.Key + " = " + ToTomlLiteral(pair.Value));
        }

        File.WriteAllText(codexPath, string.Join(Environment.NewLine, lines), System.Text.Encoding.UTF8);
    }

    private static void WriteJson(string path, object value)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(path, json, System.Text.Encoding.UTF8);
    }

    private static string ToTomlLiteral(string value)
    {
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }
}
