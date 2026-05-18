using LanguageExt;
using McpServer.Application.Abstractions.Ssh;
using McpServer.Application.Mcp.Tools;
using McpServer.Application.Ssh.Commands;
using McpServer.Application.Ssh.Results;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace McpServer.UnitTests.Application;

public sealed class SshWriteTextToolHandlerTests
{
    [Fact]
    public async Task Handle_Should_Return_Remote_File_Write_Summary()
    {
        var ssh = Substitute.For<ISshService>();
        var logger = Substitute.For<ILogger<SshWriteTextToolHandler>>();

        ssh.WriteTextAsync(Arg.Any<WriteSshTextCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Fin<SshFileWriteResult>>(new SshFileWriteResult(
                Profile: "db-admin",
                Host: "10.0.0.25",
                Port: 22,
                Username: "ops",
                Path: "/etc/postgresql/16/main/postgresql.conf",
                BytesWritten: 128,
                Success: true)));

        var handler = new SshWriteTextToolHandler(ssh, logger);
        var result = await handler.Handle(
            new SshWriteTextRequest("db-admin", "/etc/postgresql/16/main/postgresql.conf", "shared_buffers = 512MB"),
            CancellationToken.None);

        Assert.True(result.IsSucc);

        var dto = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        Assert.Contains(dto.Content, item => item.Text.Contains("postgresql.conf", StringComparison.Ordinal));
        Assert.False(dto.IsError);
        Assert.NotNull(dto.StructuredContent);
    }
}
