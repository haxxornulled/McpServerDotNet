# Method Summary

## Scope

This document summarizes the repo's production-facing entrypoints and the main service seams behind them. It is intentionally surface-oriented rather than a line-by-line dump of every method in `src/`.

## `McpServer.Host`

### Runtime entrypoints

| Path | Type | Role |
| --- | --- | --- |
| `src/McpServer.Host/Program.cs` | host bootstrap | Builds the stdio host from `AppContext.BaseDirectory`, configures Serilog, registers named `HttpClient` instances, and loads Autofac |
| `src/McpServer.Host/Transport/Stdio/StdioServerHostedService.cs` | hosted service | Owns the stdio request loop, lifecycle coordination, and JSON-RPC dispatch |
| `src/McpServer.Host/Transport/Stdio/StdioMessageTransport.cs` | transport | Reads request lines, writes responses and notifications, and supports server-initiated requests |
| `src/McpServer.Host/DependencyInjection/AutofacRootModule.cs` | DI module | Registers tools, resources, prompts, policies, optional features, and infrastructure services |

### Protocol dispatch

| Path | Role |
| --- | --- |
| `src/McpServer.Protocol/Lifecycle/InitializeHandler.cs` | Negotiates protocol version and advertises capabilities |
| `src/McpServer.Protocol/Lifecycle/ShutdownHandler.cs` | Marks shutdown requested |
| `src/McpServer.Protocol/Lifecycle/ExitHandler.cs` | Final exit-state handling |
| `src/McpServer.Protocol/Session/McpSession.cs` | Tracks initialization, readiness, shutdown, and roots support |
| `src/McpServer.Protocol/Routing/ToolCallRouter.cs` | Lists tools and dispatches `tools/call` |
| `src/McpServer.Protocol/Routing/ResourceReadRouter.cs` | Lists resources and dispatches `resources/read` |
| `src/McpServer.Protocol/Routing/PromptRouter.cs` | Lists prompts and dispatches `prompts/get` |

### Application seams

| Contract | Purpose |
| --- | --- |
| `IToolHandler<TRequest>` | Standard MCP tool extension seam |
| `IResourceHandler` | Standard MCP resource extension seam |
| `IPromptHandler` | Standard MCP prompt extension seam |
| `IFileSystemService` | Validated file operations inside allowed roots |
| `IPathPolicy` | Read/write path normalization and containment enforcement |
| `IResourcePathTranslator` | Maps MCP resource URIs to local paths |
| `IProcessExecutionService` | Runs validated non-interactive local processes |
| `IWebAccessService` | Optional outbound fetch/search execution |
| `ISshService` | Optional profile-based SSH execution and remote file writes |

### Public MCP surface

Always registered:

- Filesystem tools
- Workspace tools
- Activity tools
- `file://`, `dir://`, `filemeta://`, `tree://`, `changes://` resources
- `prompt.summarize_file`
- `prompt.review_directory`

Conditionally registered:

- `shell.exec`
- `web.fetch_url`
- `web.search`
- `ssh.execute`
- `ssh.write_text`
- `inference.local_*`

## `McpServer.AgentRouter.Host`

### Runtime entrypoints

| Path | Type | Role |
| --- | --- | --- |
| `src/McpServer.AgentRouter.Host/Program.cs` | host bootstrap | Builds the HTTP host from the repo content root when available, binds `AgentRouterOptions`, configures Serilog, and maps endpoints |
| `src/McpServer.AgentRouter.Host/Endpoints/AgentRouterEndpointExtensions.cs` | endpoint module | Maps the HTTP API, OpenAI-compatible wire DTOs, and converts application failures into HTTP/OpenAI-style error responses |
| `src/McpServer.AgentRouter.Host/Services/AgentRouterStartupLifecycleService.cs` | hosted service | Verifies run-storage roots, Ollama readiness, and MCP catalog readiness when startup orchestration is enabled |

### HTTP API surface

| Endpoint | Purpose |
| --- | --- |
| `GET /health` | Basic host liveness probe |
| `GET /v1/models` | Lists configured model profiles |
| `POST /v1/chat/completions` | OpenAI-style chat completion request routed to the configured local model client |
| `GET /agent/mcp/tools` | Discovers the current stdio MCP tool catalog |
| `POST /agent/mcp/tools/call` | Executes one allowlisted MCP tool call through the stdio bridge |
| `POST /agent/shell/exec` | Executes one policy-approved local command |
| `POST /agent/ssh/exec` | Executes one policy-approved SSH command against a named profile |
| `POST /agent/loops` | Runs the bounded autonomous loop workflow |
| `POST /agent/runs` | Creates and persists an agent run |
| `GET /agent/runs/{id}` | Reads a persisted agent run |

