using McpServer.AgentRouter.Application.Shell;
using McpServer.AgentRouter.Application.Runtime;
using McpServer.AgentRouter.Domain.Shell;
using Xunit;

namespace McpServer.AgentRouter.UnitTests;

public sealed class ShellExecutionPolicyTests
{
    [Fact]
    public async Task EvaluateAsync_AllowsCommandInAllowlist_InsideWorkspaceRoot()
    {
        using var workspace = new TemporaryWorkspace();
        var policy = CreatePolicy(workspace.RootPath);

        var result = await policy.EvaluateAsync(new ShellExecutionRequest
        {
            Command = "dotnet",
            Arguments = ["--info"],
            WorkingDirectory = "."
        }, CancellationToken.None);

        Assert.True(result.IsSucc);
        result.IfSucc(decision =>
        {
            Assert.True(decision.Allowed);
            Assert.Equal("allowed", decision.Decision);
            Assert.Equal(workspace.RootPath, decision.WorkingDirectory);
            Assert.Equal("dotnet", decision.ResolvedCommand);
            Assert.Equal(new[] { "--info" }, decision.ResolvedArguments);
        });
    }

    [Fact]
    public async Task EvaluateAsync_DeniesCommandOutsideAllowlist()
    {
        using var workspace = new TemporaryWorkspace();
        var policy = CreatePolicy(workspace.RootPath);

        var result = await policy.EvaluateAsync(new ShellExecutionRequest
        {
            Command = "cmd",
            Arguments = ["/c", "echo no"],
            WorkingDirectory = "."
        }, CancellationToken.None);

        Assert.True(result.IsSucc);
        result.IfSucc(decision =>
        {
            Assert.False(decision.Allowed);
            Assert.Contains("AllowedCommands", decision.Reason, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task EvaluateAsync_DeniesWorkingDirectoryEscape()
    {
        using var workspace = new TemporaryWorkspace();
        var policy = CreatePolicy(workspace.RootPath);

        var result = await policy.EvaluateAsync(new ShellExecutionRequest
        {
            Command = "dotnet",
            Arguments = ["--info"],
            WorkingDirectory = ".."
        }, CancellationToken.None);

        Assert.True(result.IsSucc);
        result.IfSucc(decision =>
        {
            Assert.False(decision.Allowed);
            Assert.Contains("escapes", decision.Reason, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task EvaluateAsync_DeniesInlineShellCommands_ByDefault()
    {
        using var workspace = new TemporaryWorkspace();
        var policy = CreatePolicy(workspace.RootPath);

        var result = await policy.EvaluateAsync(new ShellExecutionRequest
        {
            Command = "bash",
            Arguments = ["-c", "echo unsafe"],
            WorkingDirectory = "."
        }, CancellationToken.None);

        Assert.True(result.IsSucc);
        result.IfSucc(decision =>
        {
            Assert.False(decision.Allowed);
            Assert.Contains("inline command", decision.Reason, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static ShellExecutionPolicy CreatePolicy(string workspaceRoot)
    {
        return new ShellExecutionPolicy(
            new AgentRouterRuntimePathResolver(),
            TestRuntimeSettings.Create(
                shellExecution: new ShellExecutionRuntimeSettings
        {
            Enabled = true,
            RequireExplicitAllowlist = true,
            TimeoutSeconds = 60,
            MaxOutputChars = 200000,
            WorkingDirectoryRoot = workspaceRoot,
            AllowWorkingDirectoryOutsideRoot = false,
            AllowShellInterpreterInlineCommands = false,
            AllowedCommands = new HashSet<string>(["dotnet", "git", "pwsh", "bash"], StringComparer.OrdinalIgnoreCase),
            DeniedCommands = new HashSet<string>(["rm", "sudo"], StringComparer.OrdinalIgnoreCase),
            WriteTraceFiles = false,
            TraceRootPath = Path.Combine("workspace", "artifacts", "shell-exec")
        }));
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "agentrouter-shell-policy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
