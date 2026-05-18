using McpServer.Infrastructure.Files;
using Xunit;

namespace McpServer.UnitTests.Infrastructure;

public sealed class DestructiveFileOperationPolicyTests
{
    [Fact]
    public void ValidateDelete_Should_Refuse_Protected_Metadata_Directory()
    {
        using var workspace = new TempWorkspace();
        var gitConfig = Path.Combine(workspace.Root, ".git", "config");
        Directory.CreateDirectory(Path.GetDirectoryName(gitConfig)!);
        File.WriteAllText(gitConfig, "metadata");

        var sut = new DestructiveFileOperationPolicy(new PathPolicy([workspace.Root]));

        var result = sut.ValidateDelete(gitConfig, recursive: false, confirmation: null);

        Assert.True(result.IsFail);
        var error = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected protected metadata delete to fail."),
            Fail: failure => failure.Message);
        Assert.Contains("protected repository/security metadata", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateWrite_Should_Refuse_Protected_Key_File()
    {
        using var workspace = new TempWorkspace();
        var keyPath = Path.Combine(workspace.Root, "secret.pem");
        var sut = new DestructiveFileOperationPolicy(new PathPolicy([workspace.Root]));

        var result = sut.ValidateWrite(keyPath, overwriteExisting: false);

        Assert.True(result.IsFail);
        var error = result.Match(
            Succ: _ => throw new InvalidOperationException("Expected protected key write to fail."),
            Fail: failure => failure.Message);
        Assert.Contains("protected file", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateDelete_Should_Allow_Recursive_Delete_With_Exact_Normalized_Confirmation()
    {
        using var workspace = new TempWorkspace();
        var folder = Path.Combine(workspace.Root, "scratch");
        Directory.CreateDirectory(folder);
        var normalized = Path.GetFullPath(folder);
        var sut = new DestructiveFileOperationPolicy(new PathPolicy([workspace.Root]));

        var result = sut.ValidateDelete(
            normalized,
            recursive: true,
            confirmation: $"DELETE {normalized}");

        Assert.True(result.IsSucc, result.Match(
            Succ: _ => string.Empty,
            Fail: failure => failure.Message));
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mcpserver-destructive-policy-tests", Guid.NewGuid().ToString("N")));
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
