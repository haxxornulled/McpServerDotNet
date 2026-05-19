using System.Text.Json.Serialization;

namespace McpServer.AgentRouter.Domain.Ssh;

/// <summary>
/// Describes an SSH execution request.
/// </summary>
public sealed class SshExecutionRequest
{
    /// <summary>
    /// Gets or sets the SSH profile name to use.
    /// </summary>
    public string? Profile { get; set; }

    /// <summary>
    /// Gets or sets the command to execute.
    /// </summary>
    public string? Command { get; set; }

    /// <summary>
    /// Gets or sets the command arguments.
    /// </summary>
    public IList<string> Arguments { get; set; } = new List<string>();

    /// <summary>
    /// Gets or sets the working directory.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Gets or sets the timeout in seconds.
    /// </summary>
    public int? TimeoutSeconds { get; set; }
}

/// <summary>
/// Describes the response from an SSH execution.
/// </summary>
public sealed class SshExecutionResponse
{
    /// <summary>
    /// Gets or sets the execution identifier.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the object type for the response.
    /// </summary>
    public string Object { get; set; } = "ssh.execution";

    /// <summary>
    /// Gets or sets the execution status.
    /// </summary>
    public string Status { get; set; } = SshExecutionStatusNames.Pending;

    /// <summary>
    /// Gets or sets a value indicating whether the request was allowed.
    /// </summary>
    public bool Allowed { get; set; }

