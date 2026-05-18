using LanguageExt;
using McpServer.Application.Abstractions.Execution;
using McpServer.Application.Execution;
using McpServer.Application.Execution.Commands;
using McpServer.Application.Execution.Results;
using McpServer.Application.Mcp.Tools;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace McpServer.UnitTests.Application;

public sealed class ShellExecToolHandlerTests
{
    private static readonly IShellExecutionPolicy TestPolicy =
        new ShellExecutionPolicy(new ShellExecutionPolicyOptions(true, true, ["dotnet", "git", "dir", "ls"], [], 300, 200000));

    [Fact]
    public async Task Handle_Should_Return_Structured_Result_With_Command_Output()
    {
        var processExecution = Substitute.For<IProcessExecutionService>();
        var logger = Substitute.For<ILogger<ShellExecToolHandler>>();

        processExecution.RunAsync(Arg.Any<RunProcessCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<ProcessExecutionResult>>(new ProcessExecutionResult(
                Command: "dotnet",
                Arguments: ["--version"],
                WorkingDirectory: "D:/workspace",
                ExitCode: 0,
                StandardOutput: "10.0.201",
                StandardError: string.Empty,
                TimedOut: false,
                OutputTruncated: false)));

        var handler = new ShellExecToolHandler(processExecution, logger, TestPolicy);
        var result = await handler.Handle(new ShellExecRequest("dotnet", ["--version"]), CancellationToken.None);

        Assert.True(result.IsSucc);

        var dto = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        Assert.Contains(dto.Content, item => item.Text.Contains("Exit Code: 0", StringComparison.Ordinal));
        Assert.False(dto.IsError);
        Assert.NotNull(dto.StructuredContent);
    }

