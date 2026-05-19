using System.Text.Json;
using McpServer.Infrastructure.Ssh;
using Microsoft.Extensions.DependencyInjection;

namespace McpServer.SshVaultCli;

public static class VaultCli
{
    public static Task<int> RunAsync(string[] args) => RunAsync(args, CreateDefaultServiceProvider());

    public static int Run(string[] args) => Run(args, CreateDefaultServiceProvider());

    public static Task<int> RunAsync(string[] args, IServiceProvider services) => Task.FromResult(Run(args, services));

    public static int Run(string[] args, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);

        if (args.Length == 0 || IsHelp(args[0]))
        {
            WriteHelp();
            return 0;
        }

        var command = args[0].Trim().ToLowerInvariant();
        var options = VaultCommandLineOptions.Parse(args.Skip(1).ToArray());

        try
        {
            return command switch
            {
                "list" => RunList(options),
                "add" => RunAdd(options),
                "verify" => RunVerify(options),
                "delete" => RunDelete(options),
                "profile" => RunProfile(options, services),
                _ => UnknownCommand(command)
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Operation cancelled.");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int RunList(VaultCommandLineOptions options)
    {
        var store = CreateStore(options);
        var entries = store.ListEntries();

        if (entries.Count is 0)
        {
            Console.WriteLine("No SSH vault entries found.");
            return 0;
        }

        foreach (var entry in entries)
        {
            var description = string.IsNullOrWhiteSpace(entry.Description) ? string.Empty : $" - {entry.Description}";
            Console.WriteLine($"{entry.Name}{description}");
        }

        return 0;
    }

    private static int RunAdd(VaultCommandLineOptions options)
    {
        var name = options.RequiredValue("name", options.Positionals.FirstOrDefault());
        var secret = options.Value("secret");
        if (string.IsNullOrWhiteSpace(secret))
        {
            secret = options.Value("secret-file") is string secretFile
                ? ReadSecretFromFile(secretFile)
                : ReadSecretFromConsole($"SSH secret for '{name}': ");
        }

        var store = CreateStore(options);
        var entry = store.UpsertEntry(name, secret, options.Value("description"));
        Console.WriteLine($"Saved SSH vault item '{entry.Name}'.");
        return 0;
    }

    private static int RunDelete(VaultCommandLineOptions options)
    {
        var name = options.RequiredValue("name", options.Positionals.FirstOrDefault());
        var store = CreateStore(options);
        var deleted = store.DeleteEntry(name);
        if (!deleted)
        {
            Console.Error.WriteLine($"No SSH vault item named '{name}' was found.");
            return 1;
        }

        Console.WriteLine($"Deleted SSH vault item '{name}'.");
        return 0;
    }

    private static int RunVerify(VaultCommandLineOptions options)
    {
        var name = options.RequiredValue("name", options.Positionals.FirstOrDefault());
        var store = CreateStore(options);
        var decrypted = store.ResolveSecret(name);
        var expected = options.Value("expected")
            ?? (options.Value("expected-file") is string expectedFile
                ? ReadSecretFromFile(expectedFile)
                : ReadSecretFromConsole($"Expected SSH secret for '{name}': "));

        if (!string.Equals(decrypted, expected, StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"SSH vault item '{name}' did not match the expected secret.");
            return 1;
        }

        Console.WriteLine($"Verified SSH vault item '{name}'.");
        return 0;
    }

    private static int RunProfile(VaultCommandLineOptions options, IServiceProvider services)
    {
        var subcommand = options.Positionals.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(subcommand))
        {
            WriteProfileHelp();
            return 0;
        }

        return subcommand.Trim().ToLowerInvariant() switch
        {
            "help" or "--help" or "-h" or "/?" => WriteProfileHelpAndReturn(),
            "list" => RunProfileList(options, services),
            "show" or "get" => RunProfileShow(options, services),
            "upsert" or "add" or "update" => RunProfileUpsert(options, services),
            "link" => RunProfileLink(options, services),
            "unlink" => RunProfileUnlink(options, services),
            "delete" => RunProfileDelete(options, services),
            _ => UnknownProfileCommand(subcommand)
        };
    }

    private static int RunProfileList(VaultCommandLineOptions options, IServiceProvider services)
    {
        var manager = services.GetRequiredService<SshProfileManager>();
        var baseDirectory = ResolveBaseDirectory(options);
        var profiles = manager.ListProfiles(baseDirectory, options.Value("profiles-path"));

        if (profiles.Count is 0)
        {
            Console.WriteLine("No SSH profiles found.");
            return 0;
        }

        foreach (var profile in profiles)
        {
            Console.WriteLine(FormatProfileSummary(profile));
        }

        return 0;
    }

    private static int RunProfileShow(VaultCommandLineOptions options, IServiceProvider services)
    {
        var name = options.RequiredValue("name", options.Positionals.Skip(1).FirstOrDefault());
        var manager = services.GetRequiredService<SshProfileManager>();
        var baseDirectory = ResolveBaseDirectory(options);
        var profile = manager.GetProfile(baseDirectory, name, options.Value("profiles-path"));
        if (profile is null)
        {
            Console.Error.WriteLine($"No SSH profile named '{name}' was found.");
            return 1;
        }

        Console.WriteLine(JsonSerializer.Serialize(profile, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        }));
        return 0;
    }

