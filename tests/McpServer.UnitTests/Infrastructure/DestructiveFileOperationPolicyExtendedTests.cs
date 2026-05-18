using McpServer.Infrastructure.Files;
using Xunit;

namespace McpServer.UnitTests.Infrastructure;

public sealed class DestructiveFileOperationPolicyExtendedTests
{
    [Fact]
    public void ValidateDelete_Should_Refuse_Workspace_Root()
    {
        using var workspace = new TempWorkspace();
        var sut = CreatePolicy(workspace.Root);

        var result = sut.ValidateDelete(workspace.Root, recursive: true, confirmation: $"DELETE {workspace.Root}");

        Assert.True(result.IsFail);
        Assert.Contains("workspace or project root", GetError(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateMove_Should_Refuse_Workspace_Root_Source()
    {
        using var workspace = new TempWorkspace();
        var sut = CreatePolicy(workspace.Root);
        var destination = Path.Combine(workspace.Root, "moved");

        var result = sut.ValidateMove(workspace.Root, destination, overwrite: false);

        Assert.True(result.IsFail);
        Assert.Contains("workspace or project root", GetError(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateCopy_Should_Refuse_Recursive_Workspace_Copy()
    {
        using var workspace = new TempWorkspace();
        var sut = CreatePolicy(workspace.Root);
        var destination = Path.Combine(workspace.Root, "copy");

        var result = sut.ValidateCopy(workspace.Root, destination, overwrite: false, recursive: true);

        Assert.True(result.IsFail);
        Assert.Contains("entire workspace", GetError(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateCopy_Should_Allow_NonRecursive_File_Copy_From_Workspace_Root()
    {
        using var workspace = new TempWorkspace();
        var source = Path.Combine(workspace.Root, "source.txt");
        var destination = Path.Combine(workspace.Root, "destination.txt");
        File.WriteAllText(source, "hello");
        var sut = CreatePolicy(workspace.Root);

        var result = sut.ValidateCopy(source, destination, overwrite: false, recursive: false);

        Assert.True(result.IsSucc, GetError(result));
    }

    [Fact]
    public void ValidateWrite_Should_Refuse_Overwrite_Of_Workspace_Boundary()
    {
        using var workspace = new TempWorkspace();
        var sut = CreatePolicy(workspace.Root);

        var result = sut.ValidateWrite(workspace.Root, overwriteExisting: true);

        Assert.True(result.IsFail);
        Assert.Contains("workspace boundary", GetError(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateWrite_Should_Allow_NonOverwrite_Normal_File()
    {
        using var workspace = new TempWorkspace();
        var sut = CreatePolicy(workspace.Root);
        var file = Path.Combine(workspace.Root, "normal.txt");

        var result = sut.ValidateWrite(file, overwriteExisting: false);

        Assert.True(result.IsSucc, GetError(result));
    }

    [Fact]
    public void ValidateAppend_Should_Refuse_DotEnv_File()
    {
        using var workspace = new TempWorkspace();
        var sut = CreatePolicy(workspace.Root);
        var file = Path.Combine(workspace.Root, ".env");

        var result = sut.ValidateAppend(file);

        Assert.True(result.IsFail);
        Assert.Contains("protected file", GetError(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateMove_Should_Refuse_Destination_Inside_Ssh_Metadata()
    {
        using var workspace = new TempWorkspace();
        var sut = CreatePolicy(workspace.Root);
        var source = Path.Combine(workspace.Root, "safe.txt");
        var destination = Path.Combine(workspace.Root, ".ssh", "authorized_keys");

        var result = sut.ValidateMove(source, destination, overwrite: false);

        Assert.True(result.IsFail);
        Assert.Contains("protected repository/security metadata", GetError(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateDelete_Should_Require_Exact_Recursive_Confirmation_For_Directory()
    {
        using var workspace = new TempWorkspace();
        var folder = Path.Combine(workspace.Root, "scratch");
        Directory.CreateDirectory(folder);
        var sut = CreatePolicy(workspace.Root);

        var result = sut.ValidateDelete(folder, recursive: true, confirmation: "DELETE scratch");

        Assert.True(result.IsFail);
        Assert.Contains("confirmation exactly equal", GetError(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateDelete_Should_Allow_NonRecursive_Normal_File_Delete()
    {
        using var workspace = new TempWorkspace();
        var file = Path.Combine(workspace.Root, "scratch.txt");
        File.WriteAllText(file, "hello");
        var sut = CreatePolicy(workspace.Root);

        var result = sut.ValidateDelete(file, recursive: false, confirmation: null);

        Assert.True(result.IsSucc, GetError(result));
    }

    private static DestructiveFileOperationPolicy CreatePolicy(string workspaceRoot)
    {
        return new DestructiveFileOperationPolicy(new PathPolicy([workspaceRoot]));
    }

    private static string GetError(LanguageExt.Fin<LanguageExt.Unit> result)
    {
        return result.Match(
            Succ: _ => string.Empty,
            Fail: error => error.Message);
    }

    private sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mcpserver-destructive-policy-extended-tests", Guid.NewGuid().ToString("N")));
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
