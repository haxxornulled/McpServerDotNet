using McpServer.Application.Execution;
using McpServer.Application.Mcp.Tools;
using Xunit;

namespace McpServer.UnitTests.Application;

public sealed class ShellExecutionPolicyTests
{
    [Fact]
    public void Validate_Should_Deny_When_Allowlist_Is_Empty()
    {
        var sut = new ShellExecutionPolicy(new ShellExecutionPolicyOptions(
            AllowShellFallback: false,
            AllowWindowsCompatibilityShell: false,
            AllowedCommands: [],
            DeniedCommands: [],
            MaxTimeoutSeconds: 120,
            MaxOutputChars: 12000));

        var result = sut.Validate(new ShellExecRequest("dotnet", ["--version"]), false, false);

        Assert.True(result.IsFail);
        var error = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected validation to fail."),
            Fail: failure => failure.Message);
        Assert.Contains("explicit command allowlist", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Should_Deny_Denied_Command_Even_When_Allowlisted()
    {
        var sut = new ShellExecutionPolicy(new ShellExecutionPolicyOptions(
            AllowShellFallback: false,
            AllowWindowsCompatibilityShell: false,
            AllowedCommands: ["pwsh"],
            DeniedCommands: ["pwsh"],
            MaxTimeoutSeconds: 120,
            MaxOutputChars: 12000));

        var result = sut.Validate(new ShellExecRequest("pwsh", ["-NoProfile"]), false, false);

        Assert.True(result.IsFail);
        var error = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected validation to fail."),
            Fail: failure => failure.Message);
        Assert.Contains("denied", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_Should_Deny_Bare_Shell_Fallback_When_Not_Enabled()
    {
        var sut = new ShellExecutionPolicy(new ShellExecutionPolicyOptions(
            AllowShellFallback: false,
            AllowWindowsCompatibilityShell: false,
            AllowedCommands: ["git"],
            DeniedCommands: [],
            MaxTimeoutSeconds: 120,
            MaxOutputChars: 12000));

        var result = sut.Validate(new ShellExecRequest("git status"), true, false);

        Assert.True(result.IsFail);
        var error = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected validation to fail."),
            Fail: failure => failure.Message);
        Assert.Contains("Bare shell command lines are disabled", error, StringComparison.Ordinal);
    }
}
