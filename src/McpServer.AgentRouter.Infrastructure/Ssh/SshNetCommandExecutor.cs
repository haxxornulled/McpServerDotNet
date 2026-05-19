using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using LanguageExt;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Domain.Ssh;
using McpServer.Infrastructure.Ssh;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace McpServer.AgentRouter.Infrastructure.Ssh;

public sealed class SshNetCommandExecutor : ISshCommandExecutor
{
    private readonly IAgentRouterRuntimePathResolver _pathResolver;
    private readonly SshCredentialVaultStore _credentialVaultStore;
    private readonly ILogger<SshNetCommandExecutor> _logger;

    public SshNetCommandExecutor(
        IAgentRouterRuntimePathResolver pathResolver,
        SshCredentialVaultStore credentialVaultStore,
        ILogger<SshNetCommandExecutor> logger)
    {
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _credentialVaultStore = credentialVaultStore ?? throw new ArgumentNullException(nameof(credentialVaultStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<Fin<SshCommandExecutionResult>> ExecuteAsync(
        SshExecutionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(command.ProfileName))
        {
            return LanguageExt.Common.Error.New("SSH profile is required.");
        }

        if (string.IsNullOrWhiteSpace(command.Host))
        {
            return LanguageExt.Common.Error.New($"SSH profile '{command.ProfileName}' is missing host.");
        }

        if (string.IsNullOrWhiteSpace(command.Username))
        {
            return LanguageExt.Common.Error.New($"SSH profile '{command.ProfileName}' is missing Username.");
        }

        try
        {
            SshAuthenticationException? authFailure = null;
            foreach (var attempt in CreateConnectionAttempts(command))
            {
                try
                {
                    return await Task.Run(() => ExecuteSshCommand(command, attempt.ConnectionInfo, cancellationToken), cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (SshAuthenticationException ex)
                {
                    authFailure = ex;
                    _logger.LogWarning(
                        ex,
                        "SSH authentication attempt {Attempt} failed for profile {Profile} on {Host}:{Port}",
                        attempt.Description,
                        command.ProfileName,
                        command.Host,
                        command.Port);
                }
            }

            if (authFailure is not null)
            {
                return LanguageExt.Common.Error.New(
                    $"SSH authentication failed for profile '{command.ProfileName}': {authFailure.Message}");
            }

            return LanguageExt.Common.Error.New(
                $"SSH profile '{command.ProfileName}' did not provide a usable authentication method.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SshAuthenticationException ex)
        {
            return LanguageExt.Common.Error.New($"SSH authentication failed for profile '{command.ProfileName}': {ex.Message}");
        }
        catch (SshOperationTimeoutException ex)
        {
            return LanguageExt.Common.Error.New($"SSH command timed out for profile '{command.ProfileName}' after {command.TimeoutSeconds}s: {ex.Message}");
        }
        catch (SshConnectionException ex)
        {
            return LanguageExt.Common.Error.New($"SSH connection failed for profile '{command.ProfileName}': {ex.Message}");
        }
        catch (SocketException ex)
        {
            return LanguageExt.Common.Error.New($"SSH connection failed for profile '{command.ProfileName}': {ex.Message}");
        }
        catch (SshException ex) when (ex.Message.Contains("host key mismatch", StringComparison.OrdinalIgnoreCase))
        {
            return LanguageExt.Common.Error.New($"SSH host key mismatch for profile '{command.ProfileName}': {ex.Message}");
        }
        catch (Exception ex)
        {
            return LanguageExt.Common.Error.New($"Failed to execute SSH command via profile '{command.ProfileName}': {ex.Message}");
        }
    }

    private SshCommandExecutionResult ExecuteSshCommand(
        SshExecutionCommand command,
        ConnectionInfo connectionInfo,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();

        using var client = CreateClient(command, connectionInfo, out var hostKeyValidation);
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(Math.Max(1, command.TimeoutSeconds));
        try
        {
            client.Connect();
        }
        catch (Exception ex) when (hostKeyValidation.WasRejected && ex is SshException)
        {
            throw new SshException(hostKeyValidation.CreateFailureMessage(command), ex);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var sshCommand = client.CreateCommand(BuildRemoteCommand(command));
            sshCommand.CommandTimeout = TimeSpan.FromSeconds(Math.Max(1, command.TimeoutSeconds));

            var stdout = sshCommand.Execute();
            var stderr = sshCommand.Error;
            stopwatch.Stop();

            var stdoutTruncated = false;
            var stderrTruncated = false;
            stdout = Truncate(stdout, command.MaxOutputChars, ref stdoutTruncated);
            stderr = Truncate(stderr, command.MaxOutputChars, ref stderrTruncated);

            return new SshCommandExecutionResult
            {
                ExitCode = sshCommand.ExitStatus.GetValueOrDefault(-1),
                Stdout = stdout,
                Stderr = stderr,
                StdoutTruncated = stdoutTruncated,
                StderrTruncated = stderrTruncated,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
            };
        }
        catch (SshOperationTimeoutException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                "SSH command via profile {Profile} timed out after {TimeoutSeconds}s.",
                command.ProfileName,
                command.TimeoutSeconds);

            return new SshCommandExecutionResult
            {
                ExitCode = -1,
                TimedOut = true,
                Stderr = ex.Message,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
            };
        }
        finally
        {
            if (client.IsConnected)
            {
                client.Disconnect();
            }
        }
    }

    private SshClient CreateClient(
        SshExecutionCommand command,
        ConnectionInfo connectionInfo,
        out SshHostKeyValidationState hostKeyValidation)
    {
        hostKeyValidation = new SshHostKeyValidationState();
        var capturedHostKeyValidation = hostKeyValidation;
        var client = new SshClient(connectionInfo);
        client.HostKeyReceived += (_, args) =>
        {
            var decision = EvaluateHostKey(command, args.HostKey);
            args.CanTrust = decision.Trusted;

            if (!decision.Trusted)
            {
                capturedHostKeyValidation.MarkRejected(decision.ExpectedSha256, decision.ActualSha256);
            }
        };

        return client;
    }

    private IReadOnlyList<SshConnectionAttempt> CreateConnectionAttempts(SshExecutionCommand command)
    {
        var attempts = new List<SshConnectionAttempt>();

        if (!string.IsNullOrWhiteSpace(command.PrivateKeyPath))
        {
            var privateKeyPath = ResolveConfiguredPath(command.PrivateKeyPath);
            if (!File.Exists(privateKeyPath))
            {
                throw new InvalidOperationException($"Private key file was not found: {privateKeyPath}");
            }

            var passphrase = string.IsNullOrWhiteSpace(command.PrivateKeyPassphraseVaultItemName)
                ? null
                : ResolveRequiredVaultSecret(command.PrivateKeyPassphraseVaultItemName, "private key passphrase");
            PrivateKeyFile privateKey = string.IsNullOrEmpty(passphrase)
                ? new PrivateKeyFile(privateKeyPath)
                : new PrivateKeyFile(privateKeyPath, passphrase);

            attempts.Add(new SshConnectionAttempt(
                CreateConnectionInfo(command, [new PrivateKeyAuthenticationMethod(command.Username, privateKey)]),
                "private-key"));
        }

        if (!string.IsNullOrWhiteSpace(command.PasswordVaultItemName))
        {
            var password = ResolveRequiredVaultSecret(command.PasswordVaultItemName, "password");
            attempts.Add(new SshConnectionAttempt(
                CreateConnectionInfo(command, [CreateKeyboardInteractiveAuthenticationMethod(command.Username, password)]),
                "keyboard-interactive"));
            attempts.Add(new SshConnectionAttempt(
                CreateConnectionInfo(command, [CreatePasswordAuthenticationMethod(command.Username, password)]),
                "password"));
        }

        if (attempts.Count == 0)
        {
            throw new InvalidOperationException(
                $"SSH profile '{command.ProfileName}' must configure PasswordVaultItemName and/or PrivateKeyPath. Raw credentials are not accepted in requests.");
        }

        return attempts;
    }

    private static ConnectionInfo CreateConnectionInfo(
        SshExecutionCommand command,
        IEnumerable<AuthenticationMethod> authMethods) =>
        new(
            command.Host,
            command.Port <= 0 ? 22 : command.Port,
            command.Username,
            authMethods.ToArray());

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

    private static SshHostKeyDecision EvaluateHostKey(
        SshExecutionCommand command,
        byte[] hostKey)
    {
        var actual = "SHA256:" + ComputeSha256Fingerprint(hostKey);

        if (!string.IsNullOrWhiteSpace(command.HostKeySha256))
        {
            var expected = "SHA256:" + NormalizeSha256Fingerprint(command.HostKeySha256);
            return new SshHostKeyDecision(
                Trusted: string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
                ExpectedSha256: expected,
                ActualSha256: actual);
        }

        return new SshHostKeyDecision(
            Trusted: command.AcceptUnknownHostKey,
            ExpectedSha256: command.AcceptUnknownHostKey ? "accept_unknown" : "not_configured",
            ActualSha256: actual);
    }

    private static string ComputeSha256Fingerprint(byte[] hostKey)
    {
        var hash = SHA256.HashData(hostKey);
        return Convert.ToBase64String(hash).TrimEnd('=');
    }

    private static string NormalizeSha256Fingerprint(string fingerprint)
    {
        var normalized = fingerprint.Trim();
        if (normalized.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[7..];
        }

        return normalized.TrimEnd('=');
    }

    private sealed record SshHostKeyDecision(
        bool Trusted,
        string ExpectedSha256,
        string ActualSha256);

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

        public string CreateFailureMessage(SshExecutionCommand command)
        {
            return $"SSH host key mismatch for profile '{command.ProfileName}' host '{command.Host}'. Expected {ExpectedSha256}; actual {ActualSha256}.";
        }
    }

    private static string BuildRemoteCommand(SshExecutionCommand command)
    {
        var executableAndArguments = new List<string>
        {
            QuoteForPosixShell(command.Command)
        };

        executableAndArguments.AddRange(command.Arguments.Select(QuoteForPosixShell));
        var commandLine = string.Join(" ", executableAndArguments);

        return string.IsNullOrWhiteSpace(command.WorkingDirectory)
            ? commandLine
            : $"cd {QuoteForPosixShell(command.WorkingDirectory)} && {commandLine}";
    }

    private static string QuoteForPosixShell(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "''";
        }

        return "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    private sealed record SshConnectionAttempt(
        ConnectionInfo ConnectionInfo,
        string Description);

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

    private static string Truncate(
        string? value,
        int maxChars,
        ref bool truncated)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var limit = Math.Max(1, maxChars);
        if (value.Length <= limit)
        {
            return value;
        }

        truncated = true;
        return value[..limit];
    }

    private string ResolveConfiguredPath(string path)
    {
        return _pathResolver.ResolveRelativeToContentRoot(path);
    }
}
