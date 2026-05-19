using System.Buffers;
using System.Collections.Concurrent;
using System.Text.Json;

namespace McpServer.Infrastructure.Ssh;

public sealed class SshCredentialVaultStore
{
    private const string FileVersion = "1";
    private static readonly ConcurrentDictionary<string, object> Locks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly string _vaultPath;
    private readonly string _lockPath;
    private readonly object _mutex;
    private readonly SshCredentialVault _protector;
    private readonly string _baseDirectory;

    public SshCredentialVaultStore(string vaultPath, string vaultKeyPath, string? baseDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultKeyPath);

        _baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(baseDirectory);
        _vaultPath = ResolvePath(vaultPath, _baseDirectory);
        _lockPath = _vaultPath + ".lock";
        _protector = new SshCredentialVault(ResolvePath(vaultKeyPath, _baseDirectory));
        _mutex = Locks.GetOrAdd(_vaultPath, static _ => new object());
    }

    public IReadOnlyList<SshCredentialVaultEntry> ListEntries()
    {
        return Execute(() =>
        {
            var sourceEntries = LoadDocument().Entries.Values;
            var count = sourceEntries.Count;
            if (count is 0)
            {
                return Array.Empty<SshCredentialVaultEntry>();
            }

            var rented = ArrayPool<VaultEntryDocument>.Shared.Rent(count);
            try
            {
                var span = rented.AsSpan(0, count);
                var index = 0;
                foreach (var entry in sourceEntries)
                {
                    span[index++] = entry;
                }

                span.Sort(static (left, right) =>
                    StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));

                var result = new SshCredentialVaultEntry[count];
                for (var resultIndex = 0; resultIndex < count; resultIndex++)
                {
                    result[resultIndex] = MapEntry(span[resultIndex].Name, span[resultIndex]);
                }

                return result;
            }
            finally
            {
                ArrayPool<VaultEntryDocument>.Shared.Return(rented, clearArray: true);
            }
        });
    }

    public bool DeleteEntry(string name)
    {
        return Execute(() =>
        {
            var key = NormalizeName(name);
            var document = LoadDocument();
            if (!document.Entries.Remove(key))
            {
                return false;
            }

            SaveDocument(document);
            return true;
        });
    }

    public SshCredentialVaultEntry UpsertEntry(string name, string secret, string? description = null)
    {
        return Execute(() =>
        {
            var key = NormalizeName(name);
            var now = DateTimeOffset.UtcNow;
            var document = LoadDocument();
            var existing = document.Entries.TryGetValue(key, out var current) ? current : null;
            var secretPayload = _protector.Protect(secret);
            var entry = new VaultEntryDocument
            {
                Name = key,
                Description = string.IsNullOrWhiteSpace(description) ? existing?.Description : description.Trim(),
                Secret = secretPayload,
                CreatedUtc = existing?.CreatedUtc ?? now,
                UpdatedUtc = now
            };

            document.Entries[key] = entry;
            SaveDocument(document);
            return MapEntry(key, entry);
        });
    }

    public string ResolveSecret(string name)
    {
        return Execute(() =>
        {
            var entry = GetRequiredEntry(name);
            return _protector.Unprotect(entry.Secret);
        });
    }

    public bool ContainsEntry(string name)
    {
        return Execute(() => LoadDocument().Entries.ContainsKey(NormalizeName(name)));
    }

    private T Execute<T>(Func<T> action)
    {
        lock (_mutex)
        {
            var directory = Path.GetDirectoryName(_vaultPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var lockStream = new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return action();
        }
    }

    private VaultDocument LoadDocument()
    {
        if (!File.Exists(_vaultPath))
        {
            return new VaultDocument();
        }

        using var stream = File.OpenRead(_vaultPath);
        var document = JsonSerializer.Deserialize<VaultDocument>(stream, JsonOptions);
        if (document is null)
        {
            return new VaultDocument();
        }

        if (!string.Equals(document.Version, FileVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"SSH vault file '{_vaultPath}' has unsupported version '{document.Version}'.");
        }

        document.Entries ??= new Dictionary<string, VaultEntryDocument>(StringComparer.OrdinalIgnoreCase);
        return document;
    }

    private void SaveDocument(VaultDocument document)
    {
        var directory = Path.GetDirectoryName(_vaultPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(directory ?? _baseDirectory, Path.GetRandomFileName());
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(document, JsonOptions));
            File.Move(tempPath, _vaultPath, true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }
    }

    private VaultEntryDocument GetRequiredEntry(string name)
    {
        var key = NormalizeName(name);
        var document = LoadDocument();
        if (!document.Entries.TryGetValue(key, out var entry))
        {
            throw new InvalidOperationException($"No SSH credential vault item named '{key}' was found.");
        }

        return entry;
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Vault item name is required.", nameof(name));
        }

        return name.Trim();
    }

    private static string ResolvePath(string path, string baseDirectory)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        if (Path.IsPathRooted(expanded))
        {
            return Path.GetFullPath(expanded);
        }

        return Path.GetFullPath(Path.Combine(baseDirectory, expanded));
    }

    private static SshCredentialVaultEntry MapEntry(string name, VaultEntryDocument entry) =>
        new(
            name,
            entry.Description,
            entry.Secret,
            entry.CreatedUtc,
            entry.UpdatedUtc);

    private sealed class VaultDocument
    {
        public string Version { get; set; } = FileVersion;

        public IDictionary<string, VaultEntryDocument> Entries { get; set; } =
            new Dictionary<string, VaultEntryDocument>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class VaultEntryDocument
    {
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public SshCredentialSecret Secret { get; set; } = new();

        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    }
}
