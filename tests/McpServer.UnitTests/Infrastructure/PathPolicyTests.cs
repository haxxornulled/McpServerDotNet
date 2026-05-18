using McpServer.Infrastructure.Files;
using Xunit;

namespace McpServer.UnitTests.Infrastructure;

public sealed class PathPolicyTests
{
    [Fact]
    public void NormalizeAndValidateWritePath_Should_Map_Relative_Path_Into_Workspace()
    {
        var workspace = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mcpserver-pathpolicy-tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(workspace);

        var sut = new PathPolicy([workspace]);

        var result = sut.NormalizeAndValidateWritePath("smoke.txt");

        Assert.True(
            result.IsSucc,
            result.Match(
                Succ: value => value,
                Fail: error => error.Message));
        var normalized = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));
        Assert.Equal(Path.Combine(workspace, "smoke.txt"), normalized);
    }

    [Fact]
    public void NormalizeAndValidateWritePath_Should_Treat_Workspace_Prefix_As_Virtual_Root()
    {
        var workspace = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mcpserver-pathpolicy-tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(workspace);

        var sut = new PathPolicy([workspace]);

        var result = sut.NormalizeAndValidateWritePath("./workspace/nested/file.txt");

        Assert.True(
            result.IsSucc,
            result.Match(
                Succ: value => value,
                Fail: error => error.Message));
        var normalized = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));
        Assert.Equal(Path.Combine(workspace, "nested", "file.txt"), normalized);
    }

    [Fact]
    public void NormalizeAndValidateWritePath_Should_Treat_LmStudio_Server_Alias_As_Virtual_Root()
    {
        var workspace = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mcpserver-pathpolicy-tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(workspace);

        var sut = new PathPolicy([workspace]);

        var result = sut.NormalizeAndValidateWritePath("/mcpserver-filesystem/nested/file.txt");

        Assert.True(
            result.IsSucc,
            result.Match(
                Succ: value => value,
                Fail: error => error.Message));
        var normalized = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));
        Assert.Equal(Path.Combine(workspace, "nested", "file.txt"), normalized);
    }

    [Fact]
    public void NormalizeAndValidateWritePath_Should_Treat_Project_Prefix_As_Virtual_Root()
    {
        var workspace = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mcpserver-pathpolicy-tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(workspace);

        var sut = new PathPolicy([workspace]);

        var result = sut.NormalizeAndValidateWritePath("./project/nested/file.txt");

        Assert.True(
            result.IsSucc,
            result.Match(
                Succ: value => value,
                Fail: error => error.Message));
        var normalized = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));
        Assert.Equal(Path.Combine(workspace, "nested", "file.txt"), normalized);
    }

    [Fact]
    public void NormalizeAndValidateWritePath_Should_Resolve_Relative_Paths_Against_Selected_Project_Root()
    {
        var workspace = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mcpserver-pathpolicy-tests", Guid.NewGuid().ToString("N")));
        var project = Path.Combine(workspace, "apps");
        Directory.CreateDirectory(project);

        var sut = new PathPolicy([workspace]);
        sut.SetProjectRoot(project);

        var result = sut.NormalizeAndValidateWritePath("note.txt");

        Assert.True(
            result.IsSucc,
            result.Match(
                Succ: value => value,
                Fail: error => error.Message));
        var normalized = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));
        Assert.Equal(Path.Combine(project, "note.txt"), normalized);
    }

    [Fact]
    public void SetWorkspaceRoot_Should_Reset_Workspace_Alias_Within_Allowed_Root()
    {
        var workspace = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mcpserver-pathpolicy-tests", Guid.NewGuid().ToString("N")));
        var nestedWorkspace = Path.Combine(workspace, "nested");
        Directory.CreateDirectory(nestedWorkspace);

        var sut = new PathPolicy([workspace]);
        sut.SetWorkspaceRoot(nestedWorkspace);

        var result = sut.NormalizeAndValidateWritePath("./workspace/file.txt");

        Assert.True(
            result.IsSucc,
            result.Match(
                Succ: value => value,
                Fail: error => error.Message));

        var normalized = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        Assert.Equal(Path.Combine(nestedWorkspace, "file.txt"), normalized);
    }

    [Fact]
    public void SetWorkspaceRoot_Should_Reject_Root_Outside_Allowed_Root()
    {
        var workspace = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mcpserver-pathpolicy-tests", Guid.NewGuid().ToString("N")));
        var outside = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mcpserver-pathpolicy-outside", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(outside);

        var sut = new PathPolicy([workspace]);

        var exception = Assert.Throws<InvalidOperationException>(() => sut.SetWorkspaceRoot(outside));
        Assert.Contains("outside the allowed roots", exception.Message, StringComparison.Ordinal);
    }

}
