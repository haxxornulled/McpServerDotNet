using LanguageExt;
using LanguageExt.Common;
using McpServer.AgentRouter.Application.Abstractions;
using McpServer.AgentRouter.Application.AgentLoops;
using McpServer.AgentRouter.Application.Runtime;
using McpServer.AgentRouter.Domain.AgentLoops;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace McpServer.AgentRouter.UnitTests;

public sealed class AutonomousLoopRunnerTests
{
    [Fact]
    public async Task RunAsync_StopsAtMaxSteps_AndWritesTraceForEachStep()
    {
        var planner = new SequencePlanner(
            CreatePlannedStep("fake.observe"),
            CreatePlannedStep("fake.validate"));
        var executor = new RecordingToolExecutor();
        var inspector = new ContinueInspector();
        var validator = new ContinueValidator();
        var traceWriter = new RecordingTraceWriter();
        var runner = CreateRunner(
            planner,
            new ExplicitAllowlistToolExecutionPolicy(CreateSettings(maxSteps: 2)),
            executor,
            inspector,
            validator,
            traceWriter,
            maxSteps: 2);

        var result = await runner.RunAsync(new AgentLoopRequest
        {
            Goal = "Exercise bounded fake loop.",
            MaxSteps = 2
        }, CancellationToken.None);

        Assert.True(result.IsSucc);

        var run = result.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Expected loop success envelope."));

        Assert.Equal(AgentLoopStatusNames.Failed, run.Status);
        Assert.Equal("max_steps_reached", run.Error!.Code);
        Assert.Equal(2, run.Steps.Count);
        Assert.Equal(2, executor.ExecutedSteps.Count);
        Assert.Equal(2, traceWriter.Steps.Count);
        Assert.Single(traceWriter.Runs);
    }

    [Fact]
    public async Task RunAsync_ChecksPolicyBeforeExecution()
    {
        var planner = new SequencePlanner(CreatePlannedStep("ssh.exec"));
        var policy = new DenyAllPolicy("ssh is not allowed");
        var executor = new RecordingToolExecutor();
        var traceWriter = new RecordingTraceWriter();
        var runner = CreateRunner(
            planner,
            policy,
            executor,
            new ContinueInspector(),
            new ContinueValidator(),
            traceWriter);

        var result = await runner.RunAsync(new AgentLoopRequest
        {
            Goal = "Try a denied capability."
        }, CancellationToken.None);

        Assert.True(result.IsSucc);

        var run = result.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Expected loop result."));

        Assert.Equal(AgentLoopStatusNames.Failed, run.Status);
        Assert.Equal("policy_denied", run.Error!.Code);
        Assert.Empty(executor.ExecutedSteps);
        Assert.Single(traceWriter.Steps);
        Assert.Equal(ToolExecutionStatus.Denied, run.Steps[0].Status);
    }

