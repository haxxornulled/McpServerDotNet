namespace McpServer.Application.Execution;

public sealed class ShellExecutionPolicyOptions
{
    public static IReadOnlyCollection<string> DefaultDeniedCommands { get; } =
        Array.AsReadOnly(new[]
        {
            "bash",
            "bitsadmin",
            "certutil",
            "cmd",
            "curl",
            "del",
            "erase",
            "format",
            "mkfs",
            "mshta",
            "net",
            "netsh",
            "powershell",
            "pwsh",
            "reg",
            "regsvr32",
            "rm",
            "rmdir",
            "rundll32",
            "schtasks",
            "sh",
            "shutdown",
            "ssh",
            "wscript",
            "zsh"
        });

    public ShellExecutionPolicyOptions(
        bool AllowShellFallback,
        bool AllowWindowsCompatibilityShell,
        IReadOnlyCollection<string>? AllowedCommands,
        IReadOnlyCollection<string>? DeniedCommands,
        int MaxTimeoutSeconds,
        int MaxOutputChars)
    {
        this.AllowShellFallback = AllowShellFallback;
        this.AllowWindowsCompatibilityShell = AllowWindowsCompatibilityShell;
        this.AllowedCommands = NormalizeCommandCollection(AllowedCommands, Array.Empty<string>());
        this.DeniedCommands = NormalizeCommandCollection(DeniedCommands, DefaultDeniedCommands);
        this.MaxTimeoutSeconds = MaxTimeoutSeconds;
        this.MaxOutputChars = MaxOutputChars;
    }

    public ShellExecutionPolicyOptions(
        bool AllowShellFallback,
        IReadOnlyCollection<string>? AllowedCommands,
        IReadOnlyCollection<string>? DeniedCommands,
        int MaxTimeoutSeconds,
        int MaxOutputChars)
        : this(
            AllowShellFallback: AllowShellFallback,
            AllowWindowsCompatibilityShell: false,
            AllowedCommands: AllowedCommands,
            DeniedCommands: DeniedCommands,
            MaxTimeoutSeconds: MaxTimeoutSeconds,
            MaxOutputChars: MaxOutputChars)
    {
    }

    public bool AllowShellFallback { get; }

    public bool AllowWindowsCompatibilityShell { get; }

    public IReadOnlyCollection<string> AllowedCommands { get; }

    public IReadOnlyCollection<string> DeniedCommands { get; }

    public int MaxTimeoutSeconds { get; }

    public int MaxOutputChars { get; }

    public static ShellExecutionPolicyOptions BuildAndTestOnly { get; } = new(
        AllowShellFallback: false,
        AllowWindowsCompatibilityShell: false,
        AllowedCommands:
        [
            "dotnet",
            "git"
        ],
        DeniedCommands: DefaultDeniedCommands,
        MaxTimeoutSeconds: 300,
        MaxOutputChars: 200000);

    public static ShellExecutionPolicyOptions Permissive { get; } = new(
        AllowShellFallback: true,
        AllowWindowsCompatibilityShell: true,
        AllowedCommands: [],
        DeniedCommands: [],
        MaxTimeoutSeconds: 600,
        MaxOutputChars: 200000);

    private static IReadOnlyCollection<string> NormalizeCommandCollection(
        IReadOnlyCollection<string>? commands,
        IReadOnlyCollection<string> defaultCommands)
    {
        var source = commands ?? defaultCommands;

        if (source.Count == 0)
        {
            return Array.Empty<string>();
        }

        return source
            .Where(static command => !string.IsNullOrWhiteSpace(command))
            .Select(static command => command.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
