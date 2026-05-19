using System;
using System.IO;

namespace McpServer.AgentRouter.Tools;

internal sealed class StressSettings
{
    public Uri RouterBaseUrl { get; init; } = new("http://127.0.0.1:5177");

    public string ChatModel { get; init; } = "fast-local";

    public string ReportRootPath { get; init; } = Path.Combine("workspace", "artifacts", "stress-runs");

    public int ChatRequests { get; init; } = 12;

    public int ChatConcurrency { get; init; } = 3;

    public int AgentRunRequests { get; init; } = 6;

    public int AgentRunConcurrency { get; init; } = 2;

    public int AgentLoopRequests { get; init; } = 6;

    public int AgentLoopConcurrency { get; init; } = 2;

    public int McpCatalogRequests { get; init; } = 20;

    public int McpCatalogConcurrency { get; init; } = 4;

    public int McpToolCallRequests { get; init; } = 12;

    public int McpToolCallConcurrency { get; init; } = 3;

    public bool EnableMcpDefaultToolCoverage { get; init; }

    public int ShellExecutionRequests { get; init; } = 6;

    public int ShellExecutionConcurrency { get; init; } = 2;

    public bool EnableSshExecution { get; init; }

    public int SshExecutionRequests { get; init; } = 3;

    public int SshExecutionConcurrency { get; init; } = 1;

    public string SshProfile { get; init; } = string.Empty;

    public string SshCommand { get; init; } = "whoami";

    public string SshWorkingDirectory { get; init; } = "/tmp";

    public int TimeoutSeconds { get; init; } = 120;

    public bool SkipChat { get; init; }

    public bool SkipAgentRuns { get; init; }

    public bool SkipAgentLoops { get; init; }

    public bool SkipMcpCatalog { get; init; }

    public bool SkipMcpToolCalls { get; init; }

    public bool SkipShellExecution { get; init; }

    public static StressSettings FromOptions(CommandLineOptions options)
    {
        return new StressSettings
        {
            RouterBaseUrl = new Uri(options.GetString("router-base-url", "http://127.0.0.1:5177")),
            ChatModel = options.GetString("chat-model", "fast-local"),
            ReportRootPath = options.GetString("report-root", Path.Combine("workspace", "artifacts", "stress-runs")),
            ChatRequests = options.GetInt("chat-requests", 12),
            ChatConcurrency = options.GetInt("chat-concurrency", 3),
            AgentRunRequests = options.GetInt("agent-run-requests", 6),
            AgentRunConcurrency = options.GetInt("agent-run-concurrency", 2),
            AgentLoopRequests = options.GetInt("agent-loop-requests", 6),
            AgentLoopConcurrency = options.GetInt("agent-loop-concurrency", 2),
            McpCatalogRequests = options.GetInt("mcp-catalog-requests", 20),
            McpCatalogConcurrency = options.GetInt("mcp-catalog-concurrency", 4),
            McpToolCallRequests = options.GetInt("mcp-tool-call-requests", 12),
            McpToolCallConcurrency = options.GetInt("mcp-tool-call-concurrency", 3),
            EnableMcpDefaultToolCoverage = options.HasFlag("enable-mcp-default-tool-coverage"),
            ShellExecutionRequests = options.GetInt("shell-exec-requests", 6),
            ShellExecutionConcurrency = options.GetInt("shell-exec-concurrency", 2),
            EnableSshExecution = options.HasFlag("enable-ssh"),
            SshExecutionRequests = options.GetInt("ssh-exec-requests", 3),
            SshExecutionConcurrency = options.GetInt("ssh-exec-concurrency", 1),
            SshProfile = options.GetString("ssh-profile", string.Empty),
            SshCommand = options.GetString("ssh-command", "whoami"),
            SshWorkingDirectory = options.GetString("ssh-working-directory", "/tmp"),
            TimeoutSeconds = options.GetInt("timeout-seconds", 120),
            SkipChat = options.HasFlag("skip-chat"),
            SkipAgentRuns = options.HasFlag("skip-agent-runs"),
            SkipAgentLoops = options.HasFlag("skip-agent-loops"),
            SkipMcpCatalog = options.HasFlag("skip-mcp-catalog"),
            SkipMcpToolCalls = options.HasFlag("skip-mcp-tool-calls"),
            SkipShellExecution = options.HasFlag("skip-shell-exec")
        }.Validate();
    }

