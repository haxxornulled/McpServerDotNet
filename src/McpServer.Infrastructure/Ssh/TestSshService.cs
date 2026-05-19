using System.Text;
using LanguageExt;
using LanguageExt.Common;
using McpServer.Application.Abstractions.Ssh;
using McpServer.Application.Mcp.Validation;
using McpServer.Application.Ssh.Commands;
using McpServer.Application.Ssh.Results;
using Microsoft.Extensions.Logging;

namespace McpServer.Infrastructure.Ssh;

public sealed class TestSshService(
    IEnumerable<ConfiguredSshProfile> profiles,
    string backendRoot,
    ILogger<TestSshService> logger) : ISshService
{
    private readonly Dictionary<string, ConfiguredSshProfile> _profiles = profiles
        .Where(static profile => !string.IsNullOrWhiteSpace(profile.Name))
        .ToDictionary(static profile => profile.Name, StringComparer.OrdinalIgnoreCase);

    private readonly string _backendRoot = string.IsNullOrWhiteSpace(backendRoot)
        ? Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mcpserver-ssh-test-backend"))
        : Path.GetFullPath(backendRoot);

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

            var validation = ValidateCommand(resolvedProfile, command);
            if (validation.IsFail)
            {
                return PropagateFailure<SshCommandResult>(validation);
            }

            var workingDirectory = string.IsNullOrWhiteSpace(command.WorkingDirectory)
                ? resolvedProfile.WorkingDirectory ?? string.Empty
                : command.WorkingDirectory;
            var displayWorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? "." : workingDirectory;

            var stdout = new StringBuilder()
                .AppendLine($"profile={resolvedProfile.Name}")
                .AppendLine($"host={resolvedProfile.Host}")
                .AppendLine($"port={resolvedProfile.Port}")
                .AppendLine($"username={resolvedProfile.Username}")
                .AppendLine($"command={command.Command}")
                .AppendLine($"workingDirectory={displayWorkingDirectory}")
                .ToString();

            logger.LogInformation(
                "Executed SSH test backend command via profile {Profile} on {Host}:{Port}",
                resolvedProfile.Name,
                resolvedProfile.Host,
                resolvedProfile.Port);

            await Task.CompletedTask.ConfigureAwait(false);

            return new SshCommandResult(
                resolvedProfile.Name,
                resolvedProfile.Host,
                resolvedProfile.Port,
                resolvedProfile.Username,
                command.Command,
                displayWorkingDirectory,
                0,
                stdout,
                string.Empty,
                TimedOut: false,
                OutputTruncated: false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed executing SSH test backend command via profile {Profile}", command.Profile);
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

            var remotePath = NormalizeRemotePath(command.Path);
            if (string.IsNullOrWhiteSpace(remotePath))
            {
                throw new InvalidOperationException("Remote path is required.");
            }

            ValidateRemoteWritePath(resolvedProfile, remotePath);

            var localPath = MapRemotePath(remotePath);
            var localDirectory = Path.GetDirectoryName(localPath);
            if (command.CreateDirectories && !string.IsNullOrWhiteSpace(localDirectory))
            {
                Directory.CreateDirectory(localDirectory);
            }

            if (File.Exists(localPath) && !command.Overwrite)
            {
                throw new InvalidOperationException($"Remote path already exists: {remotePath}");
            }

            var encoding = Encoding.GetEncoding(command.Encoding);
            var bytes = encoding.GetBytes(command.Content);
            await File.WriteAllBytesAsync(localPath, bytes, ct).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(command.Permissions))
            {
                NormalizePermissions(command.Permissions);
            }

            logger.LogInformation(
                "Wrote SSH test backend file {Path} via profile {Profile}",
                remotePath,
                resolvedProfile.Name);

            return new SshFileWriteResult(
                resolvedProfile.Name,
                resolvedProfile.Host,
                resolvedProfile.Port,
                resolvedProfile.Username,
                remotePath,
                bytes.LongLength,
                Success: true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed writing SSH test backend file via profile {Profile}", command.Profile);
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

        return profile;
    }

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
        var allowAllCommands = profile.AllowAllCommands;
        var sudoAllowed = IsSudoCommand(executable) && (profile.AllowSudoCommand || allowAllCommands);
        var deniedCommands = new System.Collections.Generic.HashSet<string>(
            profile.DeniedCommands.Where(static value => !string.IsNullOrWhiteSpace(value)),
            StringComparer.OrdinalIgnoreCase);
        if (!allowAllCommands && !sudoAllowed && deniedCommands.Contains(executable))
        {
            return Error.New($"SSH command '{executable}' is denied by profile '{profile.Name}'.");
        }

        if (allowAllCommands)
        {
            return LanguageExt.Prelude.unit;
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

    private string MapRemotePath(string remotePath)
    {
        var normalizedRemotePath = NormalizeRemotePath(remotePath).TrimStart('/');
        var relative = normalizedRemotePath.Replace('/', Path.DirectorySeparatorChar);
        var localPath = Path.GetFullPath(Path.Combine(_backendRoot, relative));
        var backendRootWithSeparator = _backendRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!string.Equals(localPath, _backendRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) &&
            !localPath.StartsWith(backendRootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Remote path '{remotePath}' resolves outside the SSH test backend root.");
        }

        return localPath;
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

    private static string NormalizeRemotePath(string path) => path.Replace('\\', '/').Trim();

    private static string NormalizePermissions(string permissions)
    {
        var normalized = permissions.Trim();
        if (normalized.Length is < 3 or > 4 || normalized.Any(ch => ch is < '0' or > '7'))
        {
            throw new InvalidOperationException($"Invalid octal permissions value: {permissions}");
        }

        return normalized;
    }

    private static string ExtractExecutableName(string command)
    {
        var first = command.Trim()
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? command;
        return Path.GetFileNameWithoutExtension(first.Trim('\"', '\'')).ToLowerInvariant();
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
