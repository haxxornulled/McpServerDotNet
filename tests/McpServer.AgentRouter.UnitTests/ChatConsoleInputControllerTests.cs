using System.IO;
using System.Threading;
using System.Threading.Tasks;
using McpServer.AgentRouter.Tools;
using Xunit;

namespace McpServer.AgentRouter.UnitTests;

public sealed class ChatConsoleInputControllerTests
{
    [Fact]
    public async Task Paste_Mode_Should_Collect_MultiLine_Prompt()
    {
        using var input = new StringReader("""
            /paste
            First line
            Second line
            /end
            """);

        var controller = new ChatConsoleInputController(input, new FakeClipboardTextProvider(null));

        var modeStarted = await controller.ReadNextSubmissionAsync(CancellationToken.None);
        var submission = await controller.ReadNextSubmissionAsync(CancellationToken.None);

        Assert.Equal(ChatConsoleInputKind.PasteModeStarted, modeStarted.Kind);
        Assert.Equal("Paste mode active. Paste text, then finish with /end.", modeStarted.Notice);
        Assert.Equal(ChatConsoleInputKind.Prompt, submission.Kind);
        Assert.Equal($"First line{System.Environment.NewLine}Second line", submission.Prompt);
    }

    [Fact]
    public async Task Paste_Command_Should_Use_Clipboard_Content_When_Available()
    {
        using var input = new StringReader("/paste");

        var controller = new ChatConsoleInputController(input, new FakeClipboardTextProvider("One\r\nTwo"));

        var submission = await controller.ReadNextSubmissionAsync(CancellationToken.None);

        Assert.Equal(ChatConsoleInputKind.Prompt, submission.Kind);
        Assert.Equal("One\nTwo", submission.Prompt);
    }

    [Fact]
    public async Task Exit_Command_Should_End_Interactive_Session()
    {
        using var input = new StringReader("exit");

        var controller = new ChatConsoleInputController(input, new FakeClipboardTextProvider(null));

        var submission = await controller.ReadNextSubmissionAsync(CancellationToken.None);

        Assert.Equal(ChatConsoleInputKind.Exit, submission.Kind);
        Assert.Null(submission.Prompt);
    }

    [Fact]
    public async Task Search_Command_Should_Produce_A_Web_Search_Tool_Command()
    {
        using var input = new StringReader("/search latest MCPServer docs");

        var controller = new ChatConsoleInputController(input, new FakeClipboardTextProvider(null));

        var submission = await controller.ReadNextSubmissionAsync(CancellationToken.None);

        Assert.Equal(ChatConsoleInputKind.ToolCommand, submission.Kind);
        Assert.Equal("web.search", submission.ToolName);
        Assert.Equal("latest MCPServer docs", submission.Prompt);
        Assert.Equal("Web search submitted.", submission.Notice);
    }

    private sealed class FakeClipboardTextProvider : IClipboardTextProvider
    {
        private readonly string? _text;

        public FakeClipboardTextProvider(string? text)
        {
            _text = text;
        }

        public Task<string?> GetTextAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<string?>(_text);
        }
    }
}
