using System.Text.Json;

namespace McpServer.Infrastructure.Ssh;

public static class FileSystemSshProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static IReadOnlyList<ConfiguredSshProfile> LoadProfiles(
        string contentRoot,
        bool loadRepoProfilesFile,
        string? repoProfilesFilePath,
        bool loadUserProfilesFile,
        string? userProfilesFilePath,
        bool allowInlineProfiles,
        IEnumerable<ConfiguredSshProfile>? inlineProfiles)
    {
        var profiles = new Dictionary<string, ConfiguredSshProfile>(StringComparer.OrdinalIgnoreCase);

        if (allowInlineProfiles && inlineProfiles is not null)
        {
            MergeProfiles(profiles, inlineProfiles);
        }

        if (loadRepoProfilesFile)
        {
            var path = ResolveConfiguredPath(contentRoot, repoProfilesFilePath ?? string.Empty);
            MergeProfiles(profiles, LoadProfilesFromFile(path));
        }

        if (loadUserProfilesFile)
        {
            var path = ResolveUserProfilesPath(userProfilesFilePath);
            MergeProfiles(profiles, LoadProfilesFromFile(path));
        }

        return profiles.Values
            .OrderBy(static profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static void SaveProfiles(
        string contentRoot,
        string path,
        IEnumerable<ConfiguredSshProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        var resolvedPath = ResolveConfiguredPath(contentRoot, path);
        var directory = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var document = new SshProfilesDocument
        {
            Profiles = profiles
                .Where(static profile => !string.IsNullOrWhiteSpace(profile.Name))
                .ToDictionary(
                    static profile => profile.Name,
                    static profile => MapProfile(profile),
                    StringComparer.OrdinalIgnoreCase)
        };

        File.WriteAllText(
            resolvedPath,
            JsonSerializer.Serialize(document, JsonOptions));
    }

    private static void MergeProfiles(
        IDictionary<string, ConfiguredSshProfile> target,
        IEnumerable<ConfiguredSshProfile> source)
    {
        foreach (var profile in source)
        {
            if (profile is null || string.IsNullOrWhiteSpace(profile.Name))
            {
                continue;
            }

            target[profile.Name.Trim()] = profile with
            {
                Name = profile.Name.Trim()
            };
        }
    }

    private static void MergeProfiles(
        IDictionary<string, ConfiguredSshProfile> target,
        IEnumerable<KeyValuePair<string, ConfiguredSshProfile>> source)
    {
        foreach (var pair in source)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)
            {
                continue;
            }

            target[pair.Key.Trim()] = pair.Value with
            {
                Name = pair.Key.Trim()
            };
        }
    }

    private static IReadOnlyDictionary<string, ConfiguredSshProfile> LoadProfilesFromFile(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, ConfiguredSshProfile>(StringComparer.OrdinalIgnoreCase);
        }

        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<SshProfilesDocument>(stream, JsonOptions);
        return document?.Profiles is null
            ? new Dictionary<string, ConfiguredSshProfile>(StringComparer.OrdinalIgnoreCase)
            : document.Profiles.ToDictionary(
                static pair => pair.Key,
                static pair => MapProfile(pair.Key, pair.Value),
                StringComparer.OrdinalIgnoreCase);
    }

    private static string ResolveConfiguredPath(string contentRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        path = Environment.ExpandEnvironmentVariables(path);
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(contentRoot, path));
    }

    private static string ResolveUserProfilesPath(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
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

    private sealed class SshProfilesDocument
    {
        public IDictionary<string, SshProfileDocument> Profiles { get; set; } =
            new Dictionary<string, SshProfileDocument>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class SshProfileDocument
    {
        public string? Name { get; set; }

        public string Host { get; set; } = string.Empty;

        public int Port { get; set; } = 22;

        public string Username { get; set; } = string.Empty;

        public string? PrivateKeyPath { get; set; }

        public string? PasswordVaultItemName { get; set; }

        public string? PrivateKeyPassphraseVaultItemName { get; set; }

        public string? WorkingDirectory { get; set; }

        public string? HostKeySha256 { get; set; }

        public bool AcceptUnknownHostKey { get; set; }

        public IList<string> AllowedCommands { get; set; } = [];

        public IList<string> DeniedCommands { get; set; } = [];

        public IList<string> AllowedRemotePathPrefixes { get; set; } = [];

        public bool AllowSudoCommand { get; set; }
    }

    private static SshProfileDocument MapProfile(ConfiguredSshProfile profile)
    {
        return new SshProfileDocument
        {
            Name = profile.Name,
            Host = profile.Host,
            Port = profile.Port,
            Username = profile.Username,
            PrivateKeyPath = profile.PrivateKeyPath,
            PasswordVaultItemName = profile.PasswordVaultItemName,
            PrivateKeyPassphraseVaultItemName = profile.PrivateKeyPassphraseVaultItemName,
            WorkingDirectory = profile.WorkingDirectory,
            HostKeySha256 = profile.HostKeySha256,
            AcceptUnknownHostKey = profile.AcceptUnknownHostKey,
            AllowedCommands = profile.AllowedCommands.ToList(),
            DeniedCommands = profile.DeniedCommands.ToList(),
            AllowedRemotePathPrefixes = profile.AllowedRemotePathPrefixes.ToList(),
            AllowSudoCommand = profile.AllowSudoCommand
        };
    }

    private static ConfiguredSshProfile MapProfile(string profileName, SshProfileDocument profile)
    {
        return new ConfiguredSshProfile(
            profile.Name ?? profileName,
            profile.Host,
            profile.Port,
            profile.Username,
            profile.PrivateKeyPath,
            profile.PasswordVaultItemName,
            profile.PrivateKeyPassphraseVaultItemName,
            profile.WorkingDirectory,
            profile.HostKeySha256,
            profile.AcceptUnknownHostKey,
            profile.AllowedCommands.ToArray(),
            profile.DeniedCommands.ToArray(),
            profile.AllowedRemotePathPrefixes.ToArray(),
            profile.AllowSudoCommand);
    }
}