    private static int RunProfileUpsert(VaultCommandLineOptions options, IServiceProvider services)
    {
        var name = options.RequiredValue("name", options.Positionals.Skip(1).FirstOrDefault());
        var manager = services.GetRequiredService<SshProfileManager>();
        var baseDirectory = ResolveBaseDirectory(options);
        var profilesPath = options.Value("profiles-path");
        var existing = manager.GetProfile(baseDirectory, name, profilesPath);
        var profile = BuildProfile(name, options, existing);
        var saved = manager.UpsertProfile(baseDirectory, profile, profilesPath);

        Console.WriteLine($"Saved SSH profile '{saved.Name}' for {saved.Username}@{saved.Host}.");
        return 0;
    }

    private static int RunProfileDelete(VaultCommandLineOptions options, IServiceProvider services)
    {
        var name = options.RequiredValue("name", options.Positionals.Skip(1).FirstOrDefault());
        var manager = services.GetRequiredService<SshProfileManager>();
        var baseDirectory = ResolveBaseDirectory(options);
        var deleted = manager.DeleteProfile(baseDirectory, name, options.Value("profiles-path"));
        if (!deleted)
        {
            Console.Error.WriteLine($"No SSH profile named '{name}' was found.");
            return 1;
        }

        Console.WriteLine($"Deleted SSH profile '{name}'.");
        return 0;
    }

    private static int RunProfileLink(VaultCommandLineOptions options, IServiceProvider services)
    {
        var name = options.RequiredValue("name", options.Positionals.Skip(1).FirstOrDefault());
        var manager = services.GetRequiredService<SshProfileManager>();
        var baseDirectory = ResolveBaseDirectory(options);
        var updated = manager.LinkCredential(
            baseDirectory,
            name,
            options.Value("profiles-path"),
            options.Value("password-vault-item"),
            options.Value("private-key-passphrase-vault-item"));

        Console.WriteLine($"Linked credentials for SSH profile '{updated.Name}'.");
        return 0;
    }

    private static int RunProfileUnlink(VaultCommandLineOptions options, IServiceProvider services)
    {
        var name = options.RequiredValue("name", options.Positionals.Skip(1).FirstOrDefault());
        var manager = services.GetRequiredService<SshProfileManager>();
        var baseDirectory = ResolveBaseDirectory(options);
        var updated = manager.UnlinkCredential(baseDirectory, name, options.Value("profiles-path"));

        Console.WriteLine($"Unlinked credentials for SSH profile '{updated.Name}'.");
        return 0;
    }

