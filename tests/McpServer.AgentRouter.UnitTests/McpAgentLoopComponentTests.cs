using System.Text.Json;
using LanguageExt;
using LanguageExt.Common;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.AgentLoops;
using McpServer.AgentRouter.Application.Runtime;
using McpServer.AgentRouter.Domain.AgentLoops;
using McpServer.AgentRouter.Domain.Mcp;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace McpServer.AgentRouter.UnitTests;

public sealed class McpAgentLoopComponentTests
{
    [Fact]
    public async Task Planner_DefaultsToSafeMcpListDirectoryCall()
    {
        var planner = new McpAgentStepPlanner();
        var run = new AgentLoopRun
        {
            Id = "loop-test",
            Goal = "Inspect the workspace."
        };
        var request = new AgentLoopRequest
        {
            Goal = run.Goal
        };
        var context = new AgentLoopExecutionContext(
            run,
            request,
            maxSteps: 1,
            allowedCapabilities: new[] { "mcp.tools.call" });

        var result = await planner.PlanNextStepAsync(context, CancellationToken.None);

        Assert.True(result.IsSucc);
        var step = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        Assert.Equal("mcp.tools.call", step.Capability);
        Assert.Equal("fs.list_directory", step.ToolName);
        Assert.True(step.ArgumentsJson.HasValue);
        Assert.Equal(".", step.ArgumentsJson.Value.GetProperty("path").GetString());
    }

    [Fact]
    public async Task Runner_ExecutesMcpToolCall_AndWritesTrace()
    {
        var service = new RecordingMcpToolCallService();
        var traceWriter = new RecordingTraceWriter();
        var runner = new AutonomousLoopRunner(
            new McpAgentStepPlanner(),
            new ExplicitAllowlistToolExecutionPolicy(CreateSettings()),
            new McpAgentToolExecutor(service, NullLogger<McpAgentToolExecutor>.Instance),
            new McpAgentResultInspector(),
            new BoundedAgentLoopValidator(),
            traceWriter,
            CreateSettings(),
            NullLogger<AutonomousLoopRunner>.Instance);

        var result = await runner.RunAsync(new AgentLoopRequest
        {
            Goal = "List the workspace once.",
            MaxSteps = 1,
            ToolName = "fs.list_directory",
            Arguments = JsonSerializer.SerializeToElement(new
            {
                path = "."
            })
        }, CancellationToken.None);

        Assert.True(result.IsSucc);
        var run = result.Match(
            Succ: value => value,
            Fail: error => throw new InvalidOperationException(error.Message));

        Assert.Equal(AgentLoopStatusNames.Completed, run.Status);
        Assert.Single(run.Steps);
        Assert.Equal("mcp.tools.call", run.Steps[0].Capability);
        Assert.Equal("fs.list_directory", run.Steps[0].ToolName);
        Assert.Equal(ToolExecutionStatus.Succeeded, run.Steps[0].Status);
        Assert.Contains("Listed", run.Result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("artifacts", run.Result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mcp-test-trace", run.Result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("structuredContent", run.Result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"content\"", run.Result, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("mcp-test-trace", run.Steps[0].TraceId);
        Assert.Contains("Listed", run.Steps[0].OutputSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Single(service.Requests);
        Assert.Single(traceWriter.Steps);
        Assert.Single(traceWriter.Runs);
    }

    private static AgentRouterRuntimeSettings CreateSettings()
    {
        return TestRuntimeSettings.Create(
            agentLoop: new AgentLoopRuntimeSettings
        {
            Enabled = true,
            MaxSteps = 1,
            MaxToolCalls = 20,
            MaxRuntimeSeconds = 300,
            RequireExplicitAllowlist = true,
            AllowedCapabilities = ["mcp.tools.call"],
            WriteTraceFiles = false,
            TraceEveryStep = false,
            TraceRootPath = Path.Combine("workspace", "artifacts", "agent-loops")
        });
    }

    private sealed class RecordingMcpToolCallService : IMcpToolCallService
    {
        private readonly object _syncRoot = new();
        private readonly List<McpToolCallRequest> _requests = [];

        public IReadOnlyList<McpToolCallRequest> Requests
        {
            get
            {
                lock (_syncRoot)
                {
                    return _requests.ToArray();
                }
            }
        }

        public ValueTask<Fin<McpToolCallResponse>> CallToolAsync(
            McpToolCallRequest? request,
            CancellationToken cancellationToken)
        {
            Assert.NotNull(request);

            lock (_syncRoot)
            {
                _requests.Add(request);
            }

            return new ValueTask<Fin<McpToolCallResponse>>(Fin<McpToolCallResponse>.Succ(new McpToolCallResponse
            {
                Status = McpToolCallStatusNames.Completed,
                ToolName = request!.ToolName ?? string.Empty,
                Allowed = true,
                PolicyDecision = "allowed",
                TraceId = "mcp-test-trace",
                Transport = "stdio",
                ElapsedMilliseconds = 7,
                Result = JsonSerializer.SerializeToElement(new
                {
                    content = new[]
                    {
                        new
                        {
                            type = "text",
                            text = "[{\"name\":\"artifacts\",\"is_directory\":true}]"
                        }
                    },
                    structuredContent = new
                    {
                        path = "D:\\2026 Projects\\McpServerRepo\\workspace",
                        entries = new[]
                        {
                            new
                            {
                                name = "artifacts",
                                isDirectory = true
                            }
                        }
                    },
                    isError = false
                })
            }));
        }
    }

    private sealed class RecordingTraceWriter : IAgentTraceWriter
    {
        private readonly object _syncRoot = new();
        private readonly List<AgentLoopStep> _steps = [];
        private readonly List<AgentLoopRun> _runs = [];

        public IReadOnlyList<AgentLoopStep> Steps
        {
            get
            {
                lock (_syncRoot)
                {
                    return _steps.ToArray();
                }
            }
        }

        public IReadOnlyList<AgentLoopRun> Runs
        {
            get
            {
                lock (_syncRoot)
                {
                    return _runs.ToArray();
                }
            }
        }

        public ValueTask<Fin<Unit>> WriteStepAsync(
            AgentLoopRun run,
            AgentLoopStep step,
            CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                _steps.Add(step);
            }

            return new ValueTask<Fin<Unit>>(Prelude.unit);
        }

        public ValueTask<Fin<Unit>> WriteRunAsync(
            AgentLoopRun run,
            CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                _runs.Add(run);
            }

            return new ValueTask<Fin<Unit>>(Prelude.unit);
        }
    }
}
