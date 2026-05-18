using System.Diagnostics;
using LanguageExt;
using LanguageExt.Common;
using McpServer.Application.Abstractions.Execution;
using McpServer.Application.Abstractions.Files;
using McpServer.Application.Execution.Commands;
using McpServer.Application.Execution.Results;
using Microsoft.Extensions.Logging;

namespace McpServer.Infrastructure.Execution;

public sealed class ProcessExecutionService(
    IPathPolicy pathPolicy,
    ILogger<ProcessExecutionService> logger) : IProcessExecutionService
{
    private static readonly char[] InvalidProcessTextCharacters = ['\0', '\r', '\n'];
    private const int MinimumTimeoutSeconds = 1;
    private const int MaximumTimeoutSeconds = 600;
    private const int MinimumOutputChars = 256;
    private const int MaximumOutputChars = 200000;

    public async ValueTask<Fin<ProcessExecutionResult>> RunAsync(RunProcessCommand command, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(command.Command))
            {
                return Error.New("Command is required");
            }

            if (ContainsInvalidProcessText(command.Command))
            {
                return Error.New("Command cannot contain embedded nulls or line breaks.");
            }

            if ((command.Arguments ?? Array.Empty<string>()).Any(ContainsInvalidProcessText))
            {
                return Error.New("Command arguments cannot contain embedded nulls or line breaks.");
            }

            var timeoutSeconds = Math.Clamp(command.TimeoutSeconds, MinimumTimeoutSeconds, MaximumTimeoutSeconds);
            var maxOutputChars = Math.Clamp(command.MaxOutputChars, MinimumOutputChars, MaximumOutputChars);

            var workingDirectoryFin = pathPolicy.NormalizeAndValidateWritePath(command.WorkingDirectory ?? "project");
            if (workingDirectoryFin.IsFail)
            {
                return PropagateFailure<ProcessExecutionResult>(workingDirectoryFin);
            }

            var workingDirectory = workingDirectoryFin.Match(
                Succ: value => value,
                Fail: _ => throw new InvalidOperationException("Working directory validation unexpectedly failed."));

            if (!Directory.Exists(workingDirectory))
            {
                return Error.New($"Working directory not found: {workingDirectory}");
            }

            using var process = new Process
            {
                StartInfo = CreateStartInfo(command, workingDirectory),
                EnableRaisingEvents = true
            };

            if (!process.Start())
            {
                return Error.New("Failed to start process");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            var timedOut = false;

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                timedOut = true;

                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                }

                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            var truncated = false;

            stdout = Truncate(stdout, maxOutputChars, ref truncated);
            stderr = Truncate(stderr, maxOutputChars, ref truncated);

            var exitCode = timedOut ? -1 : process.ExitCode;

            logger.LogInformation(
                "Executed command {Command} in {WorkingDirectory} with exit code {ExitCode} timed out {TimedOut}",
                command.Command,
                workingDirectory,
                exitCode,
                timedOut);

            return new ProcessExecutionResult(
                command.Command,
                command.Arguments,
                workingDirectory,
                exitCode,
                stdout,
                stderr,
                timedOut,
                truncated);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed executing command {Command}", command.Command);
            return Error.New(ex.Message);
        }
    }

    private static ProcessStartInfo CreateStartInfo(RunProcessCommand command, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.Command,
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in command.Arguments ?? Array.Empty<string>())
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

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

    private static bool ContainsInvalidProcessText(string value) =>
        value.IndexOfAny(InvalidProcessTextCharacters) >= 0;

    private static Fin<T> PropagateFailure<T>(Fin<string> failure) =>
        failure.Match<Fin<T>>(
            Succ: _ => throw new InvalidOperationException("Expected failure while propagating result."),
            Fail: error => error);
}
