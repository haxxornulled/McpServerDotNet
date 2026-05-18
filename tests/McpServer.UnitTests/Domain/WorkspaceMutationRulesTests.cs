using LanguageExt;
using McpServer.Domain.Workspace;
using Xunit;

namespace McpServer.UnitTests.Domain;

public sealed class WorkspaceMutationRulesTests
{
    [Fact]
    public void ValidateProtectedPath_Should_Refuse_Protected_Metadata_Directory()
    {
        using var workspace = new TempWorkspace();
        var gitConfig = Path.Combine(workspace.Root, ".git", "config");
        Directory.CreateDirectory(Path.GetDirectoryName(gitConfig)!);
        File.WriteAllText(gitConfig, "metadata");

        var result = WorkspaceMutationRules.ValidateProtectedPath(gitConfig, "write");

        Assert.True(result.IsFail);
        var error = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected protected metadata write to fail."),
            Fail: failure => failure.Message);
        Assert.Contains("protected repository/security metadata", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateProtectedPath_Should_Refuse_Protected_Key_File()
    {
        using var workspace = new TempWorkspace();
        var keyPath = Path.Combine(workspace.Root, "secret.pem");

        var result = WorkspaceMutationRules.ValidateProtectedPath(keyPath, "write");

        Assert.True(result.IsFail);
        var error = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected protected key write to fail."),
            Fail: failure => failure.Message);
        Assert.Contains("protected file", error, StringComparison.Ordinal);
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mcpserver-domain-tests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
