using McpServer.Infrastructure.Ssh;

namespace McpServer.SshVaultCli;

public static class VaultCli
{
    public static Task<int> RunAsync(string[] args) => Task.FromResult(Run(args));

    public static int Run(string[] args)
    {
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

    private static SshCredentialVaultStore CreateStore(VaultCommandLineOptions options)
    {
        var baseDirectory = options.Value("base-directory") ?? Directory.GetCurrentDirectory();
        var vaultPath = options.Value("vault-path")
            ?? Path.Combine(baseDirectory, "config", "mcpserver", "ssh-vault.local.json");

        var vaultKeyPath = options.Value("vault-key-path")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "McpServer", "ssh-vault.key");

        return new SshCredentialVaultStore(vaultPath, vaultKeyPath, baseDirectory);
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

    private static void WriteHelp()
    {
        Console.WriteLine("""
SSH vault CLI

Usage:
  add <name> [--secret value] [--secret-file path] [--description text] [--vault-path path] [--vault-key-path path] [--base-directory path]
  verify <name> [--expected value] [--expected-file path] [--vault-path path] [--vault-key-path path] [--base-directory path]
  delete <name> [--vault-path path] [--vault-key-path path] [--base-directory path]
  list [--vault-path path] [--vault-key-path path] [--base-directory path]

Options:
  --name             Explicit item name. Optional if you pass the name positionally.
  --secret           Plaintext secret to encrypt and store.
  --secret-file      Read the plaintext secret from a file.
  --expected         Plaintext secret to compare against during verify.
  --expected-file    Read the expected plaintext secret from a file during verify.
  --description      Optional friendly description.
  --vault-path       Path to the vault JSON file.
  --vault-key-path   Path to the local vault key file.
  --base-directory   Base directory for relative paths.
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