    private static ConfiguredSshProfile BuildProfile(
        string name,
        VaultCommandLineOptions options,
        ConfiguredSshProfile? existing)
    {
        var host = options.Value("host") ?? existing?.Host;
        var username = options.Value("username") ?? existing?.Username;

        if (string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("Missing required option '--host'.");
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("Missing required option '--username'.");
        }

        var port = ParseIntOption(options.Value("port"), existing?.Port ?? 22, "port");
        var passwordVaultItemName = options.Value("password-vault-item") ?? existing?.PasswordVaultItemName;
        var privateKeyPath = options.Value("private-key-path") ?? existing?.PrivateKeyPath;
        var privateKeyPassphraseVaultItemName = options.Value("private-key-passphrase-vault-item") ?? existing?.PrivateKeyPassphraseVaultItemName;
        var workingDirectory = options.Value("working-directory") ?? existing?.WorkingDirectory;
        var hostKeySha256 = options.Value("host-key-sha256") ?? existing?.HostKeySha256;
        var acceptUnknownHostKey = ParseBoolOption(options.Value("accept-unknown-host-key"), existing?.AcceptUnknownHostKey ?? false, "accept-unknown-host-key");
        var allowSudoCommand = ParseBoolOption(options.Value("allow-sudo-command"), existing?.AllowSudoCommand ?? false, "allow-sudo-command");
        var allowAllCommands = ParseBoolOption(options.Value("allow-all-commands"), existing?.AllowAllCommands ?? false, "allow-all-commands");
        var allowedCommands = ParseCsvOption(options.Value("allowed-commands")) ?? existing?.AllowedCommands ?? [];
        var deniedCommands = ParseCsvOption(options.Value("denied-commands")) ?? existing?.DeniedCommands ?? [];
        var allowedRemotePathPrefixes = ParseCsvOption(options.Value("allowed-path-prefixes")) ?? existing?.AllowedRemotePathPrefixes ?? [];

        return new ConfiguredSshProfile(
            name,
            host,
            port,
            username,
            privateKeyPath,
            passwordVaultItemName,
            privateKeyPassphraseVaultItemName,
            workingDirectory,
            hostKeySha256,
            acceptUnknownHostKey,
            allowedCommands,
            deniedCommands,
            allowedRemotePathPrefixes,
            allowSudoCommand,
            allowAllCommands);
    }

    private static string FormatProfileSummary(ConfiguredSshProfile profile)
    {
        var credentialSummary = profile.PasswordVaultItemName is { Length: > 0 }
            ? $"password-vault={profile.PasswordVaultItemName}"
            : profile.PrivateKeyPath is { Length: > 0 }
                ? $"private-key={profile.PrivateKeyPath}"
                : "credential=unset";

        var sudoSuffix = profile.AllowSudoCommand ? " sudo" : string.Empty;
        var accessSuffix = profile.AllowAllCommands ? " any" : string.Empty;
        return $"{profile.Name} - {profile.Username}@{profile.Host}:{profile.Port} {credentialSummary}{accessSuffix}{sudoSuffix}";
    }

    private static string ResolveBaseDirectory(VaultCommandLineOptions options)
    {
        return Path.GetFullPath(options.Value("base-directory") ?? Directory.GetCurrentDirectory());
    }

