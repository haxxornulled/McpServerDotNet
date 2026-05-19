using McpServer.AgentRouter.Application.Runtime;
using McpServer.AgentRouter.Application.Ssh;
using McpServer.AgentRouter.Domain.Inference;

namespace McpServer.AgentRouter.UnitTests;

internal static class TestRuntimeSettings
{
    public static AgentRouterRuntimeSettings Create(
        string defaultProfile = "local-code",
        IReadOnlyDictionary<string, ModelProfile>? modelProfiles = null,
        AgentRunStorageRuntimeSettings? runStorage = null,
        McpServerClientRuntimeSettings? mcpServer = null,
        AgentLoopRuntimeSettings? agentLoop = null,
        McpToolExecutionRuntimeSettings? toolExecution = null,
        ShellExecutionRuntimeSettings? shellExecution = null,
        SshExecutionRuntimeSettings? sshExecution = null)
    {
        return new AgentRouterRuntimeSettings
        {
            DefaultProfile = defaultProfile,
            AllowCloudProviders = false,
            ModelProfiles = modelProfiles ?? CreateDefaultProfiles(),
            RunStorage = runStorage ?? new AgentRunStorageRuntimeSettings
            {
                RootPath = Path.Combine("workspace", "artifacts", "agent-runs"),
                WriteArtifactFiles = true
            },
            McpServer = mcpServer ?? new McpServerClientRuntimeSettings
            {
                Enabled = false,
                ExecutablePath = Path.Combine("workspace", "bin", "fake-mcp-server"),
                WorkingDirectory = ".",
                WorkspaceRoot = "workspace",
                TimeoutSeconds = 20,
                DisableHighRiskTools = true,
                Environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            },
            AgentLoop = agentLoop ?? new AgentLoopRuntimeSettings
            {
                Enabled = true,
                MaxSteps = 8,
                MaxToolCalls = 20,
                MaxRuntimeSeconds = 300,
                RequireExplicitAllowlist = true,
                AllowedCapabilities = ["mcp.tools.call", "shell.exec", "ssh.exec", "fake.observe", "fake.validate"],
                WriteTraceFiles = false,
                TraceEveryStep = false,
                TraceRootPath = Path.Combine("workspace", "artifacts", "agent-loops")
            },
            ToolExecution = toolExecution ?? new McpToolExecutionRuntimeSettings
            {
                Enabled = true,
                RequireExplicitAllowlist = true,
                TimeoutSeconds = 20,
                MaxOutputChars = 200000,
                AllowedTools = new HashSet<string>(["activity.schemas.list", "fs.get_metadata", "fs.list_directory"], StringComparer.OrdinalIgnoreCase),
                WriteTraceFiles = false,
                TraceRootPath = Path.Combine("workspace", "artifacts", "mcp-tool-calls")
            },
            ShellExecution = shellExecution ?? new ShellExecutionRuntimeSettings
            {
                Enabled = true,
                RequireExplicitAllowlist = true,
                TimeoutSeconds = 60,
                MaxOutputChars = 200000,
                WorkingDirectoryRoot = "workspace",
                AllowWorkingDirectoryOutsideRoot = false,
                AllowShellInterpreterInlineCommands = false,
                AllowedCommands = new HashSet<string>(["dotnet", "git", "dir", "bash"], StringComparer.OrdinalIgnoreCase),
                DeniedCommands = new HashSet<string>(["rm", "sudo"], StringComparer.OrdinalIgnoreCase),
                WriteTraceFiles = false,
                TraceRootPath = Path.Combine("workspace", "artifacts", "shell-exec")
            },
            SshExecution = sshExecution ?? new SshExecutionRuntimeSettings
            {
                Enabled = true,
                RequireExplicitProfileAllowlist = true,
                TimeoutSeconds = 60,
                MaxOutputChars = 200000,
                AllowUnknownHostKeys = false,
                AllowShellInterpreterInlineCommands = false,
                AllowedCommands = new HashSet<string>(["pwd", "whoami"], StringComparer.OrdinalIgnoreCase),
                DeniedCommands = new HashSet<string>(["rm", "sudo"], StringComparer.OrdinalIgnoreCase),
                WriteTraceFiles = false,
                TraceRootPath = Path.Combine("workspace", "artifacts", "ssh-exec"),
                LoadRepoProfilesFile = false,
                RepoProfilesFilePath = string.Empty,
                LoadUserProfilesFile = false,
                UserProfilesFilePath = string.Empty,
                AllowInlineProfiles = false,
                VaultPath = Path.Combine("workspace", "artifacts", "ssh-vault.json"),
                VaultKeyPath = Path.Combine("workspace", "artifacts", "ssh-vault.key"),
                Profiles = new Dictionary<string, SshProfileDefinition>(StringComparer.OrdinalIgnoreCase)
            }
        };
    }

    private static IReadOnlyDictionary<string, ModelProfile> CreateDefaultProfiles()
    {
        return new Dictionary<string, ModelProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["local-code"] = new(
                name: "local-code",
                provider: "Ollama",
                model: "qwen3-coder:30b",
                baseUri: new Uri("http://127.0.0.1:11434/"),
                contextLength: 131072,
                maxOutputTokens: 32000,
                temperature: 0.15d,
                allowCloudProvider: false,
                allowNonLoopbackBaseUrl: false,
                timeoutSeconds: 900),
            ["fast-local"] = new(
                name: "fast-local",
                provider: "Ollama",
                model: "qwen2.5-coder:14b",
                baseUri: new Uri("http://127.0.0.1:11434/"),
                contextLength: 32768,
                maxOutputTokens: 12000,
                temperature: 0.15d,
                allowCloudProvider: false,
                allowNonLoopbackBaseUrl: false,
                timeoutSeconds: 900)
        };
    }
}