### Application seams

| Contract | Purpose |
| --- | --- |
| `IModelRouter` | Selects a model profile and routes one completion request |
| `IAgentRunService` | Creates and reads persisted run state |
| `IAutonomousLoopRunner` | Coordinates planner, policy, executor, inspector, validator, and trace writing |
| `IMcpToolCatalogClient` | Lists stdio MCP tools from `McpServer.Host` |
| `IMcpToolCallService` | Validates and executes one stdio MCP tool call |
| `IShellExecutionService` | Validates and executes one local shell request |
| `ISshExecutionService` | Validates and executes one SSH request |

### Core router adapters

| Path | Role |
| --- | --- |
| `src/McpServer.AgentRouter.Infrastructure/Ollama/OllamaChatModelClient.cs` | OpenAI-style completion bridge to Ollama |
| `src/McpServer.AgentRouter.Infrastructure/Mcp/StdioMcpToolCatalogClient.cs` | stdio MCP catalog discovery |
| `src/McpServer.AgentRouter.Infrastructure/Mcp/StdioMcpToolCallClient.cs` | stdio MCP tool execution |
| `src/McpServer.AgentRouter.Infrastructure/Shell/ProcessShellCommandExecutor.cs` | Local process execution for approved commands |
| `src/McpServer.AgentRouter.Infrastructure/Ssh/SshNetCommandExecutor.cs` | SSH.NET-based remote command execution |
| `src/McpServer.AgentRouter.Infrastructure/Ssh/FileSystemSshProfileStore.cs` | Layered repo-local and user-local SSH profile loading |

## `McpServer.Domain`

### Key domain types

| Path | Role |
| --- | --- |
| `src/McpServer.Domain/Workspace/WorkspacePathState.cs` | Owns workspace and project root state, path normalization, URI translation, and project-root change notifications |
| `src/McpServer.Domain/Workspace/WorkspaceBoundaryState.cs` | Captures the active workspace boundary and allowed roots as a domain snapshot |
| `src/McpServer.Domain/Workspace/WorkspaceMutationRules.cs` | Enforces protected-path mutation rules inside the workspace boundary |

## `McpServer.AgentRouter.Domain`

### Key domain types

| Path | Role |
| --- | --- |
| `src/McpServer.AgentRouter.Domain/AgentRuns/AgentRunState.cs` | Owns agent-run lifecycle, terminal transitions, and artifact creation |
| `src/McpServer.AgentRouter.Domain/AgentLoops/AgentLoopRunState.cs` | Owns bounded loop state and loop-run transitions |
| `src/McpServer.AgentRouter.Domain/AgentLoops/AgentLoopModels.cs` | Owns loop request and response shapes |
| `src/McpServer.AgentRouter.Domain/AgentLoops/AgentLoopExecutionModels.cs` | Owns loop execution step and trace shapes |
| `src/McpServer.AgentRouter.Domain/Inference/InferenceModels.cs` | Owns inference request, response, and status shapes |
| `src/McpServer.AgentRouter.Domain/Inference/ModelProfile.cs` | Owns model profile shape used by routing |
| `src/McpServer.AgentRouter.Domain/Mcp/McpToolCallModels.cs` | Owns MCP tool-call request, response, and trace shapes |
| `src/McpServer.AgentRouter.Domain/Shell/ShellExecutionModels.cs` | Owns shell execution request, response, and trace shapes |
| `src/McpServer.AgentRouter.Domain/Ssh/SshExecutionModels.cs` | Owns SSH execution request, response, and trace shapes |

## Durable runtime outputs

| Root | Purpose |
| --- | --- |
| `workspace/artifacts/agent-runs` | persisted agent-run requests, responses, artifacts, and traces |
| `workspace/artifacts/agent-loops` | autonomous loop traces |
| `workspace/artifacts/mcp-tool-calls` | MCP tool-call traces |
| `workspace/artifacts/shell-exec` | local shell-execution traces |
| `workspace/artifacts/ssh-exec` | SSH-execution traces |
| `workspace/artifacts/stress-runs` | smoke/stress harness reports |
