using System.Text.Json.Serialization;

namespace McpServer.AgentRouter.Host.Protocol.Ssh;

public sealed class SshExecutionRequest
{
    [JsonPropertyName("profile")]
    public string? Profile { get; set; }

    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("arguments")]
    public IList<string> Arguments { get; set; } = new List<string>();

    [JsonPropertyName("working_directory")]
    public string? WorkingDirectory { get; set; }

    [JsonPropertyName("timeout_seconds")]
    public int? TimeoutSeconds { get; set; }
}

public sealed class SshExecutionResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("object")]
    public string Object { get; set; } = "ssh.execution";

    [JsonPropertyName("status")]
    public string Status { get; set; } = SshExecutionStatusNames.Pending;

    [JsonPropertyName("allowed")]
    public bool Allowed { get; set; }

    [JsonPropertyName("policy_decision")]
    public string PolicyDecision { get; set; } = string.Empty;

    [JsonPropertyName("policy_reason")]
    public string? PolicyReason { get; set; }

    [JsonPropertyName("profile")]
    public string Profile { get; set; } = string.Empty;

    [JsonPropertyName("host")]
    public string Host { get; set; } = string.Empty;

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public IList<string> Arguments { get; set; } = new List<string>();

    [JsonPropertyName("working_directory")]
    public string WorkingDirectory { get; set; } = string.Empty;

    [JsonPropertyName("exit_code")]
    public int? ExitCode { get; set; }

    [JsonPropertyName("timed_out")]
    public bool TimedOut { get; set; }

    [JsonPropertyName("stdout")]
    public string? Stdout { get; set; }

    [JsonPropertyName("stderr")]
    public string? Stderr { get; set; }

    [JsonPropertyName("stdout_truncated")]
    public bool StdoutTruncated { get; set; }

    [JsonPropertyName("stderr_truncated")]
    public bool StderrTruncated { get; set; }

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("trace_id")]
    public string TraceId { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("completed_at")]
    public DateTimeOffset? CompletedAt { get; set; }

    [JsonPropertyName("elapsed_milliseconds")]
    public long ElapsedMilliseconds { get; set; }
}

public static class SshExecutionStatusNames
{
    public const string Pending = "pending";
    public const string Denied = "denied";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string TimedOut = "timed_out";
}