    [Fact]
    public async Task Handle_Should_Run_Bare_Shell_Command_Line_Via_System_Shell_When_Arguments_Are_Omitted()
    {
        var processExecution = Substitute.For<IProcessExecutionService>();
        var logger = Substitute.For<ILogger<ShellExecToolHandler>>();

        var expectedCommand = OperatingSystem.IsWindows() ? "pwsh" : "/bin/sh";
        string[] expectedArguments = OperatingSystem.IsWindows()
            ? [
                "-NoLogo",
                "-NoProfile",
                "-Command",
                "git clone https://github.com/haxxornulled/PF-World-Of-Warcraft-Framework.git"
            ]
            : [
                "-lc",
                "git clone https://github.com/haxxornulled/PF-World-Of-Warcraft-Framework.git"
            ];

        processExecution.RunAsync(Arg.Any<RunProcessCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<ProcessExecutionResult>>(new ProcessExecutionResult(
                Command: expectedCommand,
                Arguments: expectedArguments,
                WorkingDirectory: "D:/workspace",
                ExitCode: 0,
                StandardOutput: string.Empty,
                StandardError: string.Empty,
                TimedOut: false,
                OutputTruncated: false)));

        var handler = new ShellExecToolHandler(processExecution, logger, TestPolicy);
        var result = await handler.Handle(
            new ShellExecRequest("git clone https://github.com/haxxornulled/PF-World-Of-Warcraft-Framework.git"),
            CancellationToken.None);

        Assert.True(result.IsSucc);

        await processExecution.Received(1).RunAsync(
            Arg.Is<RunProcessCommand>(command =>
                command.Command == expectedCommand
                    && command.Arguments != null
                    && command.Arguments.SequenceEqual(expectedArguments)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_Run_Windows_Shell_Builtins_Via_System_Shell_When_Arguments_Are_Omitted()
    {
        var processExecution = Substitute.For<IProcessExecutionService>();
        var logger = Substitute.For<ILogger<ShellExecToolHandler>>();

        var builtInCommand = OperatingSystem.IsWindows() ? "pwsh" : "dir";
        string[] expectedArguments = OperatingSystem.IsWindows()
            ? [
                "-NoLogo",
                "-NoProfile",
                "-Command",
                "dir"
            ]
            : Array.Empty<string>();

        processExecution.RunAsync(Arg.Any<RunProcessCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<ProcessExecutionResult>>(new ProcessExecutionResult(
                Command: builtInCommand,
                Arguments: expectedArguments,
                WorkingDirectory: "D:/workspace",
                ExitCode: 0,
                StandardOutput: string.Empty,
                StandardError: string.Empty,
                TimedOut: false,
                OutputTruncated: false)));

        var handler = new ShellExecToolHandler(processExecution, logger, TestPolicy);
        var result = await handler.Handle(new ShellExecRequest("dir"), CancellationToken.None);

        Assert.True(result.IsSucc);

        await processExecution.Received(1).RunAsync(
            Arg.Is<RunProcessCommand>(command =>
                OperatingSystem.IsWindows()
                    ? command.Command == "pwsh"
                        && command.Arguments != null
                        && command.Arguments.SequenceEqual(expectedArguments)
                    : command.Command == "dir"
                        && command.Arguments == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_Run_Bare_Ls_Via_PowerShell_Compatibility_On_Windows()
    {
        var processExecution = Substitute.For<IProcessExecutionService>();
        var logger = Substitute.For<ILogger<ShellExecToolHandler>>();

        var expectedCommand = OperatingSystem.IsWindows() ? "pwsh" : "ls";

        processExecution.RunAsync(Arg.Any<RunProcessCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var command = call.Arg<RunProcessCommand>();
                return new ValueTask<Fin<ProcessExecutionResult>>(new ProcessExecutionResult(
                    Command: command.Command,
                    Arguments: command.Arguments,
                    WorkingDirectory: "D:/workspace",
                    ExitCode: 0,
                    StandardOutput: string.Empty,
                    StandardError: string.Empty,
                    TimedOut: false,
                    OutputTruncated: false));
            });

        var handler = new ShellExecToolHandler(processExecution, logger, TestPolicy);
        var result = await handler.Handle(new ShellExecRequest("ls"), CancellationToken.None);

        Assert.True(result.IsSucc);

        var expectedArguments = new[]
        {
            "-NoLogo",
            "-NoProfile",
            "-Command",
            "Get-ChildItem"
        };

        await processExecution.Received(1).RunAsync(
            Arg.Is<RunProcessCommand>(command =>
                command.Command == expectedCommand
                    && (!OperatingSystem.IsWindows()
                        || command.Arguments != null
                        && command.Arguments.SequenceEqual(expectedArguments))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_Run_Ls_With_Args_Via_PowerShell_Compatibility_On_Windows()
    {
        var processExecution = Substitute.For<IProcessExecutionService>();
        var logger = Substitute.For<ILogger<ShellExecToolHandler>>();

        processExecution.RunAsync(Arg.Any<RunProcessCommand>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var command = call.Arg<RunProcessCommand>();
                return new ValueTask<Fin<ProcessExecutionResult>>(new ProcessExecutionResult(
                    Command: command.Command,
                    Arguments: command.Arguments,
                    WorkingDirectory: "D:/workspace",
                    ExitCode: 0,
                    StandardOutput: string.Empty,
                    StandardError: string.Empty,
                    TimedOut: false,
                    OutputTruncated: false));
            });

        var handler = new ShellExecToolHandler(processExecution, logger, TestPolicy);
        var result = await handler.Handle(new ShellExecRequest("ls", ["-la", "."]), CancellationToken.None);

        Assert.True(result.IsSucc);

        var expectedWindowsArguments = new[]
        {
            "-NoLogo",
            "-NoProfile",
            "-Command",
            "Get-ChildItem -Force '.'"
        };
        var expectedNonWindowsArguments = new[] { "-la", "." };

        await processExecution.Received(1).RunAsync(
            Arg.Is<RunProcessCommand>(command =>
                OperatingSystem.IsWindows()
                    ? command.Command == "pwsh"
                        && command.Arguments != null
                        && command.Arguments.SequenceEqual(expectedWindowsArguments)
                    : command.Command == "ls"
                        && command.Arguments != null
                        && command.Arguments.SequenceEqual(expectedNonWindowsArguments)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_Reject_Shell_Control_Operators_In_Bare_Command_Lines()
    {
        var processExecution = Substitute.For<IProcessExecutionService>();
        var logger = Substitute.For<ILogger<ShellExecToolHandler>>();
        var handler = new ShellExecToolHandler(processExecution, logger, TestPolicy);

        var result = await handler.Handle(new ShellExecRequest("git status; whoami"), CancellationToken.None);

        Assert.True(result.IsFail);
        var error = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected shell command validation to fail."),
            Fail: failure => failure.Message);
        Assert.Contains("shell control characters", error, StringComparison.Ordinal);
        await processExecution.DidNotReceive().RunAsync(Arg.Any<RunProcessCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_Reject_Embedded_Arguments_In_Executable_Mode()
    {
        var processExecution = Substitute.For<IProcessExecutionService>();
        var logger = Substitute.For<ILogger<ShellExecToolHandler>>();
        var handler = new ShellExecToolHandler(processExecution, logger, TestPolicy);

        var result = await handler.Handle(
            new ShellExecRequest("git clone", ["https://example.com/repo.git"]),
            CancellationToken.None);

        Assert.True(result.IsFail);
        var error = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected executable validation to fail."),
            Fail: failure => failure.Message);
        Assert.Contains("Pass command arguments via 'args'", error, StringComparison.Ordinal);
        await processExecution.DidNotReceive().RunAsync(Arg.Any<RunProcessCommand>(), Arg.Any<CancellationToken>());
    }
}
