using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.Mcp;
using McpServer.AgentRouter.Application.Runtime;
using McpServer.AgentRouter.Domain.Mcp;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace McpServer.AgentRouter.UnitTests;

public sealed class McpToolCallServiceTests
{
    [Fact]
    public async Task CallToolAsync_ExecutesAllowedTool_AndWritesTrace()
    {
        var client = Substitute.For<IMcpToolCallClient>();
        client.CallToolAsync(Arg.Any<McpToolCallCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var command = callInfo.Arg<McpToolCallCommand>();
                return new ValueTask<Fin<McpToolInvocationResult>>(Fin<McpToolInvocationResult>.Succ(new McpToolInvocationResult
                {
                    Status = McpToolCallStatusNames.Completed,
                    ToolName = command.ToolName,
                    Transport = "stdio",
                    ElapsedMilliseconds = 42,
                    Result = JsonSerializer.SerializeToElement(new
                    {
                        ok = true
                    })
                }));
            });

        var traceWriter = new RecordingMcpToolCallTraceWriter();
        var service = CreateService(client, traceWriter);

        var result = await service.CallToolAsync(new McpToolCallRequest
        {
            ToolName = "fs.list_directory",
            Arguments = JsonSerializer.SerializeToElement(new
            {
                path = "."
            })
        }, CancellationToken.None);

        Assert.True(result.IsSucc);

        var response = result.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Expected successful MCP tool call."));

        Assert.Equal(McpToolCallStatusNames.Completed, response.Status);
        Assert.Equal("fs.list_directory", response.ToolName);
        Assert.True(response.Allowed);
        Assert.StartsWith("mcp-call-", response.TraceId, StringComparison.Ordinal);
        Assert.Single(traceWriter.Records);
        Assert.Equal(response.TraceId, traceWriter.Records[0].TraceId);
        await client.Received(1).CallToolAsync(Arg.Any<McpToolCallCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CallToolAsync_DeniesToolOutsideAllowlist_AndDoesNotInvokeClient()
    {
        var client = Substitute.For<IMcpToolCallClient>();
        var traceWriter = new RecordingMcpToolCallTraceWriter();
        var service = CreateService(client, traceWriter);

        var result = await service.CallToolAsync(new McpToolCallRequest
        {
            ToolName = "fs.delete_path",
            Arguments = JsonSerializer.SerializeToElement(new
            {
                path = "do-not-delete"
            })
        }, CancellationToken.None);

        Assert.True(result.IsSucc);

        var response = result.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Expected denied response envelope."));

        Assert.Equal(McpToolCallStatusNames.Denied, response.Status);
        Assert.False(response.Allowed);
        Assert.Equal("denied", response.PolicyDecision);
        Assert.Contains("not in AgentRouter:ToolExecution:AllowedTools", response.PolicyReason, StringComparison.OrdinalIgnoreCase);
        Assert.Single(traceWriter.Records);
        await client.DidNotReceive().CallToolAsync(Arg.Any<McpToolCallCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CallToolAsync_FailsValidation_WhenToolNameIsMissing()
    {
        var service = CreateService(
            Substitute.For<IMcpToolCallClient>(),
            new RecordingMcpToolCallTraceWriter());

        var result = await service.CallToolAsync(new McpToolCallRequest(), CancellationToken.None);

        Assert.True(result.IsFail);
    }

    private static McpToolCallService CreateService(
        IMcpToolCallClient client,
        IMcpToolCallTraceWriter traceWriter)
    {
        var settings = TestRuntimeSettings.Create(
            toolExecution: new McpToolExecutionRuntimeSettings
            {
                Enabled = true,
                RequireExplicitAllowlist = true,
                TimeoutSeconds = 20,
                MaxOutputChars = 200000,
                AllowedTools = new System.Collections.Generic.HashSet<string>(["fs.list_directory", "fs.get_metadata"], StringComparer.OrdinalIgnoreCase),
                WriteTraceFiles = false,
                TraceRootPath = Path.Combine("workspace", "artifacts", "mcp-tool-calls")
            });

        return new McpToolCallService(
            new McpToolCallPolicy(settings),
            client,
            traceWriter,
            settings,
            NullLogger<McpToolCallService>.Instance);
    }

    private sealed class RecordingMcpToolCallTraceWriter : IMcpToolCallTraceWriter
    {
        private readonly object _syncRoot = new();
        private readonly List<McpToolCallTraceRecord> _records = [];

        public IReadOnlyList<McpToolCallTraceRecord> Records
        {
            get
            {
                lock (_syncRoot)
                {
                    return _records.ToArray();
                }
            }
        }

        public ValueTask<Fin<Unit>> WriteAsync(
            McpToolCallTraceRecord traceRecord,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_syncRoot)
            {
                _records.Add(traceRecord);
            }

            return new ValueTask<Fin<Unit>>(Prelude.unit);
        }
    }
}
