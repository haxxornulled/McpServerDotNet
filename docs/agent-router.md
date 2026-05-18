# AgentRouter

`McpServer.AgentRouter.Host` is the repo's local HTTP orchestration surface. It complements `McpServer.Host`; it does not replace it.

`McpServer.Host` remains the stdio MCP server.

`McpServer.AgentRouter.Domain` owns the runtime request, run, inference, MCP, shell, SSH, and loop state. The host owns the wire DTOs and maps them at the HTTP boundary.

`McpServer.AgentRouter.Host` provides:

- OpenAI-compatible chat completions against local model profiles
- durable agent-run storage
- stdio MCP tool catalog discovery and tool execution
- bounded shell execution
- bounded SSH execution
- bounded autonomous loop execution

## Default bind

```text
http://127.0.0.1:5177
```

OpenAI-compatible base URL:

```text
http://127.0.0.1:5177/v1
```

## Endpoints

| Endpoint | Purpose |
| --- | --- |
| `GET /health` | Liveness probe |
| `GET /v1/models` | Lists configured model profiles |
| `POST /v1/chat/completions` | OpenAI-style chat completions, including `stream=true` SSE responses |
| `GET /agent/mcp/tools` | Lists the stdio MCP tool catalog |
| `POST /agent/mcp/tools/call` | Executes one allowlisted MCP tool call |
| `POST /agent/shell/exec` | Executes one approved local command |
| `POST /agent/ssh/exec` | Executes one approved SSH command against a named profile |
| `POST /agent/loops` | Runs a bounded autonomous loop |
| `POST /agent/runs` | Creates and persists an agent run |
| `GET /agent/runs/{id}` | Reads persisted agent-run state |

Streaming chat completions are implemented for `stream=true` requests.
The stream uses minimal OpenAI-style SSE chunks and does not include usage totals.
If the upstream provider fails before the first chunk is emitted, the host returns a normal JSON error response; once streaming has started, failures are emitted as SSE `event: error`.

## Default model profiles

| Profile | Provider | Model | Context | Timeout |
| --- | --- | --- | --- | --- |
| `local-code` | Ollama | `qwen3-coder:30b` | 131072 | 900s |
| `fast-local` | Ollama | `qwen2.5-coder:14b` | 32768 | 600s |
| `local-agent` | Ollama | `qwen3-coder:30b` | 131072 | 1200s |

Cloud providers are disabled by default. Non-loopback model base URLs are rejected unless explicitly allowed on a profile.
Web search uses a configurable base URL, and local loopback/private targets can be explicitly enabled for controlled test harnesses.
SSH can swap to a deterministic test backend when integration coverage needs to avoid a live target.

## Startup lifecycle

When `AgentRouter:Startup:Enabled` is true, the host can:

- verify the durable run-storage root
- verify the loop trace root
- verify the MCP tool-call trace root
- verify the shell execution trace root
- verify the shell execution working-directory root
- verify Ollama readiness
- start Ollama if configured and missing
- verify required models
- verify stdio MCP tool catalog readiness

This is handled by `AgentRouterStartupLifecycleService`.

## Runtime storage

Default runtime roots:

```text
workspace/artifacts/agent-runs
workspace/artifacts/agent-loops
workspace/artifacts/mcp-tool-calls
workspace/artifacts/shell-exec
workspace/artifacts/ssh-exec
workspace/artifacts/stress-runs
```

Per-run files include request/response/artifact and trace output. MCP tool calls, shell execution, SSH execution, and loop steps also write durable trace records.

## MCP stdio boundary

AgentRouter uses `McpServer.Host` as its MCP execution boundary.

Flow:

```text
AgentRouter
  -> stdio child process: McpServer.Host
  -> initialize
  -> notifications/initialized
  -> tools/list or tools/call
  -> shutdown + exit
```

By default, the router launches the child host with high-risk stdio features disabled for the MCP bridge:

```text
MCPSERVER__SHELL__ENABLED=false
MCPSERVER__WEBACCESS__ENABLED=false
MCPSERVER__SSH__ENABLED=false
```

Default MCP tool allowlist:

- `activity.schemas.list`
- `fs.get_metadata`
- `fs.list_directory`

## Shell execution

`POST /agent/shell/exec` is policy-gated before any process starts.

Default rules:

- command must be an executable name, not a path
- explicit allowlist is enabled
- denied commands are blocked
- working directory must stay under `AgentRouter:ShellExecution:WorkingDirectoryRoot`
- inline shell switches such as `pwsh -Command` and `bash -c` are denied by default
- output and timeout are bounded

Default allowed commands:

- `dotnet`
- `git`
- `pwsh`
- `bash`

## SSH execution

`POST /agent/ssh/exec` is named-profile based.

Default rules:

- requests reference a profile name instead of raw host credentials
- repo-local and user-local profile files are layered
- environment variables hold passwords or passphrases
- unknown host keys are blocked by default
- inline shell switches are denied by default
- output and timeout are bounded

Profile loading order:

1. `config/agentrouter/ssh-profiles.local.json`
2. `%LOCALAPPDATA%/McpServer/AgentRouter/ssh-profiles.json`

The repo ignores `config/agentrouter/*.local.json`. Keep templates or examples checked in, not real secrets.

## Autonomous loop

`POST /agent/loops` runs the bounded autonomous loop coordinator.

The loop coordinates:

- planner
- execution policy
- executor
- result inspector
- validator
- trace writer

Default allowed loop capabilities:

- `mcp.tools.call`
- `shell.exec`
- `ssh.exec`

The loop stays bounded by:

- max step limit
- max runtime limit
- max output limit
- explicit allowlists
- durable step traces

## Local run and validation

Preferred local stack start:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Start-AgentRouterStack.ps1
```

Smoke test:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-AgentRouter.ps1
```

Stress test:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Stress-AgentRouter.ps1
```

Typed harness:

```powershell
dotnet run --project .\tools\McpServer.AgentRouter.Tools -- smoke
dotnet run --project .\tools\McpServer.AgentRouter.Tools -- stress
dotnet run --project .\tools\McpServer.AgentRouter.Tools -- provider-unavailable
```

## Manual examples

Health:

```powershell
Invoke-RestMethod http://127.0.0.1:5177/health
```

Chat:

```powershell
$body = @{
  model = "fast-local"
  messages = @(
    @{ role = "user"; content = "Say router online in one sentence." }
  )
  stream = $false
} | ConvertTo-Json -Depth 10

Invoke-RestMethod `
  -Uri "http://127.0.0.1:5177/v1/chat/completions" `
  -Method Post `
  -ContentType "application/json" `
  -Body $body
```

MCP tool call:

```powershell
$body = @{
  toolName = "fs.list_directory"
  arguments = @{
    path = "."
  }
} | ConvertTo-Json -Depth 10

Invoke-RestMethod `
  -Uri "http://127.0.0.1:5177/agent/mcp/tools/call" `
  -Method Post `
  -ContentType "application/json" `
  -Body $body
```

Shell execution:

```powershell
$body = @{
  command = "dotnet"
  arguments = @("--info")
  working_directory = "."
} | ConvertTo-Json -Depth 10

Invoke-RestMethod `
  -Uri "http://127.0.0.1:5177/agent/shell/exec" `
  -Method Post `
  -ContentType "application/json" `
  -Body $body
```