    /// <summary>
    /// Gets or sets the policy decision name.
    /// </summary>
    public string PolicyDecision { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the policy reason.
    /// </summary>
    public string? PolicyReason { get; set; }

    /// <summary>
    /// Gets or sets the SSH profile name.
    /// </summary>
    public string Profile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target host.
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target port.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Gets or sets the target username.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the executed command.
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the executed arguments.
    /// </summary>
    public IList<string> Arguments { get; set; } = new List<string>();

    /// <summary>
    /// Gets or sets the working directory.
    /// </summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the exit code, if available.
    /// </summary>
    public int? ExitCode { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the command timed out.
    /// </summary>
    public bool TimedOut { get; set; }

    /// <summary>
    /// Gets or sets the captured standard output.
    /// </summary>
    public string? Stdout { get; set; }

    /// <summary>
    /// Gets or sets the captured standard error.
    /// </summary>
    public string? Stderr { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether stdout was truncated.
    /// </summary>
    public bool StdoutTruncated { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether stderr was truncated.
    /// </summary>
    public bool StderrTruncated { get; set; }

    /// <summary>
    /// Gets or sets a short execution summary.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the trace identifier.
    /// </summary>
    public string TraceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the completion timestamp, if available.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Gets or sets the elapsed execution time in milliseconds.
    /// </summary>
    public long ElapsedMilliseconds { get; set; }
}

/// <summary>
/// Provides stable status names for SSH execution.
/// </summary>
public static class SshExecutionStatusNames
{
    /// <summary>Pending execution.</summary>
    public const string Pending = "pending";
    /// <summary>Denied by policy.</summary>
    public const string Denied = "denied";
    /// <summary>Execution completed.</summary>
    public const string Completed = "completed";
    /// <summary>Execution failed.</summary>
    public const string Failed = "failed";
    /// <summary>Execution timed out.</summary>
    public const string TimedOut = "timed_out";
}

/// <summary>
/// Represents a policy decision for SSH execution.
/// </summary>
public sealed class SshExecutionPolicyDecision
{
    /// <summary>
    /// Gets or sets a value indicating whether the command is allowed.
    /// </summary>
    public bool Allowed { get; set; }

    /// <summary>
    /// Gets or sets the policy decision name.
    /// </summary>
    public string Decision { get; set; } = "denied";

    /// <summary>
    /// Gets or sets the policy reason.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Gets or sets the SSH profile name.
    /// </summary>
    public string ProfileName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target host.
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target port.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Gets or sets the target username.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resolved command.
    /// </summary>
    public string ResolvedCommand { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resolved arguments.
    /// </summary>
    public IList<string> ResolvedArguments { get; set; } = new List<string>();

    /// <summary>
    /// Gets or sets the working directory.
    /// </summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; }

    /// <summary>
    /// Gets or sets the maximum output size in characters.
    /// </summary>
    public int MaxOutputChars { get; set; }

    /// <summary>
    /// Gets or sets the password environment variable name.
    /// </summary>
    public string? PasswordEnvironmentVariable { get; set; }

    /// <summary>
    /// Gets or sets the private key path.
    /// </summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>
    /// Gets or sets the private key passphrase environment variable name.
    /// </summary>
    public string? PrivateKeyPassphraseEnvironmentVariable { get; set; }

    /// <summary>
    /// Gets or sets the expected host key hash.
    /// </summary>
    public string? HostKeySha256 { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether unknown host keys are accepted.
    /// </summary>
    public bool AcceptUnknownHostKey { get; set; }
}

/// <summary>
/// Describes an internal SSH execution command.
/// </summary>
public sealed class SshExecutionCommand
{
    /// <summary>
    /// Gets or sets the SSH profile name.
    /// </summary>
    public string ProfileName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target host.
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target port.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Gets or sets the target username.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the command to execute.
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the command arguments.
    /// </summary>
    public IList<string> Arguments { get; set; } = new List<string>();

    /// <summary>
    /// Gets or sets the working directory.
    /// </summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; set; }

    /// <summary>
    /// Gets or sets the maximum output size in characters.
    /// </summary>
    public int MaxOutputChars { get; set; }

    /// <summary>
    /// Gets or sets the password environment variable name.
    /// </summary>
    public string? PasswordEnvironmentVariable { get; set; }

    /// <summary>
    /// Gets or sets the private key path.
    /// </summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>
    /// Gets or sets the private key passphrase environment variable name.
    /// </summary>
    public string? PrivateKeyPassphraseEnvironmentVariable { get; set; }

    /// <summary>
    /// Gets or sets the expected host key hash.
    /// </summary>
    public string? HostKeySha256 { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether unknown host keys are accepted.
    /// </summary>
    public bool AcceptUnknownHostKey { get; set; }
}

/// <summary>
/// Describes the raw result of an SSH command execution.
/// </summary>
public sealed class SshCommandExecutionResult
{
    /// <summary>
    /// Gets or sets the exit code.
    /// </summary>
    public int ExitCode { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the command timed out.
    /// </summary>
    public bool TimedOut { get; set; }

    /// <summary>
    /// Gets or sets the captured standard output.
    /// </summary>
    public string Stdout { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the captured standard error.
    /// </summary>
    public string Stderr { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether stdout was truncated.
    /// </summary>
    public bool StdoutTruncated { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether stderr was truncated.
    /// </summary>
    public bool StderrTruncated { get; set; }

    /// <summary>
    /// Gets or sets the elapsed execution time in milliseconds.
    /// </summary>
    public long ElapsedMilliseconds { get; set; }
}

/// <summary>
/// Captures the trace record for an SSH execution.
/// </summary>
public sealed class SshExecutionTraceRecord
{
    /// <summary>
    /// Gets or sets the trace identifier.
    /// </summary>
    [JsonPropertyName("trace_id")]
    public string TraceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the completion timestamp, if available.
    /// </summary>
    [JsonPropertyName("completed_at")]
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Gets or sets the execution status.
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";

    /// <summary>
    /// Gets or sets a value indicating whether the request was allowed.
    /// </summary>
    [JsonPropertyName("allowed")]
    public bool Allowed { get; set; }

    /// <summary>
    /// Gets or sets the policy decision name.
    /// </summary>
    [JsonPropertyName("policy_decision")]
    public string PolicyDecision { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the policy reason.
    /// </summary>
    [JsonPropertyName("policy_reason")]
    public string? PolicyReason { get; set; }

    /// <summary>
    /// Gets or sets the SSH profile name.
    /// </summary>
    [JsonPropertyName("profile")]
    public string Profile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target host.
    /// </summary>
    [JsonPropertyName("host")]
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target port.
    /// </summary>
    [JsonPropertyName("port")]
    public int Port { get; set; }

    /// <summary>
    /// Gets or sets the target username.
    /// </summary>
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the executed command.
    /// </summary>
    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the executed arguments.
    /// </summary>
    [JsonPropertyName("arguments")]
    public IList<string> Arguments { get; set; } = new List<string>();

    /// <summary>
    /// Gets or sets the working directory.
    /// </summary>
    [JsonPropertyName("working_directory")]
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the exit code, if available.
    /// </summary>
    [JsonPropertyName("exit_code")]
    public int? ExitCode { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the command timed out.
    /// </summary>
    [JsonPropertyName("timed_out")]
    public bool TimedOut { get; set; }

    /// <summary>
    /// Gets or sets the captured standard output.
    /// </summary>
    [JsonPropertyName("stdout")]
    public string? Stdout { get; set; }

    /// <summary>
    /// Gets or sets the captured standard error.
    /// </summary>
    [JsonPropertyName("stderr")]
    public string? Stderr { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether stdout was truncated.
    /// </summary>
    [JsonPropertyName("stdout_truncated")]
    public bool StdoutTruncated { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether stderr was truncated.
    /// </summary>
    [JsonPropertyName("stderr_truncated")]
    public bool StderrTruncated { get; set; }

    /// <summary>
    /// Gets or sets a short execution summary.
    /// </summary>
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the elapsed execution time in milliseconds.
    /// </summary>
    [JsonPropertyName("elapsed_milliseconds")]
    public long ElapsedMilliseconds { get; set; }
}
