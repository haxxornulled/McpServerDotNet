using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Konscious.Security.Cryptography;

namespace McpServer.Infrastructure.Ssh;

public sealed class SshCredentialVault
{
    private const string VaultFileVersion = "1";
    private const int DefaultIterations = 3;
    private const int DefaultMemorySizeKiB = 65536;
    private const int DefaultDegreeOfParallelism = 1;
    private const int DerivedKeyLengthBytes = 32;
    private const int SaltLengthBytes = 16;
    private const int NonceLengthBytes = 12;
    private const int TagLengthBytes = 16;

    private readonly string _vaultKeyPath;
    private readonly object _syncRoot = new();
    private byte[]? _masterKey;

    public SshCredentialVault(string vaultKeyPath)
    {
        if (string.IsNullOrWhiteSpace(vaultKeyPath))
        {
            throw new ArgumentException("Vault key path is required.", nameof(vaultKeyPath));
        }

        _vaultKeyPath = Path.GetFullPath(vaultKeyPath);
    }

    public SshCredentialSecret Protect(
        string secret,
        int iterations = DefaultIterations,
        int memorySizeKiB = DefaultMemorySizeKiB,
        int degreeOfParallelism = DefaultDegreeOfParallelism)
    {
        if (secret is null)
        {
            throw new ArgumentNullException(nameof(secret));
        }

        var masterKey = LoadOrCreateMasterKey();
        var salt = RandomNumberGenerator.GetBytes(SaltLengthBytes);
        var nonce = RandomNumberGenerator.GetBytes(NonceLengthBytes);
        var derivedKey = DeriveKey(masterKey, salt, iterations, memorySizeKiB, degreeOfParallelism);
        var plaintextBytes = Encoding.UTF8.GetBytes(secret);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagLengthBytes];

        try
        {
            using var aes = new AesGcm(derivedKey, TagLengthBytes);
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

            return new SshCredentialSecret
            {
                Algorithm = GetAlgorithmName(),
                Salt = Convert.ToBase64String(salt),
                Nonce = Convert.ToBase64String(nonce),
                Tag = Convert.ToBase64String(tag),
                Ciphertext = Convert.ToBase64String(ciphertext),
                Iterations = Math.Max(1, iterations),
                MemorySizeKiB = Math.Max(8, memorySizeKiB),
                DegreeOfParallelism = Math.Max(1, degreeOfParallelism)
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derivedKey);
            CryptographicOperations.ZeroMemory(plaintextBytes);
            CryptographicOperations.ZeroMemory(tag);
            CryptographicOperations.ZeroMemory(ciphertext);
        }
    }

    public string Unprotect(SshCredentialSecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        if (!string.Equals(secret.Algorithm, GetAlgorithmName(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsupported SSH credential algorithm '{secret.Algorithm}'. Expected '{GetAlgorithmName()}'.");
        }

        var masterKey = LoadOrCreateMasterKey();
        var salt = Convert.FromBase64String(secret.Salt);
        var nonce = Convert.FromBase64String(secret.Nonce);
        var tag = Convert.FromBase64String(secret.Tag);
        var ciphertext = Convert.FromBase64String(secret.Ciphertext);
        var derivedKey = DeriveKey(
            masterKey,
            salt,
            secret.Iterations,
            secret.MemorySizeKiB,
            secret.DegreeOfParallelism);
        var plaintextBytes = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(derivedKey, TagLengthBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintextBytes);
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derivedKey);
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    private byte[] LoadOrCreateMasterKey()
    {
        lock (_syncRoot)
        {
            if (_masterKey is not null)
            {
                return (byte[])_masterKey.Clone();
            }

            var directory = Path.GetDirectoryName(_vaultKeyPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(_vaultKeyPath))
            {
                var document = JsonSerializer.Deserialize<VaultKeyDocument>(File.ReadAllText(_vaultKeyPath))
                    ?? throw new InvalidOperationException($"SSH vault key file '{_vaultKeyPath}' is empty or invalid.");

                if (!string.Equals(document.Version, VaultFileVersion, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"SSH vault key file '{_vaultKeyPath}' has unsupported version '{document.Version}'.");
                }

                _masterKey = Convert.FromBase64String(document.MasterKey);
                return (byte[])_masterKey.Clone();
            }

            _masterKey = RandomNumberGenerator.GetBytes(DerivedKeyLengthBytes);
            var payload = new VaultKeyDocument
            {
                Version = VaultFileVersion,
                MasterKey = Convert.ToBase64String(_masterKey)
            };

            File.WriteAllText(
                _vaultKeyPath,
                JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));

            return (byte[])_masterKey.Clone();
        }
    }

    private static byte[] DeriveKey(
        byte[] masterKey,
        byte[] salt,
        int iterations,
        int memorySizeKiB,
        int degreeOfParallelism)
    {
        var argon2 = new Argon2id(masterKey)
        {
            Salt = salt,
            Iterations = Math.Max(1, iterations),
            MemorySize = Math.Max(8, memorySizeKiB),
            DegreeOfParallelism = Math.Max(1, degreeOfParallelism)
        };

        return argon2.GetBytes(DerivedKeyLengthBytes);
    }

    private static string GetAlgorithmName() => "argon2id-aesgcm-v1";

    private sealed class VaultKeyDocument
    {
        public string Version { get; set; } = VaultFileVersion;

        public string MasterKey { get; set; } = string.Empty;
    }
}