    [Fact]
    public async Task RunAsync_CapturesToolFailure_AndStopsWithoutRetryingSilently()
    {
        var planner = new SequencePlanner(CreatePlannedStep("fake.observe"));
        var executor = new RecordingToolExecutor
        {
            Result = new AgentToolExecutionResult
            {
                Status = ToolExecutionStatus.Failed,
                ExitCode = 1,
                OutputSummary = "tool failed",
                ErrorMessage = "simulated failure"
            }
        };
        var traceWriter = new RecordingTraceWriter();
        var runner = CreateRunner(
            planner,
            AllowAllPolicy.Instance,
            executor,
            new ContinueInspector(),
            new ContinueValidator(),
            traceWriter);

        var result = await runner.RunAsync(new AgentLoopRequest
        {
            Goal = "Capture tool failure."
        }, CancellationToken.None);

        Assert.True(result.IsSucc);

        var run = result.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Expected loop result."));

        Assert.Equal(AgentLoopStatusNames.Failed, run.Status);
        Assert.Equal("tool_reported_failure", run.Error!.Code);
        Assert.Single(executor.ExecutedSteps);
        Assert.Single(traceWriter.Steps);
        Assert.Equal(ToolExecutionStatus.Failed, run.Steps[0].Status);
        Assert.Equal("simulated failure", run.Steps[0].ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_ValidatorCanStopLoopSuccessfully()
    {
        var planner = new SequencePlanner(CreatePlannedStep("fake.observe"));
        var executor = new RecordingToolExecutor();
        var validator = new StopSucceededValidator("validator accepted outcome");
        var traceWriter = new RecordingTraceWriter();
        var runner = CreateRunner(
            planner,
            AllowAllPolicy.Instance,
            executor,
            new ContinueInspector(),
            validator,
            traceWriter,
            maxSteps: 8);

        var result = await runner.RunAsync(new AgentLoopRequest
        {
            Goal = "Validator stops after first step."
        }, CancellationToken.None);

        Assert.True(result.IsSucc);

        var run = result.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Expected loop result."));

        Assert.Equal(AgentLoopStatusNames.Completed, run.Status);
        Assert.Equal("validator accepted outcome", run.Result);
        Assert.Single(run.Steps);
        Assert.Single(traceWriter.Steps);
    }

    [Fact]
    public async Task RunAsync_FailsValidation_WhenGoalIsMissing()
    {
        var runner = CreateRunner(
            new SequencePlanner(CreatePlannedStep("fake.observe")),
            AllowAllPolicy.Instance,
            new RecordingToolExecutor(),
            new ContinueInspector(),
            new ContinueValidator(),
            new RecordingTraceWriter());

        var result = await runner.RunAsync(new AgentLoopRequest(), CancellationToken.None);

        Assert.True(result.IsFail);
    }


    [Fact]
    public async Task RunAsync_DoesNotAllowRequestToBroadenConfiguredCapabilities()
    {
        var planner = new SequencePlanner(CreatePlannedStep("ssh.exec"));
        var executor = new RecordingToolExecutor();
        var traceWriter = new RecordingTraceWriter();
        var runner = CreateRunner(
            planner,
            new ExplicitAllowlistToolExecutionPolicy(CreateSettings(maxSteps: 1)),
            executor,
            new ContinueInspector(),
            new ContinueValidator(),
            traceWriter,
            maxSteps: 1);

        var result = await runner.RunAsync(new AgentLoopRequest
        {
            Goal = "Try to broaden loop capabilities from the request.",
            MaxSteps = 1,
            AllowedCapabilities = new List<string>
            {
                "ssh.exec"
            }
        }, CancellationToken.None);

        Assert.True(result.IsSucc);

        var run = result.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Expected loop result."));

        Assert.Equal(AgentLoopStatusNames.Failed, run.Status);
        Assert.Equal("policy_denied", run.Error!.Code);
        Assert.Empty(executor.ExecutedSteps);
        Assert.Single(traceWriter.Steps);
        Assert.Equal(ToolExecutionStatus.Denied, run.Steps[0].Status);
    }

    [Fact]
    public async Task RunAsync_AllowsRequestToNarrowConfiguredCapabilities()
    {
        var planner = new SequencePlanner(CreatePlannedStep("fake.observe"));
        var executor = new RecordingToolExecutor();
        var traceWriter = new RecordingTraceWriter();
        var runner = CreateRunner(
            planner,
            new ExplicitAllowlistToolExecutionPolicy(CreateSettings(maxSteps: 1)),
            executor,
            new StopSucceededInspector("narrowed capability executed"),
            new ContinueValidator(),
            traceWriter,
            maxSteps: 1);

        var result = await runner.RunAsync(new AgentLoopRequest
        {
            Goal = "Narrow configured loop capabilities from the request.",
            MaxSteps = 1,
            AllowedCapabilities = new List<string>
            {
                "fake.observe",
                "ssh.exec"
            }
        }, CancellationToken.None);

        Assert.True(result.IsSucc);

        var run = result.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Expected loop result."));

        Assert.Equal(AgentLoopStatusNames.Completed, run.Status);
        Assert.Equal("narrowed capability executed", run.Result);
        Assert.Single(executor.ExecutedSteps);
        Assert.Single(traceWriter.Steps);
    }

    [Fact]
    public async Task RunAsync_StopsBeforeExecution_WhenMaxToolCallsIsReached()
    {
        var planner = new SequencePlanner(CreatePlannedStep("fake.observe"));
        var executor = new RecordingToolExecutor();
        var traceWriter = new RecordingTraceWriter();
        var runner = CreateRunner(
            planner,
            new ExplicitAllowlistToolExecutionPolicy(CreateSettings(maxSteps: 1, maxToolCalls: 0)),
            executor,
            new ContinueInspector(),
            new ContinueValidator(),
            traceWriter,
            maxSteps: 1,
            maxToolCalls: 0);

        var result = await runner.RunAsync(new AgentLoopRequest
        {
            Goal = "Hit max tool calls before execution.",
            MaxSteps = 1
        }, CancellationToken.None);

        Assert.True(result.IsSucc);

        var run = result.Match(
            Succ: value => value,
            Fail: _ => throw new InvalidOperationException("Expected loop result."));

        Assert.Equal(AgentLoopStatusNames.Failed, run.Status);
        Assert.Equal("max_tool_calls_reached", run.Error!.Code);
        Assert.Empty(executor.ExecutedSteps);
        Assert.Single(traceWriter.Steps);
        Assert.Equal(ToolExecutionStatus.Failed, run.Steps[0].Status);
    }

    [Fact]
    public async Task RunAsync_CanRunConcurrently_WithSharedRunner_WithoutTraceCrossContamination()
    {
        var traceWriter = new RecordingTraceWriter();
        var runner = CreateRunner(
            new FakeAgentStepPlanner(),
            AllowAllPolicy.Instance,
            new RecordingToolExecutor(),
            new BasicAgentResultInspector(),
            new BoundedAgentLoopValidator(),
            traceWriter,
            maxSteps: 2);

        var tasks = Enumerable
            .Range(1, 20)
            .Select(index => runner.RunAsync(new AgentLoopRequest
            {
                Goal = $"Concurrent loop {index}.",
                MaxSteps = 2
            }, CancellationToken.None).AsTask())
            .ToArray();

        var results = await Task.WhenAll(tasks);
        var runs = results
            .Select(result => result.Match(
                Succ: run => run,
                Fail: error => throw new InvalidOperationException(error.Message)))
            .ToArray();

        Assert.All(results, result => Assert.True(result.IsSucc));
        Assert.All(runs, run => Assert.Equal(AgentLoopStatusNames.Completed, run.Status));
        Assert.Equal(runs.Length, runs.Select(run => run.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(40, traceWriter.Steps.Count);
        Assert.Equal(20, traceWriter.Runs.Count);
    }

    private static AutonomousLoopRunner CreateRunner(
        IAgentStepPlanner planner,
        IToolExecutionPolicy policy,
        IAgentToolExecutor executor,
        IAgentResultInspector inspector,
        IAgentLoopValidator validator,
        IAgentTraceWriter traceWriter,
        int maxSteps = 4,
        int maxToolCalls = 20,
        int maxRuntimeSeconds = 300)
    {
        return new AutonomousLoopRunner(
            planner,
            policy,
            executor,
            inspector,
            validator,
            traceWriter,
            CreateSettings(maxSteps, maxToolCalls, maxRuntimeSeconds),
            NullLogger<AutonomousLoopRunner>.Instance);
    }

    private static AgentRouterRuntimeSettings CreateSettings(
        int maxSteps = 4,
        int maxToolCalls = 20,
        int maxRuntimeSeconds = 300)
    {
        return TestRuntimeSettings.Create(
            agentLoop: new AgentLoopRuntimeSettings
        {
            Enabled = true,
            MaxSteps = maxSteps,
            MaxToolCalls = maxToolCalls,
            MaxRuntimeSeconds = maxRuntimeSeconds,
            RequireExplicitAllowlist = true,
            AllowedCapabilities = ["fake.observe", "fake.validate"],
            WriteTraceFiles = false,
            TraceEveryStep = false,
            TraceRootPath = Path.Combine("workspace", "artifacts", "agent-loops")
        });
    }

    private static AgentPlannedStep CreatePlannedStep(string capability)
    {
        return new AgentPlannedStep
        {
            Phase = AgentStepPhase.Observe,
            Capability = capability,
            ToolName = capability,
            RiskLevel = ToolRiskLevel.Low,
            InputSummary = $"planned {capability}"
        };
    }

    private sealed class SequencePlanner : IAgentStepPlanner
    {
        private readonly object _syncRoot = new();
        private readonly Queue<AgentPlannedStep> _steps;

        public SequencePlanner(params AgentPlannedStep[] steps)
        {
            _steps = new Queue<AgentPlannedStep>(steps);
        }

        public ValueTask<Fin<AgentPlannedStep>> PlanNextStepAsync(
            AgentLoopExecutionContext context,
            CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                if (_steps.Count == 0)
                {
                    return new ValueTask<Fin<AgentPlannedStep>>(Fin<AgentPlannedStep>.Succ(CreatePlannedStep("fake.observe")));
                }

                return new ValueTask<Fin<AgentPlannedStep>>(Fin<AgentPlannedStep>.Succ(_steps.Dequeue()));
            }
        }
    }

    private sealed class RecordingToolExecutor : IAgentToolExecutor
    {
        private readonly object _syncRoot = new();
        private readonly List<AgentPlannedStep> _executedSteps = [];

        public IReadOnlyList<AgentPlannedStep> ExecutedSteps
        {
            get
            {
                lock (_syncRoot)
                {
                    return _executedSteps.ToArray();
                }
            }
        }

        public AgentToolExecutionResult Result { get; set; } = new()
        {
            Status = ToolExecutionStatus.Succeeded,
            ExitCode = 0,
            OutputSummary = "ok",
            ElapsedMilliseconds = 1
        };

        public ValueTask<Fin<AgentToolExecutionResult>> ExecuteAsync(
            AgentToolExecutionRequest request,
            CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                _executedSteps.Add(request.PlannedStep);
            }

            return new ValueTask<Fin<AgentToolExecutionResult>>(Fin<AgentToolExecutionResult>.Succ(Result));
        }
    }

    private sealed class ContinueInspector : IAgentResultInspector
    {
        public ValueTask<Fin<AgentResultInspection>> InspectAsync(
            AgentLoopExecutionContext context,
            AgentPlannedStep plannedStep,
            AgentToolExecutionResult executionResult,
            CancellationToken cancellationToken)
        {
            return new ValueTask<Fin<AgentResultInspection>>(Fin<AgentResultInspection>.Succ(new AgentResultInspection
            {
                Decision = AgentDecisionType.Continue,
                OutputSummary = executionResult.OutputSummary
            }));
        }
    }

    private sealed class StopSucceededInspector : IAgentResultInspector
    {
        private readonly string _finalResult;

        public StopSucceededInspector(string finalResult)
        {
            _finalResult = finalResult;
        }

        public ValueTask<Fin<AgentResultInspection>> InspectAsync(
            AgentLoopExecutionContext context,
            AgentPlannedStep plannedStep,
            AgentToolExecutionResult executionResult,
            CancellationToken cancellationToken)
        {
            return new ValueTask<Fin<AgentResultInspection>>(Fin<AgentResultInspection>.Succ(new AgentResultInspection
            {
                Decision = AgentDecisionType.StopSucceeded,
                OutputSummary = executionResult.OutputSummary,
                FinalResult = _finalResult
            }));
        }
    }

    private sealed class ContinueValidator : IAgentLoopValidator
    {
        public ValueTask<Fin<AgentLoopValidation>> ValidateAsync(
            AgentLoopExecutionContext context,
            CancellationToken cancellationToken)
        {
            return new ValueTask<Fin<AgentLoopValidation>>(Fin<AgentLoopValidation>.Succ(new AgentLoopValidation
            {
                Decision = AgentDecisionType.Continue
            }));
        }
    }

    private sealed class StopSucceededValidator : IAgentLoopValidator
    {
        private readonly string _message;

        public StopSucceededValidator(string message)
        {
            _message = message;
        }

        public ValueTask<Fin<AgentLoopValidation>> ValidateAsync(
            AgentLoopExecutionContext context,
            CancellationToken cancellationToken)
        {
            return new ValueTask<Fin<AgentLoopValidation>>(Fin<AgentLoopValidation>.Succ(new AgentLoopValidation
            {
                Decision = AgentDecisionType.StopSucceeded,
                Message = _message
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

            return new ValueTask<Fin<Unit>>(Fin<Unit>.Succ(Unit.Default));
        }

        public ValueTask<Fin<Unit>> WriteRunAsync(
            AgentLoopRun run,
            CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                _runs.Add(run);
            }

            return new ValueTask<Fin<Unit>>(Fin<Unit>.Succ(Unit.Default));
        }
    }

    private sealed class DenyAllPolicy : IToolExecutionPolicy
    {
        private readonly string _reason;

        public DenyAllPolicy(string reason)
        {
            _reason = reason;
        }

        public ValueTask<Fin<ToolExecutionPolicyDecision>> EvaluateAsync(
            AgentLoopExecutionContext context,
            AgentPlannedStep plannedStep,
            CancellationToken cancellationToken)
        {
            return new ValueTask<Fin<ToolExecutionPolicyDecision>>(Fin<ToolExecutionPolicyDecision>.Succ(new ToolExecutionPolicyDecision
            {
                Allowed = false,
                Decision = "denied",
                Reason = _reason
            }));
        }
    }

    private sealed class AllowAllPolicy : IToolExecutionPolicy
    {
        public static AllowAllPolicy Instance { get; } = new();

        public ValueTask<Fin<ToolExecutionPolicyDecision>> EvaluateAsync(
            AgentLoopExecutionContext context,
            AgentPlannedStep plannedStep,
            CancellationToken cancellationToken)
        {
            return new ValueTask<Fin<ToolExecutionPolicyDecision>>(Fin<ToolExecutionPolicyDecision>.Succ(new ToolExecutionPolicyDecision
            {
                Allowed = true,
                Decision = "allowed"
            }));
        }
    }
}