    public StressSettings AsSmokeProfile()
    {
        return new StressSettings
        {
            RouterBaseUrl = RouterBaseUrl,
            ChatModel = ChatModel,
            ReportRootPath = ReportRootPath,
            TimeoutSeconds = TimeoutSeconds,
            ChatRequests = SkipChat ? 0 : 1,
            ChatConcurrency = 1,
            AgentRunRequests = SkipAgentRuns ? 0 : 1,
            AgentRunConcurrency = 1,
            AgentLoopRequests = SkipAgentLoops ? 0 : 1,
            AgentLoopConcurrency = 1,
            McpCatalogRequests = SkipMcpCatalog ? 0 : 1,
            McpCatalogConcurrency = 1,
            McpToolCallRequests = 0,
            McpToolCallConcurrency = 1,
            EnableMcpDefaultToolCoverage = true,
            ShellExecutionRequests = SkipShellExecution ? 0 : 1,
            ShellExecutionConcurrency = 1,
            EnableSshExecution = EnableSshExecution,
            SshExecutionRequests = EnableSshExecution ? 1 : 0,
            SshExecutionConcurrency = 1,
            SshProfile = SshProfile,
            SshCommand = SshCommand,
            SshWorkingDirectory = SshWorkingDirectory,
            SkipChat = SkipChat,
            SkipAgentRuns = SkipAgentRuns,
            SkipAgentLoops = SkipAgentLoops,
            SkipMcpCatalog = SkipMcpCatalog,
            SkipMcpToolCalls = SkipMcpToolCalls,
            SkipShellExecution = SkipShellExecution
        }.Validate();
    }

    public StressSettings AsProviderUnavailableProfile()
    {
        return new StressSettings
        {
            RouterBaseUrl = RouterBaseUrl,
            ChatModel = ChatModel,
            ReportRootPath = ReportRootPath,
            TimeoutSeconds = TimeoutSeconds
        };
    }

    private StressSettings Validate()
    {
        ThrowIfNegative(ChatRequests, nameof(ChatRequests));
        ThrowIfNegative(AgentRunRequests, nameof(AgentRunRequests));
        ThrowIfNegative(AgentLoopRequests, nameof(AgentLoopRequests));
        ThrowIfNegative(McpCatalogRequests, nameof(McpCatalogRequests));
        ThrowIfNegative(McpToolCallRequests, nameof(McpToolCallRequests));
        ThrowIfNegative(ShellExecutionRequests, nameof(ShellExecutionRequests));
        ThrowIfNegative(SshExecutionRequests, nameof(SshExecutionRequests));
        ThrowIfLessThanOne(ChatConcurrency, nameof(ChatConcurrency));
        ThrowIfLessThanOne(AgentRunConcurrency, nameof(AgentRunConcurrency));
        ThrowIfLessThanOne(AgentLoopConcurrency, nameof(AgentLoopConcurrency));
        ThrowIfLessThanOne(McpCatalogConcurrency, nameof(McpCatalogConcurrency));
        ThrowIfLessThanOne(McpToolCallConcurrency, nameof(McpToolCallConcurrency));
        ThrowIfLessThanOne(ShellExecutionConcurrency, nameof(ShellExecutionConcurrency));
        ThrowIfLessThanOne(SshExecutionConcurrency, nameof(SshExecutionConcurrency));
        ThrowIfLessThanOne(TimeoutSeconds, nameof(TimeoutSeconds));

        if (EnableSshExecution && string.IsNullOrWhiteSpace(SshProfile))
        {
            throw new ArgumentException("--ssh-profile is required when --enable-ssh is set.");
        }

        return this;
    }

    private static void ThrowIfNegative(int value, string name)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(name, value, $"{name} cannot be negative.");
        }
    }

    private static void ThrowIfLessThanOne(int value, string name)
    {
        if (value < 1)
        {
            throw new ArgumentOutOfRangeException(name, value, $"{name} must be greater than zero.");
        }
    }
}
