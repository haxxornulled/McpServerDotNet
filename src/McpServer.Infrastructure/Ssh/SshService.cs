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
    SshCredentialVaultStore? credentialVaultStore = null) : ISshService
{
    private const int MinimumTimeoutSeconds = 1;
    private const int MaximumTimeoutSeconds = 1800;
    private const int MinimumOutputChars = 256;
    private const int MaximumOutputChars = 200000;

    private readonly Dictionary<string, ConfiguredSshProfile> _profiles = profiles
        .Where(static profile => !string.IsNullOrWhiteSpace(profile.Name))
        .ToDictionary(static profile => profile.Name, StringComparer.OrdinalIgnoreCase);
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

            SshAuthenticationException? authFailure = null;
            foreach (var attempt in CreateConnectionAttempts(resolvedProfile, timeoutSeconds))
            {
                try
                {
                    var executionResult = await Task.Run(() =>
                        ExecuteWithConnectionInfo(
                            resolvedProfile,
                            attempt.ConnectionInfo,
                            command,
                            workingDirectory,
                            timeoutSeconds,
                            maxOutputChars), ct).ConfigureAwait(false);

                    logger.LogInformation(
                        "Executed SSH command via profile {Profile} on {Host}:{Port} with exit code {ExitCode}",
                        executionResult.Profile,
                        executionResult.Host,
                        executionResult.Port,
                        executionResult.ExitCode);

                    return executionResult;
                }
                catch (SshAuthenticationException ex)
                {
                    authFailure = ex;
                    logger.LogWarning(
                        ex,
                        "SSH authentication attempt {Attempt} failed for profile {Profile} on {Host}:{Port}",
                        attempt.Description,
                        resolvedProfile.Name,
                        resolvedProfile.Host,
                        resolvedProfile.Port);
                }
            }

            if (authFailure is not null)
            {
                return Error.New($"SSH authentication failed for profile '{command.Profile}': {authFailure.Message}");
            }

            return Error.New($"SSH profile '{command.Profile}' did not provide a usable authentication method.");
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

            SshAuthenticationException? authFailure = null;
            foreach (var attempt in CreateConnectionAttempts(resolvedProfile, timeoutSeconds: 60))
            {
                try
                {
                    var writeResult = await Task.Run(() =>
                        WriteTextWithConnectionInfo(
                            resolvedProfile,
                            attempt.ConnectionInfo,
                            command), ct).ConfigureAwait(false);

                    logger.LogInformation(
                        "Wrote remote file {Path} via SSH profile {Profile}",
                        writeResult.Path,
                        writeResult.Profile);

                    return writeResult;
                }
                catch (SshAuthenticationException ex)
                {
                    authFailure = ex;
                    logger.LogWarning(
                        ex,
                        "SSH authentication attempt {Attempt} failed for profile {Profile} on {Host}:{Port}",
                        attempt.Description,
                        resolvedProfile.Name,
                        resolvedProfile.Host,
                        resolvedProfile.Port);
                }
            }

            if (authFailure is not null)
            {
                return Error.New($"SSH authentication failed for profile '{command.Profile}': {authFailure.Message}");
            }

            return Error.New($"SSH profile '{command.Profile}' did not provide a usable authentication method.");
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

    private IReadOnlyList<SshConnectionAttempt> CreateConnectionAttempts(ConfiguredSshProfile profile, int timeoutSeconds)
    {
        List<SshConnectionAttempt> attempts = [];

        if (!string.IsNullOrWhiteSpace(profile.PrivateKeyPath))
        {
            var privateKeyPath = ResolvePath(profile.PrivateKeyPath);
            if (!File.Exists(privateKeyPath))
            {
                throw new FileNotFoundException($"SSH private key was not found: {privateKeyPath}", privateKeyPath);
            }

            var passphrase = string.IsNullOrWhiteSpace(profile.PrivateKeyPassphraseVaultItemName)
                ? null
                : ResolveRequiredVaultSecret(profile.PrivateKeyPassphraseVaultItemName, "private key passphrase");
            var privateKeyFile = string.IsNullOrWhiteSpace(passphrase)
                ? new PrivateKeyFile(privateKeyPath)
                : new PrivateKeyFile(privateKeyPath, passphrase);

            attempts.Add(new SshConnectionAttempt(
                CreateConnectionInfo(profile, timeoutSeconds, [new PrivateKeyAuthenticationMethod(profile.Username, privateKeyFile)]),
                "private-key"));
        }

        if (!string.IsNullOrWhiteSpace(profile.PasswordVaultItemName))
        {
            if (_credentialVaultStore is null)
            {
                throw new InvalidOperationException(
                    $"SSH profile '{profile.Name}' uses a vault item reference, but no SSH credential vault store is configured.");
            }

            var password = ResolveRequiredVaultSecret(profile.PasswordVaultItemName, "password");
            attempts.Add(new SshConnectionAttempt(
                CreateConnectionInfo(profile, timeoutSeconds, [CreateKeyboardInteractiveAuthenticationMethod(profile.Username, password)]),
                "keyboard-interactive"));
            attempts.Add(new SshConnectionAttempt(
                CreateConnectionInfo(profile, timeoutSeconds, [CreatePasswordAuthenticationMethod(profile.Username, password)]),
                "password"));
        }

        if (attempts.Count is 0)
        {
            throw new InvalidOperationException(
                $"SSH profile '{profile.Name}' must define PasswordVaultItemName and/or PrivateKeyPath.");
        }

        return attempts;
    }

    private static ConnectionInfo CreateConnectionInfo(
        ConfiguredSshProfile profile,
        int timeoutSeconds,
        IEnumerable<AuthenticationMethod> authMethods) =>
        new(profile.Host, profile.Port, profile.Username, authMethods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };

    private SshClient CreateSshClient(ConnectionInfo connectionInfo, ConfiguredSshProfile profile)
    {
        var client = new SshClient(connectionInfo);
        ConfigureHostKeyValidation(client, profile);
        return client;
    }

    private SftpClient CreateSftpClient(ConnectionInfo connectionInfo, ConfiguredSshProfile profile)
    {
        var client = new SftpClient(connectionInfo);
        ConfigureHostKeyValidation(client, profile);
        return client;
    }

    private SshCommandResult ExecuteWithConnectionInfo(
        ConfiguredSshProfile profile,
        ConnectionInfo connectionInfo,
        ExecuteSshCommand command,
        string workingDirectory,
        int timeoutSeconds,
        int maxOutputChars)
    {
        using var client = CreateSshClient(connectionInfo, profile, out var hostKeyValidation);
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        try
        {
            client.Connect();
        }
        catch (Exception ex) when (hostKeyValidation.WasRejected && ex is SshException)
        {
            throw new SshException(hostKeyValidation.CreateFailureMessage(profile), ex);
        }

        try
        {
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
                    profile.Name,
                    profile.Host,
                    profile.Port,
                    profile.Username,
                    command.Command,
                    string.IsNullOrWhiteSpace(workingDirectory) ? "." : workingDirectory,
                    sshCommand.ExitStatus.GetValueOrDefault(-1),
                    stdout,
                    stderr,
                    TimedOut: false,
                    OutputTruncated: outputTruncated);
            }
            catch (SshOperationTimeoutException ex)
            {
                return new SshCommandResult(
                    profile.Name,
                    profile.Host,
                    profile.Port,
                    profile.Username,
                    command.Command,
                    string.IsNullOrWhiteSpace(workingDirectory) ? "." : workingDirectory,
                    ExitCode: -1,
                    StandardOutput: string.Empty,
                    StandardError: ex.Message,
                    TimedOut: true,
                    OutputTruncated: false);
            }
        }
        finally
        {
            if (client.IsConnected)
            {
                client.Disconnect();
            }
        }
    }

    private SshFileWriteResult WriteTextWithConnectionInfo(
        ConfiguredSshProfile profile,
        ConnectionInfo connectionInfo,
        WriteSshTextCommand command)
    {
        using var client = CreateSftpClient(connectionInfo, profile, out var hostKeyValidation);
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(60);
        try
        {
            client.Connect();
        }
        catch (Exception ex) when (hostKeyValidation.WasRejected && ex is SshException)
        {
            throw new SshException(hostKeyValidation.CreateFailureMessage(profile), ex);
        }

        try
        {
            var remotePath = NormalizeRemotePath(command.Path);
            if (string.IsNullOrWhiteSpace(remotePath))
            {
                throw new InvalidOperationException("Remote path is required.");
            }

            ValidateRemoteWritePath(profile, remotePath);

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
                profile.Name,
                profile.Host,
                profile.Port,
                profile.Username,
                remotePath,
                bytes.LongLength,
                Success: true);
        }
        finally
        {
            if (client.IsConnected)
            {
                client.Disconnect();
            }
        }
    }

    private sealed record SshConnectionAttempt(
        ConnectionInfo ConnectionInfo,
        string Description);

    private string ResolveRequiredVaultSecret(string? itemName, string purpose)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            throw new InvalidOperationException($"SSH {purpose} vault item name is missing.");
        }

        if (_credentialVaultStore is null)
        {
            throw new InvalidOperationException(
                $"SSH {purpose} vault item '{itemName}' was requested, but no SSH credential vault store is configured.");
        }

        var secret = _credentialVaultStore.ResolveSecret(itemName);
        if (string.IsNullOrEmpty(secret))
        {
            throw new InvalidOperationException(
                $"SSH {purpose} vault item '{itemName}' resolved to an empty secret. Check that the credential source is visible to this process.");
        }

        return secret;
    }

    private SshClient CreateSshClient(ConnectionInfo connectionInfo, ConfiguredSshProfile profile, out SshHostKeyValidationState hostKeyValidation)
    {
        hostKeyValidation = new SshHostKeyValidationState();
        var capturedHostKeyValidation = hostKeyValidation;
        var client = new SshClient(connectionInfo);
        client.HostKeyReceived += (_, args) =>
        {
            args.CanTrust = profile.AcceptUnknownHostKey || HostKeyMatches(profile.HostKeySha256, args.HostKey);

            if (!args.CanTrust)
            {
                capturedHostKeyValidation.MarkRejected(
                    $"SHA256:{ComputeHostKeyFingerprint(args.HostKey)}",
                    $"SHA256:{NormalizeHostKeyFingerprint(profile.HostKeySha256)}");
            }
        };

        return client;
    }

    private SftpClient CreateSftpClient(ConnectionInfo connectionInfo, ConfiguredSshProfile profile, out SshHostKeyValidationState hostKeyValidation)
    {
        hostKeyValidation = new SshHostKeyValidationState();
        var capturedHostKeyValidation = hostKeyValidation;
        var client = new SftpClient(connectionInfo);
        client.HostKeyReceived += (_, args) =>
        {
            args.CanTrust = profile.AcceptUnknownHostKey || HostKeyMatches(profile.HostKeySha256, args.HostKey);

            if (!args.CanTrust)
            {
                capturedHostKeyValidation.MarkRejected(
                    $"SHA256:{ComputeHostKeyFingerprint(args.HostKey)}",
                    $"SHA256:{NormalizeHostKeyFingerprint(profile.HostKeySha256)}");
            }
        };

        return client;
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

    private static string ComputeHostKeyFingerprint(byte[] hostKey) =>
        Convert.ToBase64String(SHA256.HashData(hostKey)).TrimEnd('=');

    private static string NormalizeHostKeyFingerprint(string? expectedFingerprint)
    {
        if (string.IsNullOrWhiteSpace(expectedFingerprint))
        {
            return "not_configured";
        }

        var normalized = expectedFingerprint.Trim();
        if (normalized.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[7..];
        }

        return normalized.TrimEnd('=');
    }

    private sealed class SshHostKeyValidationState
    {
        public bool WasRejected { get; private set; }

        public string ExpectedSha256 { get; private set; } = string.Empty;

        public string ActualSha256 { get; private set; } = string.Empty;

        public void MarkRejected(string expectedSha256, string actualSha256)
        {
            WasRejected = true;
            ExpectedSha256 = expectedSha256;
            ActualSha256 = actualSha256;
        }

        public string CreateFailureMessage(ConfiguredSshProfile profile)
        {
            return $"SSH host key mismatch for profile '{profile.Name}' host '{profile.Host}'. Expected {ExpectedSha256}; actual {ActualSha256}.";
        }
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

    private static AuthenticationMethod CreatePasswordAuthenticationMethod(string username, string password) =>
        new PasswordAuthenticationMethod(username, password);

    private static AuthenticationMethod CreateKeyboardInteractiveAuthenticationMethod(string username, string password)
    {
        var method = new KeyboardInteractiveAuthenticationMethod(username);
        method.AuthenticationPrompt += (_, e) =>
        {
            foreach (var prompt in e.Prompts)
            {
                prompt.Response = password;
            }
        };

        return method;
    }

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
