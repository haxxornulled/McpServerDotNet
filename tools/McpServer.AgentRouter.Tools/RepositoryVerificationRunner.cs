using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace McpServer.AgentRouter.Tools;

internal sealed class RepositoryVerificationSettings
{
    public string RepositoryRootPath { get; init; } = Directory.GetCurrentDirectory();

    public string SolutionPath { get; init; } = "McpServer.slnx";

    public string Configuration { get; init; } = "Release";

    public string DotNetExecutablePath { get; init; } = "dotnet";

    public string UnitTestsProjectPath { get; init; } = Path.Combine("tests", "McpServer.UnitTests", "McpServer.UnitTests.csproj");

    public string IntegrationTestsProjectPath { get; init; } = Path.Combine("tests", "McpServer.IntegrationTests", "McpServer.IntegrationTests.csproj");

    public string AgentRouterUnitTestsProjectPath { get; init; } = Path.Combine("tests", "McpServer.AgentRouter.UnitTests", "McpServer.AgentRouter.UnitTests.csproj");

    public static RepositoryVerificationSettings FromOptions(CommandLineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new RepositoryVerificationSettings
        {
            RepositoryRootPath = options.GetString("repo-root", Directory.GetCurrentDirectory()),
            SolutionPath = options.GetString("solution", "McpServer.slnx"),
            Configuration = options.GetString("configuration", "Release"),
            DotNetExecutablePath = options.GetString("dotnet", "dotnet")
        };
    }
}

internal sealed class RepositoryVerificationRunner
{
    private readonly RepositoryVerificationSettings _settings;

    public RepositoryVerificationRunner(RepositoryVerificationSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var repoRoot = Path.GetFullPath(_settings.RepositoryRootPath);
        var solutionPath = Path.GetFullPath(Path.Combine(repoRoot, _settings.SolutionPath));

        ConsoleWriter.WriteSection("Repository verification");
        ConsoleWriter.WriteInfo($"Repo root: {repoRoot}");
        ConsoleWriter.WriteInfo($"Solution: {solutionPath}");
        ConsoleWriter.WriteInfo($"Configuration: {_settings.Configuration}");

        if (await RunDotNetAsync(
                title: "Restore solution",
                workingDirectory: repoRoot,
                cancellationToken: cancellationToken,
                "restore",
                _settings.SolutionPath).ConfigureAwait(false) != 0)
        {
            return 1;
        }

        if (await RunDotNetAsync(
                title: "Build solution",
                workingDirectory: repoRoot,
                cancellationToken: cancellationToken,
                "build",
                _settings.SolutionPath,
                "-c",
                _settings.Configuration,
                "--no-restore",
                "-v",
                "minimal").ConfigureAwait(false) != 0)
        {
            return 1;
        }

        var testProjects = new[]
        {
            _settings.UnitTestsProjectPath,
            _settings.IntegrationTestsProjectPath,
            _settings.AgentRouterUnitTestsProjectPath
        };

        foreach (var projectPath in testProjects)
        {
            if (await RunDotNetAsync(
                    title: $"Test {projectPath}",
                    workingDirectory: repoRoot,
                    cancellationToken: cancellationToken,
                    "test",
                    projectPath,
                    "-c",
                    _settings.Configuration,
                    "--no-build",
                    "--no-restore",
                    "-v",
                    "minimal").ConfigureAwait(false) != 0)
            {
                return 1;
            }
        }

        ConsoleWriter.WritePass("Repository verification completed successfully.");
        return 0;
    }

    private async Task<int> RunDotNetAsync(
        string title,
        string workingDirectory,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        ConsoleWriter.WriteSection(title);
        ConsoleWriter.WriteInfo($"dotnet {string.Join(" ", arguments)}");

        var exitCode = await ProcessRunner.RunAsync(
                _settings.DotNetExecutablePath,
                arguments,
                workingDirectory,
                cancellationToken)
            .ConfigureAwait(false);

        if (exitCode == 0)
        {
            ConsoleWriter.WritePass($"{title} succeeded.");
        }
        else
        {
            ConsoleWriter.WriteError($"{title} failed with exit code {exitCode}.");
        }

        return exitCode;
    }
}

internal static class ProcessRunner
{
    public static async Task<int> RunAsync(
        string fileName,
        IReadOnlyCollection<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start process '{fileName}'.");

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }
}
