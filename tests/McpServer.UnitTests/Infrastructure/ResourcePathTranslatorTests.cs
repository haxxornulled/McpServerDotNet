using McpServer.Infrastructure.Files;
using Xunit;

namespace McpServer.UnitTests.Infrastructure;

public sealed class ResourcePathTranslatorTests
{
    [Fact]
    public void TryTranslateToLocalPath_Should_Map_Workspace_File_Uri_To_Local_Path()
    {
        var workspace = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mcpserver-resource-tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(workspace);

        var sut = new ResourcePathTranslator(workspace);

        var result = sut.TryTranslateToLocalPath("file:///workspace/folder/test.txt");

        Assert.True(result.IsSucc);
        var translated = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));
        Assert.Equal(Path.Combine(workspace, "folder", "test.txt"), translated);
    }

    [Fact]
    public void TryTranslateToLocalPath_Should_Map_Workspace_Directory_Uri_To_Workspace_Root()
    {
        var workspace = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mcpserver-resource-tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(workspace);

        var sut = new ResourcePathTranslator(workspace);

        var result = sut.TryTranslateToLocalPath("dir:///workspace");

        Assert.True(result.IsSucc);
        var translated = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));
        Assert.Equal(workspace, translated);
    }

    [Fact]
    public void TryTranslateToLocalPath_Should_Map_Project_File_Uri_To_Local_Path()
    {
        var workspace = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mcpserver-resource-tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(workspace);

        var sut = new ResourcePathTranslator(workspace);

        var result = sut.TryTranslateToLocalPath("file:///project/folder/test.txt");

        Assert.True(result.IsSucc);
        var translated = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));
        Assert.Equal(Path.Combine(workspace, "folder", "test.txt"), translated);
    }

    [Fact]
    public void TryTranslateToLocalPath_Should_Use_Selected_Project_Root_For_Project_Uris()
    {
        var workspace = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "mcpserver-resource-tests", Guid.NewGuid().ToString("N")));
        var project = Path.Combine(workspace, "apps");
        Directory.CreateDirectory(project);

        var sut = new ResourcePathTranslator(workspace);
        sut.SetProjectRoot(project);

        var result = sut.TryTranslateToLocalPath("file:///project/folder/test.txt");

        Assert.True(result.IsSucc);
        var translated = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));
        Assert.Equal(Path.Combine(project, "folder", "test.txt"), translated);
    }
}
