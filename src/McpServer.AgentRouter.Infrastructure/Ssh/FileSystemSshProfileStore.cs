using System.Text.Json;
using LanguageExt;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.Ssh;
using McpServer.AgentRouter.Application.Runtime;
using McpServer.AgentRouter.Domain.Ssh;
using Microsoft.Extensions.Logging;

namespace McpServer.AgentRouter.Infrastructure.Ssh;

public sealed class FileSystemSshProfileStore : ISshProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly SshExecutionRuntimeSettings _settings;
    private readonly IAgentRouterRuntimePathResolver _pathResolver;
    private readonly ILogger<FileSystemSshProfileStore> _logger;

    public FileSystemSshProfileStore(
        SshExecutionRuntimeSettings settings,
        IAgentRouterRuntimePathResolver pathResolver,
        ILogger<FileSystemSshProfileStore> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<Fin<SshProfileCatalog>> LoadProfilesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var options = _settings;
            var profiles = new Dictionary<string, SshProfileDefinition>(StringComparer.OrdinalIgnoreCase);
            var sources = new List<SshProfileSourceStatus>();

            if (options.AllowInlineProfiles && options.Profiles.Count > 0)
            {
                MergeProfiles(profiles, options.Profiles);
                sources.Add(new SshProfileSourceStatus
                {
                    SourceName = "inline-appsettings",
                    Path = "AgentRouter:SshExecution:Profiles",
                    Enabled = true,
                    Exists = true,
                    ProfileCount = options.Profiles.Count,
                    Message = "Inline SSH profiles are enabled for legacy compatibility. Prefer external profile files."
                });
            }

            if (options.LoadRepoProfilesFile)
            {
                var repoPath = ResolveConfiguredPath(options.RepoProfilesFilePath);
                await LoadFileAsync(
                        profiles,
                        sources,
                        sourceName: "repo-local",
                        path: repoPath,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                sources.Add(new SshProfileSourceStatus
                {
                    SourceName = "repo-local",
                    Path = ResolveConfiguredPath(options.RepoProfilesFilePath),
                    Enabled = false,
                    Exists = false,
                    Message = "Repo-local SSH profile loading is disabled."
                });
            }

            if (options.LoadUserProfilesFile)
            {
                var userPath = ResolveUserProfilesPath(options.UserProfilesFilePath);
                await LoadFileAsync(
                        profiles,
                        sources,
                        sourceName: "user-level",
                        path: userPath,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                sources.Add(new SshProfileSourceStatus
                {
                    SourceName = "user-level",
                    Path = ResolveUserProfilesPath(options.UserProfilesFilePath),
                    Enabled = false,
                    Exists = false,
                    Message = "User-level SSH profile loading is disabled."
                });
            }

            return Fin<SshProfileCatalog>.Succ(new SshProfileCatalog
            {
                Profiles = profiles,
                Sources = sources
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return LanguageExt.Common.Error.New($"Failed to load SSH profiles: {ex.Message}");
        }
    }

    private async Task LoadFileAsync(
        IDictionary<string, SshProfileDefinition> profiles,
        IList<SshProfileSourceStatus> sources,
        string sourceName,
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            sources.Add(new SshProfileSourceStatus
            {
                SourceName = sourceName,
                Path = path,
                Enabled = true,
                Exists = false,
                ProfileCount = 0,
                Message = "Profile file does not exist."
            });
            return;
        }

        await using var stream = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync<SshProfilesFileDocument>(
                stream,
                JsonOptions,
                cancellationToken)
            .ConfigureAwait(false);

        var documentProfiles = document?.Profiles ?? new Dictionary<string, SshProfileDefinition>(StringComparer.OrdinalIgnoreCase);
        MergeProfiles(profiles, documentProfiles);

        sources.Add(new SshProfileSourceStatus
        {
            SourceName = sourceName,
            Path = path,
            Enabled = true,
            Exists = true,
            ProfileCount = documentProfiles.Count,
            Message = documentProfiles.Count == 0 ? "Profile file exists but contains no profiles." : null
        });

        _logger.LogDebug(
            "Loaded {ProfileCount} SSH profiles from {SourceName} profile file {Path}.",
            documentProfiles.Count,
            sourceName,
            path);
    }

    private static void MergeProfiles(
        IDictionary<string, SshProfileDefinition> target,
        IEnumerable<KeyValuePair<string, SshProfileDefinition>> source)
    {
        foreach (var pair in source)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)
            {
                continue;
            }

            target[pair.Key.Trim()] = MapProfile(pair.Value);
        }
    }

    private string ResolveUserProfilesPath(string configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return ResolveConfiguredPath(configuredPath);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return Path.GetFullPath(Path.Combine(
            localAppData,
            "McpServer",
            "ssh-profiles.json"));
    }

    private string ResolveConfiguredPath(string path)
    {
        return _pathResolver.ResolveRelativeToContentRoot(path);
    }

    private sealed class SshProfilesFileDocument
    {
        public IDictionary<string, SshProfileDefinition> Profiles { get; set; } =
            new Dictionary<string, SshProfileDefinition>(StringComparer.OrdinalIgnoreCase);
    }

    private static SshProfileDefinition MapProfile(SshProfileDefinition source)
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
            AllowedCommands = source.AllowedCommands.ToArray(),
            DeniedCommands = source.DeniedCommands.ToArray(),
            AllowedRemotePathPrefixes = source.AllowedRemotePathPrefixes.ToArray(),
            AllowSudoCommand = source.AllowSudoCommand,
            AllowAllCommands = source.AllowAllCommands
        };
    }
}
