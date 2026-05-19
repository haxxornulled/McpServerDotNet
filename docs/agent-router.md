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
- inline shell switches such as `cmd /c` and `bash -lc` are denied by default
- output and timeout are bounded

Default allowed commands:

- `dotnet`
- `git`
- `dir`
- `bash`

## SSH execution

`POST /agent/ssh/exec` is named-profile based.

Default rules:

- requests reference a profile name instead of raw host credentials
- repo-local and user-local profile files are layered
- vault items hold passwords and passphrases
- unknown host keys are blocked by default
- inline shell switches are denied by default
- output and timeout are bounded
- `sudo` remains denied unless a profile explicitly sets `AllowSudoCommand=true`

Profile loading order:

1. `config/mcpserver/ssh-profiles.local.json`
2. `%LOCALAPPDATA%/McpServer/ssh-profiles.json`

The repo ignores `config/mcpserver/*.local.json`. Keep templates or examples checked in, not real secrets.

## CLI quick reference

| Command | Purpose | When to use |
| --- | --- | --- |
| `dotnet run --project .\tools\McpServer.AgentRouter.Tools -- verify` | Restore, build, and test the repo in the supported C# CLI path. | Use for the default repo validation pass. |
| `dotnet run --project .\tools\McpServer.AgentRouter.Tools -- smoke` | Run the higher-fidelity AgentRouter runtime harness. | Use when you want end-to-end runtime validation beyond build/test. |
| `dotnet run --project .\tools\McpServer.AgentRouter.Tools -- stress` | Run the AgentRouter stress harness. | Use for repeatable workload and response-shape checks. |
| `dotnet run --project .\tools\McpServer.AgentRouter.Tools -- provider-unavailable` | Probe the provider-failure path. | Use when validating fallback and error handling. |
| `cmd.exe /c "set MCPSERVER_INTEGRATION_LIVE_SSH=1&& dotnet test .\tests\McpServer.IntegrationTests\McpServer.IntegrationTests.csproj -c Release --no-build --filter FullyQualifiedName~Ssh -v minimal"` | Run the live SSH integration slice against the repo-local profile and vault. | Use when you need to prove real host login and command parsing. |

`verify` streams child `dotnet` output into your terminal. When SSH execution is enabled, `smoke` also echoes SSH stdout/stderr blocks back to the terminal for each request.

- For a real host, add the credential to the vault and point the repo-local SSH profile at `passwordVaultItemName`.
- For admin workflows, create a separate SSH profile with `AllowSudoCommand=true` instead of broadening the default profile.

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

Stack start:

```text
dotnet run --project .\src\McpServer.AgentRouter.Host\McpServer.AgentRouter.Host.csproj -c Release
```

Use the commands in the quick reference above for repo validation, smoke, stress, and live SSH checks. The `smoke` command includes the full default MCP tool coverage suite plus loopback web scrape coverage.

## Manual examples

Health:

```text
curl.exe http://127.0.0.1:5177/health
```

Chat:

```text
curl.exe -s http://127.0.0.1:5177/v1/chat/completions ^
  -H "Content-Type: application/json" ^
  -d "{\"model\":\"fast-local\",\"messages\":[{\"role\":\"user\",\"content\":\"Say router online in one sentence.\"}],\"stream\":false}"
```

MCP tool call:

```text
curl.exe -s http://127.0.0.1:5177/agent/mcp/tools/call ^
  -H "Content-Type: application/json" ^
  -d "{\"toolName\":\"fs.list_directory\",\"arguments\":{\"path\":\".\"}}"
```

Shell execution:

```text
curl.exe -s http://127.0.0.1:5177/agent/shell/exec ^
  -H "Content-Type: application/json" ^
  -d "{\"command\":\"dotnet\",\"arguments\":[\"--info\"],\"working_directory\":\".\"}"
```
