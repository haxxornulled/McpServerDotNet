using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using McpServer.AgentRouter.Tools;
using Xunit;

namespace McpServer.AgentRouter.UnitTests;

public sealed class AgentRouterToolsJsonOutputTests
{
    [Fact]
    public async Task InstallLocalClients_Should_Emit_Json_When_Requested()
    {
        using var tempRoot = new TempDirectory();
        var repoRoot = FindRepoRoot();
        var assemblyPath = System.IO.Path.Combine(repoRoot, "tools", "McpServer.AgentRouter.Tools", "bin", "Release", "net10.0", "McpServer.AgentRouter.Tools.dll");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add(assemblyPath);
        startInfo.ArgumentList.Add("install-local-clients");
        startInfo.ArgumentList.Add("--repo-root");
        startInfo.ArgumentList.Add(tempRoot.DirectoryPath);
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add("json");

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);

        _ = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.Equal(0, process.ExitCode);
        Assert.True(File.Exists(System.IO.Path.Combine(tempRoot.DirectoryPath, ".codex", "config.toml")));
        Assert.True(File.Exists(System.IO.Path.Combine(tempRoot.DirectoryPath, ".vscode", "mcp.json")));
        Assert.True(File.Exists(System.IO.Path.Combine(tempRoot.DirectoryPath, ".mcp.json")));
        Assert.True(string.IsNullOrWhiteSpace(stderr), stderr);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            DirectoryPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mcpserver-tools-json-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private static string FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(System.IO.Path.Combine(current, "McpServer.slnx")))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName;
        }

        throw new InvalidOperationException("Unable to locate repository root.");
    }
}