    private static int ParseIntOption(string? value, int fallback, string name)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : int.TryParse(value, out var parsed)
                ? parsed
                : throw new ArgumentException($"Invalid integer value for '--{name}': '{value}'.");
    }

    private static bool ParseBoolOption(string? value, bool fallback, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        throw new ArgumentException($"Invalid boolean value for '--{name}': '{value}'.");
    }

    private static IReadOnlyCollection<string>? ParseCsvOption(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    private static SshCredentialVaultStore CreateStore(VaultCommandLineOptions options)
    {
        var baseDirectory = ResolveBaseDirectory(options);
        var vaultPath = options.Value("vault-path")
            ?? Path.Combine(baseDirectory, "config", "mcpserver", "ssh-vault.local.json");

        var vaultKeyPath = options.Value("vault-key-path")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "McpServer", "ssh-vault.key");

        return new SshCredentialVaultStore(vaultPath, vaultKeyPath, baseDirectory);
    }

    private static IServiceProvider CreateDefaultServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<SshProfileManager>();
        return services.BuildServiceProvider();
    }

    private static string ReadSecretFromConsole(string prompt)
    {
        if (Console.IsInputRedirected)
        {
            throw new InvalidOperationException("A secret must be provided with --secret or --secret-file when stdin is redirected.");
        }

        Console.Write(prompt);
        var buffer = new List<char>();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key is ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }

            if (key.Key is ConsoleKey.Backspace)
            {
                if (buffer.Count > 0)
                {
                    buffer.RemoveAt(buffer.Count - 1);
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                buffer.Add(key.KeyChar);
            }
        }

        return new string(buffer.ToArray());
    }

    private static string ReadSecretFromFile(string secretFile)
    {
        var secret = File.ReadAllText(secretFile);
        return secret.TrimEnd('\r', '\n');
    }

    private static bool IsHelp(string value)
    {
        return string.Equals(value, "help", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "/?", StringComparison.OrdinalIgnoreCase);
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command '{command}'.");
        WriteHelp();
        return 2;
    }

    private static int UnknownProfileCommand(string command)
    {
        Console.Error.WriteLine($"Unknown profile command '{command}'.");
        WriteProfileHelp();
        return 2;
    }

    private static int WriteProfileHelpAndReturn()
    {
        WriteProfileHelp();
        return 0;
    }

    private static void WriteHelp()
    {
        Console.WriteLine("""
SSH vault and profile CLI

Usage:
  add <name> [--secret value] [--secret-file path] [--description text] [--vault-path path] [--vault-key-path path] [--base-directory path]
  verify <name> [--expected value] [--expected-file path] [--vault-path path] [--vault-key-path path] [--base-directory path]
  delete <name> [--vault-path path] [--vault-key-path path] [--base-directory path]
  list [--vault-path path] [--vault-key-path path] [--base-directory path]
  profile help
  profile list [--profiles-path path] [--base-directory path]
  profile show <name> [--profiles-path path] [--base-directory path]
  profile upsert <name> --host host --username user [--port n] [--password-vault-item name] [--private-key-path path] [--private-key-passphrase-vault-item name] [--working-directory path] [--host-key-sha256 value] [--accept-unknown-host-key true|false] [--allow-sudo-command true|false] [--allow-all-commands true|false] [--allowed-commands csv] [--denied-commands csv] [--allowed-path-prefixes csv] [--profiles-path path] [--base-directory path]
  profile link <name> [--password-vault-item name] [--private-key-passphrase-vault-item name] [--profiles-path path] [--base-directory path]
  profile unlink <name> [--profiles-path path] [--base-directory path]
  profile delete <name> [--profiles-path path] [--base-directory path]

Options:
  --name             Explicit item name. Optional if you pass the name positionally.
  --secret           Plaintext secret to encrypt and store.
  --secret-file      Read the plaintext secret from a file.
  --expected         Plaintext secret to compare against during verify.
  --expected-file    Read the expected plaintext secret from a file during verify.
  --description      Optional friendly description.
  --vault-path       Path to the vault JSON file.
  --vault-key-path   Path to the local vault key file.
  --profiles-path    Path to the SSH profiles JSON file.
  --base-directory   Base directory for relative paths.
""");
    }

    private static void WriteProfileHelp()
    {
        Console.WriteLine("""
SSH profile commands

Usage:
  profile list [--profiles-path path] [--base-directory path]
  profile show <name> [--profiles-path path] [--base-directory path]
  profile upsert <name> --host host --username user [--port n] [--password-vault-item name] [--private-key-path path] [--private-key-passphrase-vault-item name] [--working-directory path] [--host-key-sha256 value] [--accept-unknown-host-key true|false] [--allow-sudo-command true|false] [--allow-all-commands true|false] [--allowed-commands csv] [--denied-commands csv] [--allowed-path-prefixes csv] [--profiles-path path] [--base-directory path]
  profile delete <name> [--profiles-path path] [--base-directory path]
""");
    }
}

internal sealed class VaultCommandLineOptions
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Positionals { get; private set; } = Array.Empty<string>();

    public string? Value(string key) => _values.TryGetValue(key, out var value) ? value : null;

    public string RequiredValue(string key, string? fallback = null)
    {
        var value = Value(key) ?? fallback;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Missing required option '--{key}'.");
        }

        return value;
    }

    public static VaultCommandLineOptions Parse(string[] args)
    {
        var options = new VaultCommandLineOptions();
        var positionals = new List<string>();

        for (var index = 0; index < args.Length; index++)
        {
            var current = args[index];
            if (!current.StartsWith("--", StringComparison.Ordinal))
            {
                positionals.Add(current);
                continue;
            }

            var key = current[2..];
            var nextIsValue = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal);
            options._values[key] = nextIsValue ? args[++index] : "true";
        }

        options.Positionals = positionals;
        return options;
    }
}
