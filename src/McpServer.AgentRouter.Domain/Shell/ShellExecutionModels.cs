using System.Text.Json.Serialization;

namespace McpServer.AgentRouter.Domain.Shell;

public sealed class ShellExecutionRequest
{
    public string? Command { get; set; }

    public IList<string> Arguments { get; set; } = new List<string>();

    public string? WorkingDirectory { get; set; }

    public int? TimeoutSeconds { get; set; }
}

public sealed class ShellExecutionResponse
{
    public string Id { get; set; } = string.Empty;

    public string Object { get; set; } = "shell.execution";

    public string Status { get; set; } = ShellExecutionStatusNames.Pending;

    public bool Allowed { get; set; }

    public string PolicyDecision { get; set; } = string.Empty;

    public string? PolicyReason { get; set; }

    public string Command { get; set; } = string.Empty;

    public IList<string> Arguments { get; set; } = new List<string>();

    public string WorkingDirectory { get; set; } = string.Empty;

    public int? ExitCode { get; set; }

    public bool TimedOut { get; set; }

    public string? Stdout { get; set; }

    public string? Stderr { get; set; }

    public bool StdoutTruncated { get; set; }

    public bool StderrTruncated { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string TraceId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public long ElapsedMilliseconds { get; set; }
}

public static class ShellExecutionStatusNames
{
    public const string Pending = "pending";
    public const string Denied = "denied";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string TimedOut = "timed_out";
}

public sealed class ShellExecutionPolicyDecision
{
    public bool Allowed { get; set; }

    public string Decision { get; set; } = "denied";

    public string? Reason { get; set; }

    public string ResolvedCommand { get; set; } = string.Empty;

    public IList<string> ResolvedArguments { get; set; } = new List<string>();

    public string WorkingDirectory { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; }

    public int MaxOutputChars { get; set; }
}

public sealed class ShellExecutionCommand
{
    public string Command { get; set; } = string.Empty;

    public IList<string> Arguments { get; set; } = new List<string>();

    public string WorkingDirectory { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; }

    public int MaxOutputChars { get; set; }
}

public sealed class ShellCommandExecutionResult
{
    public int ExitCode { get; set; }

    public bool TimedOut { get; set; }

    public string Stdout { get; set; } = string.Empty;

    public string Stderr { get; set; } = string.Empty;

    public bool StdoutTruncated { get; set; }

    public bool StderrTruncated { get; set; }

    public long ElapsedMilliseconds { get; set; }
}

public sealed class ShellExecutionTraceRecord
{
    [JsonPropertyName("trace_id")]
    public string TraceId { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("completed_at")]
    public DateTimeOffset? CompletedAt { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = ShellExecutionStatusNames.Pending;

    [JsonPropertyName("allowed")]
    public bool Allowed { get; set; }

    [JsonPropertyName("policy_decision")]
    public string PolicyDecision { get; set; } = string.Empty;

    [JsonPropertyName("policy_reason")]
    public string? PolicyReason { get; set; }

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

    [JsonPropertyName("elapsed_milliseconds")]
    public long ElapsedMilliseconds { get; set; }
}
