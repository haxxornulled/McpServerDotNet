using System.Security.Cryptography;
using System.Text;
using LanguageExt;
using LanguageExt.Common;
using McpServer.Application.Abstractions.Ssh;
using McpServer.Application.Mcp.Validation;
using McpServer.Application.Ssh.Commands;
using McpServer.Application.Ssh.Results;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace McpServer.Infrastructure.Ssh;

public sealed class SshService(
    IEnumerable<ConfiguredSshProfile> profiles,
    string contentRoot,
    ILogger<SshService> logger,
    SshCredentialVault? credentialVault = null,
    SshCredentialVaultStore? credentialVaultStore = null) : ISshService
{
    private const int MinimumTimeoutSeconds = 1;
    private const int MaximumTimeoutSeconds = 1800;
    private const int MinimumOutputChars = 256;
    private const int MaximumOutputChars = 200000;

    private readonly Dictionary<string, ConfiguredSshProfile> _profiles = profiles
        .Where(static profile => !string.IsNullOrWhiteSpace(profile.Name))
        .ToDictionary(static profile => profile.Name, StringComparer.OrdinalIgnoreCase);
    private readonly SshCredentialVault? _credentialVault = credentialVault;
    private readonly SshCredentialVaultStore? _credentialVaultStore = credentialVaultStore;

    public async ValueTask<Fin<SshCommandResult>> ExecuteAsync(ExecuteSshCommand command, CancellationToken ct)
    {
        try
        {
            var profile = ResolveProfile(command.Profile);
            if (profile.IsFail)
            {
                return PropagateFailure<SshCommandResult>(profile);
            }

            var resolvedProfile = profile.Match(
                Succ: value => value,
                Fail: _ => throw new InvalidOperationException("Expected SSH profile resolution to succeed."));

            var commandValidation = ValidateCommand(resolvedProfile, command);
            if (commandValidation.IsFail)
            {
                return PropagateFailure<SshCommandResult>(commandValidation);
            }

            var timeoutSeconds = Math.Clamp(command.TimeoutSeconds, MinimumTimeoutSeconds, MaximumTimeoutSeconds);
            var maxOutputChars = Math.Clamp(command.MaxOutputChars, MinimumOutputChars, MaximumOutputChars);
            var workingDirectory = string.IsNullOrWhiteSpace(command.WorkingDirectory)
                ? resolvedProfile.WorkingDirectory ?? string.Empty
                : command.WorkingDirectory;

            var executionResult = await Task.Run(() =>
            {
                using var client = CreateSshClient(resolvedProfile, timeoutSeconds);
                client.Connect();

                using var sshCommand = client.CreateCommand(BuildRemoteCommand(command.Command, command.Args, workingDirectory));
                sshCommand.CommandTimeout = TimeSpan.FromSeconds(timeoutSeconds);

                try
                {
                    var stdout = sshCommand.Execute();
                    var stderr = sshCommand.Error;
                    var outputTruncated = false;

                    stdout = Truncate(stdout, maxOutputChars, ref outputTruncated);
                    stderr = Truncate(stderr, maxOutputChars, ref outputTruncated);

                    return new SshCommandResult(
                        resolvedProfile.Name,
                        resolvedProfile.Host,
                        resolvedProfile.Port,
                        resolvedProfile.Username,
                        command.Command,
                        string.IsNullOrWhiteSpace(workingDirectory) ? "." : workingDirectory,
                        sshCommand.ExitStatus ?? -1,
                        stdout,
                        stderr,
                        TimedOut: false,
                        OutputTruncated: outputTruncated);
                }
                catch (SshOperationTimeoutException ex)
                {
                    return new SshCommandResult(
                        resolvedProfile.Name,
                        resolvedProfile.Host,
                        resolvedProfile.Port,
                        resolvedProfile.Username,
                        command.Command,
                        string.IsNullOrWhiteSpace(workingDirectory) ? "." : workingDirectory,
                        ExitCode: -1,
                        StandardOutput: string.Empty,
                        StandardError: ex.Message,
                        TimedOut: true,
                        OutputTruncated: false);
                }
            }, ct).ConfigureAwait(false);

            logger.LogInformation(
                "Executed SSH command via profile {Profile} on {Host}:{Port} with exit code {ExitCode}",
                executionResult.Profile,
                executionResult.Host,
                executionResult.Port,
                executionResult.ExitCode);

            return executionResult;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed executing SSH command via profile {Profile}", command.Profile);
            return Error.New(ex.Message);
        }
    }

    public async ValueTask<Fin<SshFileWriteResult>> WriteTextAsync(WriteSshTextCommand command, CancellationToken ct)
    {
        try
        {
            var profile = ResolveProfile(command.Profile);
            if (profile.IsFail)
            {
                return PropagateFailure<SshFileWriteResult>(profile);
            }

            var resolvedProfile = profile.Match(
                Succ: value => value,
                Fail: _ => throw new InvalidOperationException("Expected SSH profile resolution to succeed."));

            var writeResult = await Task.Run(() =>
            {
                using var client = CreateSftpClient(resolvedProfile, timeoutSeconds: 60);
                client.Connect();

                var remotePath = NormalizeRemotePath(command.Path);
                if (string.IsNullOrWhiteSpace(remotePath))
                {
                    throw new InvalidOperationException("Remote path is required.");
                }

                ValidateRemoteWritePath(resolvedProfile, remotePath);

                var remoteDirectory = GetRemoteDirectory(remotePath);
                if (command.CreateDirectories && !string.IsNullOrWhiteSpace(remoteDirectory))
                {
                    EnsureDirectoryExists(client, remoteDirectory);
                }

                var existed = client.Exists(remotePath);
                if (existed && !command.Overwrite)
                {
                    throw new InvalidOperationException($"Remote path already exists: {remotePath}");
                }

                var encoding = Encoding.GetEncoding(command.Encoding);
                var bytes = encoding.GetBytes(command.Content);
                using var ms = new MemoryStream(bytes, writable: false);
                client.UploadFile(ms, remotePath, canOverride: command.Overwrite);

                if (!string.IsNullOrWhiteSpace(command.Permissions))
                {
                    var permissionsApplied = NormalizePermissions(command.Permissions);
                    client.ChangePermissions(remotePath, Convert.ToInt16(permissionsApplied, 8));
                }

                return new SshFileWriteResult(
                    resolvedProfile.Name,
                    resolvedProfile.Host,
                    resolvedProfile.Port,
                    resolvedProfile.Username,
                    remotePath,
                    bytes.LongLength,
                    Success: true);
            }, ct).ConfigureAwait(false);

            logger.LogInformation(
                "Wrote remote file {Path} via SSH profile {Profile}",
                writeResult.Path,
                writeResult.Profile);

            return writeResult;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed writing remote file via SSH profile {Profile}", command.Profile);
            return Error.New(ex.Message);
        }
    }

    private Fin<ConfiguredSshProfile> ResolveProfile(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return Error.New("SSH profile is required.");
        }

        if (!_profiles.TryGetValue(profileName, out var profile))
        {
            return Error.New($"Unknown SSH profile: {profileName}");
        }

        if (string.IsNullOrWhiteSpace(profile.Host))
        {
            return Error.New($"SSH profile '{profileName}' is missing Host.");
        }

        if (string.IsNullOrWhiteSpace(profile.Username))
        {
            return Error.New($"SSH profile '{profileName}' is missing Username.");
        }

        if (!profile.AcceptUnknownHostKey && string.IsNullOrWhiteSpace(profile.HostKeySha256))
        {
            return Error.New($"SSH profile '{profileName}' must pin HostKeySha256 or explicitly set AcceptUnknownHostKey=true.");
        }

        return profile;
    }

    private SshClient CreateSshClient(ConfiguredSshProfile profile, int timeoutSeconds)
    {
        var connectionInfo = CreateConnectionInfo(profile, timeoutSeconds);
        var client = new SshClient(connectionInfo);
        ConfigureHostKeyValidation(client, profile);
        return client;
    }

    private SftpClient CreateSftpClient(ConfiguredSshProfile profile, int timeoutSeconds)
    {
        var connectionInfo = CreateConnectionInfo(profile, timeoutSeconds);
        var client = new SftpClient(connectionInfo);
        ConfigureHostKeyValidation(client, profile);
        return client;
    }

    private ConnectionInfo CreateConnectionInfo(ConfiguredSshProfile profile, int timeoutSeconds)
    {
        List<AuthenticationMethod> authMethods = [];

        if (!string.IsNullOrWhiteSpace(profile.PasswordVaultItemName))
        {
            if (_credentialVaultStore is null)
            {
                throw new InvalidOperationException(
                    $"SSH profile '{profile.Name}' uses a vault item reference, but no SSH credential vault store is configured.");
            }

            var password = _credentialVaultStore.ResolveSecret(profile.PasswordVaultItemName);
            authMethods.Add(new PasswordAuthenticationMethod(profile.Username, password));
        }
        else
        if (profile.PasswordSecret is not null)
        {
            if (_credentialVault is null)
            {
                throw new InvalidOperationException(
                    $"SSH profile '{profile.Name}' uses an encrypted password secret, but no SSH credential vault is configured.");
            }

            var password = _credentialVault.Unprotect(profile.PasswordSecret);
            authMethods.Add(new PasswordAuthenticationMethod(profile.Username, password));
        }
        else if (!string.IsNullOrWhiteSpace(profile.PasswordEnvironmentVariable))
        {
            var password = Environment.GetEnvironmentVariable(profile.PasswordEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException(
                    $"Environment variable '{profile.PasswordEnvironmentVariable}' for SSH profile '{profile.Name}' is not set.");
            }

            authMethods.Add(new PasswordAuthenticationMethod(profile.Username, password));
        }

        if (!string.IsNullOrWhiteSpace(profile.PrivateKeyPath))
        {
            var privateKeyPath = ResolvePath(profile.PrivateKeyPath);
            if (!File.Exists(privateKeyPath))
            {
                throw new FileNotFoundException($"SSH private key was not found: {privateKeyPath}", privateKeyPath);
            }

            var passphrase = string.IsNullOrWhiteSpace(profile.PrivateKeyPassphraseEnvironmentVariable)
                ? null
                : Environment.GetEnvironmentVariable(profile.PrivateKeyPassphraseEnvironmentVariable);

            var privateKeyFile = string.IsNullOrWhiteSpace(passphrase)
                ? new PrivateKeyFile(privateKeyPath)
                : new PrivateKeyFile(privateKeyPath, passphrase);

            authMethods.Add(new PrivateKeyAuthenticationMethod(profile.Username, privateKeyFile));
        }

        if (authMethods.Count is 0)
        {
            throw new InvalidOperationException(
                $"SSH profile '{profile.Name}' must define PasswordVaultItemName, PasswordSecret, PasswordEnvironmentVariable, or PrivateKeyPath.");
        }

        return new ConnectionInfo(profile.Host, profile.Port, profile.Username, authMethods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };
    }

    private void ConfigureHostKeyValidation(BaseClient client, ConfiguredSshProfile profile)
    {
        client.HostKeyReceived += (_, args) =>
        {
            args.CanTrust = profile.AcceptUnknownHostKey || HostKeyMatches(profile.HostKeySha256, args.HostKey);
        };
    }

    private static bool HostKeyMatches(string? expectedFingerprint, byte[] hostKey)
    {
        if (string.IsNullOrWhiteSpace(expectedFingerprint))
        {
            return false;
        }

        var normalizedExpected = expectedFingerprint
            .Trim()
            .Replace("SHA256:", string.Empty, StringComparison.OrdinalIgnoreCase)
            .TrimEnd('=');
        var computed = Convert.ToBase64String(SHA256.HashData(hostKey)).TrimEnd('=');
        return string.Equals(normalizedExpected, computed, StringComparison.Ordinal);
    }

    private string ResolvePath(string path) =>
        Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(contentRoot, path));

    private static string BuildRemoteCommand(string command, IReadOnlyList<string>? args, string? workingDirectory)
    {
        var invocation = args is { Count: > 0 }
            ? BuildExecutableInvocation(command, args)
            : command;

        var script = string.IsNullOrWhiteSpace(workingDirectory)
            ? invocation
            : $"cd {QuotePosix(workingDirectory)} && {invocation}";

        return $"sh -lc {QuotePosix(script)}";
    }

    private static string BuildExecutableInvocation(string command, IReadOnlyList<string> args) =>
        "exec " + string.Join(" ", new[] { command }.Concat(args).Select(QuotePosix));

    private static string QuotePosix(string value) =>
        $"'{value.Replace("'", "'\"'\"'")}'";

    private static Fin<Unit> ValidateCommand(ConfiguredSshProfile profile, ExecuteSshCommand command)
    {
        var errors = new List<string>();

        if (command.Args is { Length: > 0 })
        {
            ToolRequestValidation.RequireExecutableOnly(errors, command.Command, "command");
            ToolRequestValidation.RequireSafeArgumentValues(errors, command.Args, "args");
        }
        else
        {
            ToolRequestValidation.RequireShellSafeCommandText(errors, command.Command, "command");
        }

        if (errors.Count > 0)
        {
            return Error.New(string.Join(" ", errors));
        }

        var executable = ExtractExecutableName(command.Command);
        var sudoAllowed = IsSudoCommand(executable) && profile.AllowSudoCommand;
        var deniedCommands = new System.Collections.Generic.HashSet<string>(
            profile.DeniedCommands.Where(static value => !string.IsNullOrWhiteSpace(value)),
            StringComparer.OrdinalIgnoreCase);
        if (!sudoAllowed && deniedCommands.Contains(executable))
        {
            return Error.New($"SSH command '{executable}' is denied by profile '{profile.Name}'.");
        }

        var allowedCommands = new System.Collections.Generic.HashSet<string>(
            profile.AllowedCommands.Where(static value => !string.IsNullOrWhiteSpace(value)),
            StringComparer.OrdinalIgnoreCase);
        if (allowedCommands.Count == 0)
        {
            return Error.New($"SSH profile '{profile.Name}' requires an explicit command allowlist.");
        }

        if (!allowedCommands.Contains(executable) && !sudoAllowed)
        {
            return Error.New($"SSH command '{executable}' is not in the allowlist for profile '{profile.Name}'.");
        }

        return LanguageExt.Prelude.unit;
    }

    private static bool IsSudoCommand(string executable) =>
        executable.Equals("sudo", StringComparison.OrdinalIgnoreCase);

    private static string ExtractExecutableName(string command)
    {
        var first = command.Trim()
            .Split([' ', '	'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? command;
        return Path.GetFileNameWithoutExtension(first.Trim('\"', '\'')).ToLowerInvariant();
    }

    private static void ValidateRemoteWritePath(ConfiguredSshProfile profile, string remotePath)
    {
        if (ContainsProtectedRemoteSegment(remotePath))
        {
            throw new InvalidOperationException($"Refusing to write protected remote path: {remotePath}");
        }

        var allowedPrefixes = profile.AllowedRemotePathPrefixes
            .Where(static prefix => !string.IsNullOrWhiteSpace(prefix))
            .Select(NormalizeRemotePrefix)
            .ToArray();

        if (allowedPrefixes.Length == 0 && !string.IsNullOrWhiteSpace(profile.WorkingDirectory))
        {
            allowedPrefixes = [NormalizeRemotePrefix(profile.WorkingDirectory)];
        }

        if (allowedPrefixes.Length == 0)
        {
            throw new InvalidOperationException($"SSH profile '{profile.Name}' requires AllowedRemotePathPrefixes or WorkingDirectory before remote file writes are allowed.");
        }

        var normalizedRemotePath = NormalizeRemotePath(remotePath);
        if (!allowedPrefixes.Any(prefix => IsRemoteSameOrDescendant(normalizedRemotePath, prefix)))
        {
            throw new InvalidOperationException($"Remote path '{remotePath}' is outside allowed SSH write prefixes for profile '{profile.Name}'.");
        }
    }

    private static bool ContainsProtectedRemoteSegment(string remotePath)
    {
        var segments = NormalizeRemotePath(remotePath)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return segments.Any(static segment =>
            segment.Equals(".ssh", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".svn", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".hg", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeRemotePrefix(string prefix) =>
        NormalizeRemotePath(prefix).TrimEnd('/');

    private static bool IsRemoteSameOrDescendant(string path, string prefix) =>
        string.Equals(path, prefix, StringComparison.Ordinal) ||
        path.StartsWith(prefix + "/", StringComparison.Ordinal);

    private static string Truncate(string text, int maxChars, ref bool truncated)
    {
        if (text.Length <= maxChars)
        {
            return text;
        }

        truncated = true;
        const string suffix = "\n...[truncated]";
        var take = Math.Max(0, maxChars - suffix.Length);
        return text[..take] + suffix;
    }

    private static string NormalizeRemotePath(string path) => path.Replace('\\', '/').Trim();

    private static string GetRemoteDirectory(string path)
    {
        var index = path.LastIndexOf('/');
        return index <= 0 ? string.Empty : path[..index];
    }

    private static bool EnsureDirectoryExists(SftpClient client, string directory)
    {
        var normalized = NormalizeRemotePath(directory);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var createdAny = false;
        var startsAtRoot = normalized.StartsWith("/", StringComparison.Ordinal);
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = startsAtRoot ? "/" : string.Empty;

        foreach (var segment in segments)
        {
            current = current switch
            {
                "/" => "/" + segment,
                "" => segment,
                _ => current + "/" + segment
            };

            if (!client.Exists(current))
            {
                client.CreateDirectory(current);
                createdAny = true;
            }
        }

        return createdAny;
    }

    private static string NormalizePermissions(string permissions)
    {
        var normalized = permissions.Trim();
        if (normalized.Length is < 3 or > 4 || normalized.Any(ch => ch is < '0' or > '7'))
        {
            throw new InvalidOperationException($"Invalid octal permissions value: {permissions}");
        }

        return normalized;
    }

    private static Fin<T> PropagateFailure<T>(Fin<ConfiguredSshProfile> failure) =>
        failure.Match<Fin<T>>(
            Succ: _ => throw new InvalidOperationException("Expected SSH profile resolution to fail."),
            Fail: error => error);

    private static Fin<T> PropagateFailure<T>(Fin<Unit> failure) =>
        failure.Match<Fin<T>>(
            Succ: _ => throw new InvalidOperationException("Expected command validation to fail."),
            Fail: error => error);
}
