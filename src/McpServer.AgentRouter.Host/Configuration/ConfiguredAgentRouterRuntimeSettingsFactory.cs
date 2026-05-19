using McpServer.AgentRouter.Application.Runtime;
using McpServer.AgentRouter.Application.Ssh;
using McpServer.AgentRouter.Domain.Inference;

namespace McpServer.AgentRouter.Host.Configuration;

internal static class ConfiguredAgentRouterRuntimeSettingsFactory
{
    public static AgentRouterRuntimeSettings Create(AgentRouterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new AgentRouterRuntimeSettings
        {
            DefaultProfile = string.IsNullOrWhiteSpace(options.DefaultProfile)
                ? throw new InvalidOperationException("AgentRouter:DefaultProfile must be configured.")
                : options.DefaultProfile.Trim(),
            AllowCloudProviders = options.AllowCloudProviders,
            ModelProfiles = BuildProfiles(options),
            RunStorage = new AgentRunStorageRuntimeSettings
            {
                RootPath = string.IsNullOrWhiteSpace(options.RunStorage.RootPath)
                    ? Path.Combine("workspace", "artifacts", "agent-runs")
                    : options.RunStorage.RootPath.Trim(),
                WriteArtifactFiles = options.RunStorage.WriteArtifactFiles
            },
            McpServer = new McpServerClientRuntimeSettings
            {
                Enabled = options.McpServer.Enabled,
                ExecutablePath = string.IsNullOrWhiteSpace(options.McpServer.ExecutablePath)
                    ? throw new InvalidOperationException("AgentRouter:McpServer:ExecutablePath is required.")
                    : options.McpServer.ExecutablePath.Trim(),
                WorkingDirectory = string.IsNullOrWhiteSpace(options.McpServer.WorkingDirectory)
                    ? throw new InvalidOperationException("AgentRouter:McpServer:WorkingDirectory is required.")
                    : options.McpServer.WorkingDirectory.Trim(),
                WorkspaceRoot = string.IsNullOrWhiteSpace(options.McpServer.WorkspaceRoot)
                    ? throw new InvalidOperationException("AgentRouter:McpServer:WorkspaceRoot is required.")
                    : options.McpServer.WorkspaceRoot.Trim(),
                TimeoutSeconds = Math.Clamp(options.McpServer.TimeoutSeconds, 1, 300),
                DisableHighRiskTools = options.McpServer.DisableHighRiskTools,
                Environment = NormalizeEnvironment(options.McpServer.Environment)
            },
            AgentLoop = new AgentLoopRuntimeSettings
            {
                Enabled = options.AgentLoop.Enabled,
                MaxSteps = Math.Max(1, options.AgentLoop.MaxSteps),
                MaxToolCalls = Math.Max(0, options.AgentLoop.MaxToolCalls),
                MaxRuntimeSeconds = Math.Max(1, options.AgentLoop.MaxRuntimeSeconds),
                RequireExplicitAllowlist = options.AgentLoop.RequireExplicitAllowlist,
                AllowedCapabilities = NormalizeList(options.AgentLoop.AllowedCapabilities),
                WriteTraceFiles = options.AgentLoop.WriteTraceFiles,
                TraceEveryStep = options.AgentLoop.TraceEveryStep,
                TraceRootPath = string.IsNullOrWhiteSpace(options.AgentLoop.TraceRootPath)
                    ? Path.Combine("workspace", "artifacts", "agent-loops")
                    : options.AgentLoop.TraceRootPath.Trim()
            },
            ToolExecution = new McpToolExecutionRuntimeSettings
            {
                Enabled = options.ToolExecution.Enabled,
                RequireExplicitAllowlist = options.ToolExecution.RequireExplicitAllowlist,
                TimeoutSeconds = Math.Clamp(options.ToolExecution.TimeoutSeconds, 1, 300),
                MaxOutputChars = Math.Clamp(options.ToolExecution.MaxOutputChars, 1024, 1_000_000),
                AllowedTools = NormalizeSet(options.ToolExecution.AllowedTools),
                WriteTraceFiles = options.ToolExecution.WriteTraceFiles,
                TraceRootPath = string.IsNullOrWhiteSpace(options.ToolExecution.TraceRootPath)
                    ? Path.Combine("workspace", "artifacts", "mcp-tool-calls")
                    : options.ToolExecution.TraceRootPath.Trim()
            },
            ShellExecution = new ShellExecutionRuntimeSettings
            {
                Enabled = options.ShellExecution.Enabled,
                RequireExplicitAllowlist = options.ShellExecution.RequireExplicitAllowlist,
                TimeoutSeconds = Math.Max(1, options.ShellExecution.TimeoutSeconds),
                MaxOutputChars = Math.Max(1, options.ShellExecution.MaxOutputChars),
                WorkingDirectoryRoot = string.IsNullOrWhiteSpace(options.ShellExecution.WorkingDirectoryRoot)
                    ? "workspace"
                    : options.ShellExecution.WorkingDirectoryRoot.Trim(),
                AllowWorkingDirectoryOutsideRoot = options.ShellExecution.AllowWorkingDirectoryOutsideRoot,
                AllowShellInterpreterInlineCommands = options.ShellExecution.AllowShellInterpreterInlineCommands,
                AllowedCommands = NormalizeSet(options.ShellExecution.AllowedCommands),
                DeniedCommands = NormalizeSet(options.ShellExecution.DeniedCommands),
                WriteTraceFiles = options.ShellExecution.WriteTraceFiles,
                TraceRootPath = string.IsNullOrWhiteSpace(options.ShellExecution.TraceRootPath)
                    ? Path.Combine("workspace", "artifacts", "shell-exec")
                    : options.ShellExecution.TraceRootPath.Trim()
            },
            SshExecution = new SshExecutionRuntimeSettings
            {
                Enabled = options.SshExecution.Enabled,
                RequireExplicitProfileAllowlist = options.SshExecution.RequireExplicitProfileAllowlist,
                TimeoutSeconds = Math.Max(1, options.SshExecution.TimeoutSeconds),
                MaxOutputChars = Math.Max(1, options.SshExecution.MaxOutputChars),
                AllowUnknownHostKeys = options.SshExecution.AllowUnknownHostKeys,
                AllowShellInterpreterInlineCommands = options.SshExecution.AllowShellInterpreterInlineCommands,
                AllowedCommands = NormalizeSet(options.SshExecution.AllowedCommands),
                DeniedCommands = NormalizeSet(options.SshExecution.DeniedCommands),
                WriteTraceFiles = options.SshExecution.WriteTraceFiles,
                TraceRootPath = string.IsNullOrWhiteSpace(options.SshExecution.TraceRootPath)
                    ? Path.Combine("workspace", "artifacts", "ssh-exec")
                    : options.SshExecution.TraceRootPath.Trim(),
                LoadRepoProfilesFile = options.SshExecution.LoadRepoProfilesFile,
                RepoProfilesFilePath = string.IsNullOrWhiteSpace(options.SshExecution.RepoProfilesFilePath)
                    ? Path.Combine("..", "..", "config", "mcpserver", "ssh-profiles.local.json")
                    : options.SshExecution.RepoProfilesFilePath.Trim(),
                LoadUserProfilesFile = options.SshExecution.LoadUserProfilesFile,
                UserProfilesFilePath = string.IsNullOrWhiteSpace(options.SshExecution.UserProfilesFilePath)
                    ? string.Empty
                    : options.SshExecution.UserProfilesFilePath.Trim(),
                AllowInlineProfiles = options.SshExecution.AllowInlineProfiles,
                VaultPath = string.IsNullOrWhiteSpace(options.SshExecution.VaultPath)
                    ? Path.Combine("..", "..", "config", "mcpserver", "ssh-vault.local.json")
                    : options.SshExecution.VaultPath.Trim(),
                VaultKeyPath = string.IsNullOrWhiteSpace(options.SshExecution.VaultKeyPath)
                    ? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "McpServer",
                        "ssh-vault.key")
                    : options.SshExecution.VaultKeyPath.Trim(),
                Profiles = BuildSshProfiles(options)
            }
        };
    }

    private static IReadOnlyDictionary<string, ModelProfile> BuildProfiles(AgentRouterOptions options)
    {
        var profiles = new Dictionary<string, ModelProfile>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in options.ModelProfiles)
        {
            var profileName = pair.Key?.Trim();
            if (string.IsNullOrWhiteSpace(profileName))
            {
                continue;
            }

            profiles[profileName] = CreateProfile(profileName, pair.Value, options.AllowCloudProviders);
        }

        return profiles;
    }

    private static IReadOnlyDictionary<string, SshProfileDefinition> BuildSshProfiles(AgentRouterOptions options)
    {
        var profiles = new Dictionary<string, SshProfileDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in options.SshExecution.Profiles)
        {
            var profileName = pair.Key?.Trim();
            if (string.IsNullOrWhiteSpace(profileName) || pair.Value is null)
            {
                continue;
            }

            profiles[profileName] = CreateProfile(pair.Value);
        }

        return profiles;
    }

    private static ModelProfile CreateProfile(
        string name,
        ModelProfileOptions profileOptions,
        bool allowCloudProviders)
    {
        if (profileOptions is null)
        {
            throw new InvalidOperationException($"Model profile '{name}' is empty.");
        }

        if (string.IsNullOrWhiteSpace(profileOptions.Model))
        {
            throw new InvalidOperationException($"Model profile '{name}' must configure a model.");
        }

        var baseUri = CreateBaseUri(name, profileOptions.BaseUrl);

        if (profileOptions.AllowCloudProvider && !allowCloudProviders)
        {
            throw new InvalidOperationException(
                $"Model profile '{name}' is marked as cloud-backed, but AgentRouter:AllowCloudProviders is false.");
        }

        if (!profileOptions.AllowNonLoopbackBaseUrl && !baseUri.IsLoopback)
        {
            throw new InvalidOperationException(
                $"Model profile '{name}' BaseUrl must be loopback unless AllowNonLoopbackBaseUrl is explicitly enabled.");
        }

        return new ModelProfile(
            name: name,
            provider: string.IsNullOrWhiteSpace(profileOptions.Provider) ? "Ollama" : profileOptions.Provider.Trim(),
            model: profileOptions.Model,
            baseUri: baseUri,
            contextLength: profileOptions.ContextLength,
            maxOutputTokens: profileOptions.MaxOutputTokens,
            temperature: profileOptions.Temperature,
            allowCloudProvider: profileOptions.AllowCloudProvider,
            allowNonLoopbackBaseUrl: profileOptions.AllowNonLoopbackBaseUrl,
            timeoutSeconds: profileOptions.TimeoutSeconds);
    }

    private static SshProfileDefinition CreateProfile(SshProfileOptions source)
    {
        return new SshProfileDefinition
        {
            Host = source.Host,
            Port = source.Port,
            Username = source.Username,
            PrivateKeyPath = source.PrivateKeyPath,
            PasswordVaultItemName = source.PasswordVaultItemName,
            PrivateKeyPassphraseVaultItemName = source.PrivateKeyPassphraseVaultItemName,
            WorkingDirectory = source.WorkingDirectory,
            HostKeySha256 = source.HostKeySha256,
            AcceptUnknownHostKey = source.AcceptUnknownHostKey,
            AllowedCommands = source.AllowedCommands.ToList(),
            DeniedCommands = source.DeniedCommands.ToList(),
            AllowedRemotePathPrefixes = source.AllowedRemotePathPrefixes.ToList(),
            AllowSudoCommand = source.AllowSudoCommand
        };
    }

    private static IReadOnlyDictionary<string, string?> NormalizeEnvironment(
        IDictionary<string, string?> values)
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            environment[pair.Key.Trim().Replace(":", "__", StringComparison.Ordinal)] = pair.Value;
        }

        return environment;
    }

    private static Uri CreateBaseUri(string profileName, string? value)
    {
        var candidate = string.IsNullOrWhiteSpace(value)
            ? "http://127.0.0.1:11434/"
            : value.Trim();

        if (!candidate.EndsWith("/", StringComparison.Ordinal))
        {
            candidate += "/";
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException($"Model profile '{profileName}' has invalid BaseUrl: Value is not an absolute URI.");
        }

        if (baseUri.Scheme is not "http" and not "https")
        {
            throw new InvalidOperationException($"Model profile '{profileName}' has invalid BaseUrl: Only HTTP and HTTPS are supported.");
        }

        return baseUri;
    }

    private static IReadOnlyList<string> NormalizeList(IEnumerable<string> values)
    {
        return NormalizeSet(values).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlySet<string> NormalizeSet(IEnumerable<string> values)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                set.Add(value.Trim());
            }
        }

        return set;
    }
}
