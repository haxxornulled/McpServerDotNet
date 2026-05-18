using System.Text;
using System.Text.Json;
using McpServer.Host.Transport.Stdio;
using McpServer.Protocol.JsonRpc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace McpServer.UnitTests.Transport;

public sealed class StdioMessageTransportTests
{
    [Fact]
    public async Task ReadRequestAsync_Should_Read_NewlineDelimited_Request()
    {
        var input = new MemoryStream(Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\",\"params\":{}}\n"));
        var output = new MemoryStream();
        var logger = Substitute.For<ILogger<StdioMessageTransport>>();

        await using var transport = new StdioMessageTransport(input, output, logger);

        var readResult = await transport.ReadRequestAsync(CancellationToken.None);
        JsonRpcRequest? request = readResult.Request;

        Assert.NotNull(request);
        Assert.False(readResult.EndOfStream);
        Assert.Equal("tools/list", request!.Method);
    }

    [Fact]
    public async Task ReadRequestAsync_Should_Read_Single_ContentLength_Framed_Request()
    {
        string body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{}}";
        var input = new MemoryStream(BuildFrameBytes(body));
        var output = new MemoryStream();
        var logger = Substitute.For<ILogger<StdioMessageTransport>>();

        await using var transport = new StdioMessageTransport(input, output, logger);

        var readResult = await transport.ReadRequestAsync(CancellationToken.None);
        JsonRpcRequest? request = readResult.Request;

        Assert.NotNull(request);
        Assert.False(readResult.EndOfStream);
        Assert.Equal("initialize", request!.Method);
        Assert.NotNull(request.Id);
    }

    [Fact]
    public async Task ReadRequestAsync_Should_Read_Multiple_ContentLength_Framed_Requests_BackToBack()
    {
        string firstBody = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{}}";
        string secondBody = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}";
        byte[] inputBytes = Concat(BuildFrameBytes(firstBody), BuildFrameBytes(secondBody));
        var input = new MemoryStream(inputBytes);
        var output = new MemoryStream();
        var logger = Substitute.For<ILogger<StdioMessageTransport>>();

        await using var transport = new StdioMessageTransport(input, output, logger);

        JsonRpcRequest? first = (await transport.ReadRequestAsync(CancellationToken.None)).Request;
        JsonRpcRequest? second = (await transport.ReadRequestAsync(CancellationToken.None)).Request;

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal("initialize", first!.Method);
        Assert.Equal("tools/list", second!.Method);
    }

    [Fact]
    public async Task ReadRequestAsync_Should_Return_Null_For_Header_Block_Without_ContentLength()
    {
        byte[] inputBytes = Encoding.UTF8.GetBytes("Content-Type: application/vscode-jsonrpc; charset=utf-8\r\n\r\n");
        var input = new MemoryStream(inputBytes);
        var output = new MemoryStream();
        var logger = Substitute.For<ILogger<StdioMessageTransport>>();

        await using var transport = new StdioMessageTransport(input, output, logger);

        var readResult = await transport.ReadRequestAsync(CancellationToken.None);
        JsonRpcRequest? request = readResult.Request;

        Assert.Null(request);
        Assert.False(readResult.EndOfStream);
    }

    [Fact]
    public async Task ReadRequestAsync_Should_Return_Null_For_Malformed_ContentLength()
    {
        byte[] inputBytes = Encoding.UTF8.GetBytes("Content-Length: nope\r\n\r\n{}");
        var input = new MemoryStream(inputBytes);
        var output = new MemoryStream();
        var logger = Substitute.For<ILogger<StdioMessageTransport>>();

        await using var transport = new StdioMessageTransport(input, output, logger);

        var readResult = await transport.ReadRequestAsync(CancellationToken.None);
        JsonRpcRequest? request = readResult.Request;

        Assert.Null(request);
        Assert.False(readResult.EndOfStream);
    }

    [Fact]
    public async Task ReadRequestAsync_Should_Use_ContentLength_Byte_Count_For_Utf8_Body()
    {
        string body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/π\",\"params\":{\"text\":\"hello 🌋\"}}";
        var input = new MemoryStream(BuildFrameBytes(body));
        var output = new MemoryStream();
        var logger = Substitute.For<ILogger<StdioMessageTransport>>();

        await using var transport = new StdioMessageTransport(input, output, logger);

        var readResult = await transport.ReadRequestAsync(CancellationToken.None);
        JsonRpcRequest? request = readResult.Request;

        Assert.NotNull(request);
        Assert.False(readResult.EndOfStream);
        Assert.Equal("tools/π", request!.Method);
    }

    [Fact]
    public async Task WriteResponseAsync_Should_Write_NewlineDelimited_Response_By_Default()
    {
        var input = new MemoryStream();
        var output = new MemoryStream();
        var logger = Substitute.For<ILogger<StdioMessageTransport>>();

        await using var transport = new StdioMessageTransport(input, output, logger);
        JsonElement id = JsonDocument.Parse("1").RootElement.Clone();
        var response = new JsonRpcResponse("2.0", id, Result: new { ok = true });

        await transport.WriteResponseAsync(response, CancellationToken.None);

        string text = ReadOutputText(output);
        Assert.EndsWith("\n", text);
        Assert.False(
            text.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase),
            "Newline-delimited responses must not be Content-Length framed by default.");
        Assert.DoesNotContain("\"error\":null", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteResponseAsync_Should_Write_ContentLength_Framed_Response_After_Framed_Input()
    {
        string body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{}}";
        var input = new MemoryStream(BuildFrameBytes(body));
        var output = new MemoryStream();
        var logger = Substitute.For<ILogger<StdioMessageTransport>>();

        await using var transport = new StdioMessageTransport(input, output, logger);
        JsonRpcRequest? request = (await transport.ReadRequestAsync(CancellationToken.None)).Request;
        Assert.NotNull(request);

        JsonElement id = JsonDocument.Parse("1").RootElement.Clone();
        var response = new JsonRpcResponse("2.0", id, Result: new { ok = true });

        await transport.WriteResponseAsync(response, CancellationToken.None);

        string text = ReadOutputText(output);
        Assert.StartsWith("Content-Length:", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\r\n\r\n", text, StringComparison.Ordinal);
        Assert.Contains("\"ok\":true", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteNotificationAsync_Should_Write_NewlineDelimited_Notification_By_Default()
    {
        var input = new MemoryStream();
        var output = new MemoryStream();
        var logger = Substitute.For<ILogger<StdioMessageTransport>>();

        await using var transport = new StdioMessageTransport(input, output, logger);
        var notification = new JsonRpcNotification("2.0", "notifications/workspace/changed", new { ok = true });

        await transport.WriteNotificationAsync(notification, CancellationToken.None);

        string text = ReadOutputText(output);
        Assert.EndsWith("\n", text);
        Assert.Contains("\"notifications/workspace/changed\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadRequestAsync_Should_Report_EndOfStream_When_Input_Is_Closed()
    {
        var input = new MemoryStream(Array.Empty<byte>());
        var output = new MemoryStream();
        var logger = Substitute.For<ILogger<StdioMessageTransport>>();

        await using var transport = new StdioMessageTransport(input, output, logger);

        var readResult = await transport.ReadRequestAsync(CancellationToken.None);

        Assert.Null(readResult.Request);
        Assert.True(readResult.EndOfStream);
    }

    [Fact]
    public async Task WriteResponseAsync_Should_Flush_Stream_Once()
    {
        var input = new MemoryStream();
        var output = new CountingStream();
        var logger = Substitute.For<ILogger<StdioMessageTransport>>();

        await using var transport = new StdioMessageTransport(input, output, logger);
        JsonElement id = JsonDocument.Parse("1").RootElement.Clone();
        var response = new JsonRpcResponse("2.0", id, Result: new { ok = true });

        await transport.WriteResponseAsync(response, CancellationToken.None);

        Assert.Equal(1, output.FlushAsyncCount);
    }

    private static byte[] BuildFrameBytes(string body)
    {
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        byte[] headerBytes = Encoding.ASCII.GetBytes($"Content-Length: {bodyBytes.Length}\r\n\r\n");
        return Concat(headerBytes, bodyBytes);
    }

    private static byte[] Concat(params byte[][] chunks)
    {
        int length = chunks.Sum(chunk => chunk.Length);
        byte[] result = new byte[length];
        int offset = 0;

        foreach (byte[] chunk in chunks)
        {
            Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
            offset += chunk.Length;
        }

        return result;
    }

    private static string ReadOutputText(MemoryStream output)
    {
        output.Position = 0;
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private sealed class CountingStream : MemoryStream
    {
        public int FlushAsyncCount { get; private set; }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            FlushAsyncCount++;
            return base.FlushAsync(cancellationToken);
        }
    }
}
