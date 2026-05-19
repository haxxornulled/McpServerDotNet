using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace McpServer.Infrastructure.Ssh;

public sealed class SshProfileManager
{
    private const string DefaultProfilesPath = "config/mcpserver/ssh-profiles.local.json";
    private static readonly ConcurrentDictionary<string, object> Locks = new(StringComparer.OrdinalIgnoreCase);

    private readonly ILogger<SshProfileManager> _logger;

    public SshProfileManager(ILogger<SshProfileManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<ConfiguredSshProfile> ListProfiles(string contentRoot, string? profilesPath = null)
    {
        var resolvedProfilesPath = ResolveProfilesPath(contentRoot, profilesPath);
        return ExecuteLocked(resolvedProfilesPath, () =>
        {
            var profiles = LoadProfiles(contentRoot, resolvedProfilesPath);
            _logger.LogInformation("Loaded {ProfileCount} SSH profile(s) from {ProfilesPath}.", profiles.Count, resolvedProfilesPath);
            return profiles;
        });
    }

    public ConfiguredSshProfile? GetProfile(string contentRoot, string name, string? profilesPath = null)
    {
        var resolvedProfilesPath = ResolveProfilesPath(contentRoot, profilesPath);
        return ExecuteLocked(resolvedProfilesPath, () =>
        {
            var profiles = LoadProfiles(contentRoot, resolvedProfilesPath);
            for (var i = 0; i < profiles.Count; i++)
            {
                if (string.Equals(profiles[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Loaded SSH profile '{ProfileName}' from {ProfilesPath}.", profiles[i].Name, resolvedProfilesPath);
                    return profiles[i];
                }
            }

            _logger.LogInformation("SSH profile '{ProfileName}' was not found in {ProfilesPath}.", name, resolvedProfilesPath);
            return null;
        });
    }

    public ConfiguredSshProfile UpsertProfile(string contentRoot, ConfiguredSshProfile profile, string? profilesPath = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ValidateProfile(profile);

        var resolvedProfilesPath = ResolveProfilesPath(contentRoot, profilesPath);
        return ExecuteLocked(resolvedProfilesPath, () =>
        {
            var profiles = LoadProfiles(contentRoot, resolvedProfilesPath).ToDictionary(static item => item.Name, StringComparer.OrdinalIgnoreCase);
            var normalizedProfile = NormalizeProfile(profile);
            profiles[normalizedProfile.Name] = normalizedProfile;

            SaveProfiles(contentRoot, resolvedProfilesPath, profiles.Values);
            _logger.LogInformation("Saved SSH profile '{ProfileName}' to {ProfilesPath}.", normalizedProfile.Name, resolvedProfilesPath);
            return normalizedProfile;
        });
    }

    public ConfiguredSshProfile LinkCredential(
        string contentRoot,
        string name,
        string? profilesPath = null,
        string? passwordVaultItemName = null,
        string? privateKeyPassphraseVaultItemName = null)
    {
        if (string.IsNullOrWhiteSpace(passwordVaultItemName) && string.IsNullOrWhiteSpace(privateKeyPassphraseVaultItemName))
        {
            throw new ArgumentException("At least one credential reference must be provided.");
        }

        var resolvedProfilesPath = ResolveProfilesPath(contentRoot, profilesPath);
        return ExecuteLocked(resolvedProfilesPath, () =>
        {
            var profiles = LoadProfiles(contentRoot, resolvedProfilesPath).ToDictionary(static item => item.Name, StringComparer.OrdinalIgnoreCase);
            if (!profiles.TryGetValue(name, out var existing))
            {
                throw new InvalidOperationException($"No SSH profile named '{name}' was found.");
            }

            var updated = existing with
            {
                PasswordVaultItemName = TrimOrNull(passwordVaultItemName) ?? existing.PasswordVaultItemName,
                PrivateKeyPassphraseVaultItemName = TrimOrNull(privateKeyPassphraseVaultItemName) ?? existing.PrivateKeyPassphraseVaultItemName
            };

            profiles[updated.Name] = updated;
            SaveProfiles(contentRoot, resolvedProfilesPath, profiles.Values);
            _logger.LogInformation(
                "Linked SSH credential reference for profile '{ProfileName}' in {ProfilesPath}.",
                updated.Name,
                resolvedProfilesPath);
            return updated;
        });
    }

    public ConfiguredSshProfile UnlinkCredential(string contentRoot, string name, string? profilesPath = null)
    {
        var resolvedProfilesPath = ResolveProfilesPath(contentRoot, profilesPath);
        return ExecuteLocked(resolvedProfilesPath, () =>
        {
            var profiles = LoadProfiles(contentRoot, resolvedProfilesPath).ToDictionary(static item => item.Name, StringComparer.OrdinalIgnoreCase);
            if (!profiles.TryGetValue(name, out var existing))
            {
                throw new InvalidOperationException($"No SSH profile named '{name}' was found.");
            }

            var updated = existing with
            {
                PasswordVaultItemName = null,
                PrivateKeyPassphraseVaultItemName = null
            };

            profiles[updated.Name] = updated;
            SaveProfiles(contentRoot, resolvedProfilesPath, profiles.Values);
            _logger.LogInformation(
                "Unlinked SSH credential reference for profile '{ProfileName}' in {ProfilesPath}.",
                updated.Name,
                resolvedProfilesPath);
            return updated;
        });
    }

    public bool DeleteProfile(string contentRoot, string name, string? profilesPath = null)
    {
        var resolvedProfilesPath = ResolveProfilesPath(contentRoot, profilesPath);
        return ExecuteLocked(resolvedProfilesPath, () =>
        {
            var profiles = LoadProfiles(contentRoot, resolvedProfilesPath).ToList();
            var removed = profiles.RemoveAll(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;
            if (!removed)
            {
                _logger.LogWarning("SSH profile '{ProfileName}' was not found in {ProfilesPath}.", name, resolvedProfilesPath);
                return false;
            }

            SaveProfiles(contentRoot, resolvedProfilesPath, profiles);
            _logger.LogInformation("Deleted SSH profile '{ProfileName}' from {ProfilesPath}.", name, resolvedProfilesPath);
            return true;
        });
    }

    private static IReadOnlyList<ConfiguredSshProfile> LoadProfiles(string contentRoot, string profilesPath)
    {
        return FileSystemSshProfileStore.LoadProfiles(
            contentRoot,
            loadRepoProfilesFile: true,
            repoProfilesFilePath: profilesPath,
            loadUserProfilesFile: false,
            userProfilesFilePath: null,
            allowInlineProfiles: false,
            inlineProfiles: null);
    }

    private static void SaveProfiles(string contentRoot, string profilesPath, IEnumerable<ConfiguredSshProfile> profiles)
    {
        FileSystemSshProfileStore.WriteProfilesUnlocked(contentRoot, profilesPath, profiles);
    }

    private static object GetMutex(string profilesPath) =>
        Locks.GetOrAdd(profilesPath, static _ => new object());

    private static T ExecuteLocked<T>(string profilesPath, Func<T> action)
    {
        lock (GetMutex(profilesPath))
        {
            var lockPath = profilesPath + ".lock";
            var directory = Path.GetDirectoryName(lockPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return action();
        }
    }

    private static string ResolveProfilesPath(string contentRoot, string? profilesPath)
    {
        var path = string.IsNullOrWhiteSpace(profilesPath) ? DefaultProfilesPath : profilesPath.Trim();
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(contentRoot, path));
    }

    private static ConfiguredSshProfile NormalizeProfile(ConfiguredSshProfile profile)
    {
        return profile with
        {
            Name = profile.Name.Trim(),
            Host = profile.Host.Trim(),
            Username = profile.Username.Trim(),
            PrivateKeyPath = TrimOrNull(profile.PrivateKeyPath),
            PasswordVaultItemName = TrimOrNull(profile.PasswordVaultItemName),
            PrivateKeyPassphraseVaultItemName = TrimOrNull(profile.PrivateKeyPassphraseVaultItemName),
            WorkingDirectory = TrimOrNull(profile.WorkingDirectory),
            HostKeySha256 = TrimOrNull(profile.HostKeySha256),
            AllowedCommands = profile.AllowedCommands
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(static item => item.Trim())
                .ToArray(),
            DeniedCommands = profile.DeniedCommands
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(static item => item.Trim())
                .ToArray(),
            AllowedRemotePathPrefixes = profile.AllowedRemotePathPrefixes
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(static item => item.Trim())
                .ToArray(),
            AllowAllCommands = profile.AllowAllCommands
        };
    }

    private static void ValidateProfile(ConfiguredSshProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Host);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Username);
    }

    private static string? TrimOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
