using McpServer.Application.Execution;
using McpServer.Application.Mcp.Tools;
using Xunit;

namespace McpServer.UnitTests.Application;

public sealed class ShellExecutionPolicyHardeningTests
{
    [Fact]
    public void BuildAndTestOnly_Should_Allow_Dotnet()
    {
        var sut = new ShellExecutionPolicy(ShellExecutionPolicyOptions.BuildAndTestOnly);

        var result = sut.Validate(new ShellExecRequest("dotnet", ["--info"]), false, false);

        Assert.True(result.IsSucc, GetError(result));
    }

    [Fact]
    public void BuildAndTestOnly_Should_Allow_Git()
    {
        var sut = new ShellExecutionPolicy(ShellExecutionPolicyOptions.BuildAndTestOnly);

        var result = sut.Validate(new ShellExecRequest("git", ["status", "--short"]), false, false);

        Assert.True(result.IsSucc, GetError(result));
    }

    [Fact]
    public void BuildAndTestOnly_Should_Deny_Pwsh_Even_With_Normal_Timeouts()
    {
        var sut = new ShellExecutionPolicy(ShellExecutionPolicyOptions.BuildAndTestOnly);

        var result = sut.Validate(new ShellExecRequest("pwsh", ["-NoProfile"]), false, false);

        Assert.True(result.IsFail);
        Assert.Contains("denied", GetError(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_Should_Deny_Cmd_With_Extension_When_Denied_Command_Has_No_Extension()
    {
        var sut = new ShellExecutionPolicy(new ShellExecutionPolicyOptions(
            AllowShellFallback: false,
            AllowWindowsCompatibilityShell: false,
            AllowedCommands: ["cmd"],
            DeniedCommands: ["cmd"],
            MaxTimeoutSeconds: 120,
            MaxOutputChars: 12000));

        var result = sut.Validate(new ShellExecRequest("cmd.exe", ["/c", "dir"]), false, false);

        Assert.True(result.IsFail);
        Assert.Contains("denied", GetError(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_Should_Extract_Executable_From_Quoted_Path()
    {
        var sut = new ShellExecutionPolicy(new ShellExecutionPolicyOptions(
            AllowShellFallback: false,
            AllowWindowsCompatibilityShell: false,
            AllowedCommands: ["dotnet"],
            DeniedCommands: [],
            MaxTimeoutSeconds: 120,
            MaxOutputChars: 12000));

        var result = sut.Validate(new ShellExecRequest("\"C:\\Program Files\\dotnet\\dotnet.exe\"", ["--version"]), false, false);

        Assert.True(result.IsSucc, GetError(result));
    }

    [Fact]
    public void Validate_Should_Deny_Command_Outside_Allowlist()
    {
        var sut = new ShellExecutionPolicy(new ShellExecutionPolicyOptions(
            AllowShellFallback: false,
            AllowWindowsCompatibilityShell: false,
            AllowedCommands: ["dotnet"],
            DeniedCommands: [],
            MaxTimeoutSeconds: 120,
            MaxOutputChars: 12000));

        var result = sut.Validate(new ShellExecRequest("git", ["status"]), false, false);

        Assert.True(result.IsFail);
        Assert.Contains("allowlist", GetError(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_Should_Deny_Bare_Shell_Fallback_When_Disabled()
    {
        var sut = new ShellExecutionPolicy(new ShellExecutionPolicyOptions(
            AllowShellFallback: false,
            AllowWindowsCompatibilityShell: false,
            AllowedCommands: ["dotnet"],
            DeniedCommands: [],
            MaxTimeoutSeconds: 120,
            MaxOutputChars: 12000));

        var result = sut.Validate(new ShellExecRequest("dotnet", ["--version"]), requiresShellFallback: true, requiresWindowsCompatibilityShell: false);

        Assert.True(result.IsFail);
        Assert.Contains("shell command lines are disabled", GetError(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_Should_Deny_Windows_Compatibility_Shell_When_Disabled()
    {
        var sut = new ShellExecutionPolicy(new ShellExecutionPolicyOptions(
            AllowShellFallback: false,
            AllowWindowsCompatibilityShell: false,
            AllowedCommands: ["dir"],
            DeniedCommands: [],
            MaxTimeoutSeconds: 120,
            MaxOutputChars: 12000));

        var result = sut.Validate(new ShellExecRequest("dir", []), requiresShellFallback: false, requiresWindowsCompatibilityShell: true);

        Assert.True(result.IsFail);
        Assert.Contains("compatibility shell", GetError(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_Should_Allow_Windows_Compatibility_Shell_When_Explicitly_Enabled()
    {
        var sut = new ShellExecutionPolicy(new ShellExecutionPolicyOptions(
            AllowShellFallback: false,
            AllowWindowsCompatibilityShell: true,
            AllowedCommands: ["dir"],
            DeniedCommands: [],
            MaxTimeoutSeconds: 120,
            MaxOutputChars: 12000));

        var result = sut.Validate(new ShellExecRequest("dir", []), requiresShellFallback: false, requiresWindowsCompatibilityShell: true);

        Assert.True(result.IsSucc, GetError(result));
    }

    [Fact]
    public void Validate_Should_Deny_Timeout_Above_Maximum()
    {
        var sut = new ShellExecutionPolicy(new ShellExecutionPolicyOptions(
            AllowShellFallback: false,
            AllowWindowsCompatibilityShell: false,
            AllowedCommands: ["dotnet"],
            DeniedCommands: [],
            MaxTimeoutSeconds: 10,
            MaxOutputChars: 12000));

        var result = sut.Validate(new ShellExecRequest("dotnet", ["test"], TimeoutSeconds: 11), false, false);

        Assert.True(result.IsFail);
        Assert.Contains("timeout", GetError(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_Should_Deny_Output_Above_Maximum()
    {
        var sut = new ShellExecutionPolicy(new ShellExecutionPolicyOptions(
            AllowShellFallback: false,
            AllowWindowsCompatibilityShell: false,
            AllowedCommands: ["dotnet"],
            DeniedCommands: [],
            MaxTimeoutSeconds: 120,
            MaxOutputChars: 1000));

        var result = sut.Validate(new ShellExecRequest("dotnet", ["test"], MaxOutputChars: 1001), false, false);

        Assert.True(result.IsFail);
        Assert.Contains("Max output chars", GetError(result), StringComparison.Ordinal);
    }

    [Fact]
    public void Options_Should_Normalize_Command_Collections()
    {
        var options = new ShellExecutionPolicyOptions(
            AllowShellFallback: false,
            AllowWindowsCompatibilityShell: false,
            AllowedCommands: [" dotnet ", "DOTNET", "git", ""],
            DeniedCommands: [" pwsh ", "PWSH", ""],
            MaxTimeoutSeconds: 120,
            MaxOutputChars: 12000);

        Assert.Equal(2, options.AllowedCommands.Count);
        Assert.Contains("dotnet", options.AllowedCommands);
        Assert.Contains("git", options.AllowedCommands);
        Assert.Single(options.DeniedCommands);
        Assert.Contains("pwsh", options.DeniedCommands);
    }

    private static string GetError(LanguageExt.Fin<LanguageExt.Unit> result)
    {
        return result.Match(
            Succ: _ => string.Empty,
            Fail: error => error.Message);
    }
}
