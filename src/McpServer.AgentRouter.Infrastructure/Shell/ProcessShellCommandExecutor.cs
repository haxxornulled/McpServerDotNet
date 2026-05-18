using System.Diagnostics;
using System.Text;
using LanguageExt;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Domain.Shell;
using Microsoft.Extensions.Logging;

namespace McpServer.AgentRouter.Infrastructure.Shell;

public sealed class ProcessShellCommandExecutor : IShellCommandExecutor
{
    private readonly ILogger<ProcessShellCommandExecutor> _logger;

    public ProcessShellCommandExecutor(ILogger<ProcessShellCommandExecutor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<Fin<ShellCommandExecutionResult>> ExecuteAsync(
        ShellExecutionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(command.Command))
        {
            return LanguageExt.Common.Error.New("Shell command is required.");
        }

        if (string.IsNullOrWhiteSpace(command.WorkingDirectory) || !Directory.Exists(command.WorkingDirectory))
        {
            return LanguageExt.Common.Error.New($"Shell command working directory '{command.WorkingDirectory}' does not exist.");
        }

        try
        {
            return await ExecuteProcessAsync(command, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return LanguageExt.Common.Error.New($"Failed to execute approved shell command '{command.Command}': {ex.Message}");
        }
    }

    private async ValueTask<ShellCommandExecutionResult> ExecuteProcessAsync(
        ShellExecutionCommand command,
        CancellationToken cancellationToken)
    {
        using var timeoutCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, command.TimeoutSeconds)));
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);

        var startInfo = new ProcessStartInfo
        {
            FileName = command.Command,
            WorkingDirectory = command.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = false
        };

        var stopwatch = Stopwatch.StartNew();

        if (!process.Start())
        {
            return new ShellCommandExecutionResult
            {
                ExitCode = -1,
                Stderr = $"Process '{command.Command}' did not start.",
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
            };
        }

        var stdoutTask = ReadBoundedAsync(process.StandardOutput, command.MaxOutputChars, cancellationToken);
        var stderrTask = ReadBoundedAsync(process.StandardError, command.MaxOutputChars, cancellationToken);
        var timedOut = false;

        try
        {
            await process.WaitForExitAsync(linkedCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            timedOut = true;
            TryKill(process);
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        stopwatch.Stop();

        var exitCode = process.HasExited
            ? process.ExitCode
            : -1;

        if (timedOut)
        {
            _logger.LogWarning(
                "Approved shell command {Command} timed out after {TimeoutSeconds}s.",
                command.Command,
                command.TimeoutSeconds);
        }

        return new ShellCommandExecutionResult
        {
            ExitCode = exitCode,
            TimedOut = timedOut,
            Stdout = stdout.Text,
            Stderr = stderr.Text,
            StdoutTruncated = stdout.Truncated,
            StderrTruncated = stderr.Truncated,
            ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
        };
    }

    private static async Task<BoundedTextResult> ReadBoundedAsync(
        StreamReader reader,
        int maxChars,
        CancellationToken cancellationToken)
    {
        var limit = Math.Max(1, maxChars);
        var buffer = new char[4096];
        var builder = new StringBuilder(Math.Min(limit, 4096));
        var truncated = false;

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                .ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            var remaining = limit - builder.Length;
            if (remaining > 0)
            {
                builder.Append(buffer, 0, Math.Min(read, remaining));
            }

            if (read > remaining)
            {
                truncated = true;
            }
        }

        return new BoundedTextResult(builder.ToString(), truncated);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private sealed record BoundedTextResult(
        string Text,
        bool Truncated);
}
