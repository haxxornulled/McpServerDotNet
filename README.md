# MCPServer

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![C%23 14](https://img.shields.io/badge/C%23-14-239120?logo=csharp)](https://learn.microsoft.com/dotnet/csharp/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

`MCPServer` is a .NET 10 local automation repo with two production surfaces:

- `McpServer.Host`: a stdio Model Context Protocol server for workspace-aware tools, resources, prompts, and local inference helpers.
- `McpServer.AgentRouter.Host`: a loopback HTTP host with OpenAI-compatible chat completions plus bounded agent, MCP, shell, and SSH execution workflows.

This repo is not a scaffold. The codebase already contains the working hosts, policy gates, trace storage, test coverage, and local harness scripts used to validate them.

## What ships

### `McpServer.Host`

Always-available surface:

- Filesystem tools: `fs.write_text`, `fs.append_text`, `fs.read_file`, `fs.read_text`, `fs.get_metadata`, `fs.list_directory`, `fs.create_directory`, `fs.move_path`, `fs.copy_path`, `fs.delete_path`
- Workspace tools: `workspace.set_root`, `workspace.open`, `workspace.select_folder`, `workspace.status`, `workspace.inspect`
- Activity tools: `activity.route`, `activity.schemas.list`, `activity.context.preview`, `activity.run`
- Resources: `file://`, `dir://`, `filemeta://`, `tree://`, `changes://`
- Prompts: `prompt.summarize_file`, `prompt.review_directory`

Optional surface, disabled unless explicitly configured:

- `shell.exec`
- `ssh.execute`
- `ssh.write_text`
- `web.fetch_url`
- `web.search`
- `inference.local_status`
- `inference.local_complete`
- `inference.local_summarize`
- `inference.local_code_review`
- `inference.local_plan`

### `McpServer.AgentRouter.Host`

Default bind:

```text
http://127.0.0.1:5177
```

Endpoints:

- `GET /health`
- `GET /v1/models`
- `POST /v1/chat/completions`
- `GET /agent/mcp/tools`
- `POST /agent/mcp/tools/call`
- `POST /agent/shell/exec`
- `POST /agent/ssh/exec`
- `POST /agent/loops`
- `POST /agent/runs`
- `GET /agent/runs/{id}`

Default model profiles:

- `local-code` -> `qwen3-coder:30b`
- `fast-local` -> `qwen2.5-coder:14b`
- `local-agent` -> `qwen3-coder:30b`

The router uses `McpServer.Host` as its controlled child-process tool boundary for MCP discovery and tool execution. Shell and SSH execution are separate policy-gated paths with durable trace output under `workspace/artifacts/`.

## Solution layout

| Path | Responsibility |
| --- | --- |
| `src/McpServer.Host` | stdio host bootstrap, configuration, Autofac registrations, transport loop |
| `src/McpServer.Protocol` | JSON-RPC and MCP lifecycle, routing, session state |
| `src/McpServer.Application` | MCP tool/resource/prompt/activity abstractions and handlers |
| `src/McpServer.Infrastructure` | filesystem, process, SSH, web, Ollama, logging implementations for the stdio host |
| `src/McpServer.Domain` | Workspace path resolution and mutation rules |
| `src/McpServer.AgentRouter.Host` | HTTP host, endpoint mapping, startup lifecycle |
| `src/McpServer.AgentRouter.Application` | model routing, MCP tool-call orchestration, shell/SSH execution, loop/run services |
| `src/McpServer.AgentRouter.Domain` | AgentRouter run, loop, inference, MCP, shell, and SSH domain models |
| `src/McpServer.AgentRouter.Infrastructure` | Ollama, stdio MCP bridge, shell and SSH runtime adapters |
| `src/McpServer.AgentRouter.Host/Protocol` and `src/McpServer.AgentRouter.Host/Configuration` | AgentRouter wire DTOs and host-owned configuration models |
| `tests/McpServer.UnitTests` | stdio host unit coverage |
| `tests/McpServer.IntegrationTests` | stdio host integration coverage |
| `tests/McpServer.AgentRouter.UnitTests` | AgentRouter unit coverage |
| `tools/McpServer.AgentRouter.Tools` | typed smoke/stress/provider-unavailable harness |

## Safety defaults

- `McpServer.Host` keeps shell, SSH, web, and Ollama disabled by default in `src/McpServer.Host/appsettings.json`.
- `McpServer.AgentRouter.Host` is loopback-only by default and disallows cloud providers unless explicitly enabled.
- AgentRouter web search uses a configurable base URL, and loopback/private web targets can be explicitly allowed for controlled local harnesses.
- AgentRouter SSH can swap to a deterministic test backend when explicitly enabled for integration coverage.
- AgentRouter MCP tool execution is allowlist-based by default.
- AgentRouter shell execution is allowlist-based, bounded by a working-directory root, and blocks inline shell command switches by default.
- AgentRouter SSH execution is named-profile based and keeps raw credentials out of request bodies.
- Both hosts write operational traces/logs to repo-local runtime paths instead of mixing them into source directories.

## Build and test baseline

```powershell
dotnet restore .\McpServer.slnx
dotnet build .\McpServer.slnx -c Release --no-restore -v minimal
dotnet test .\tests\McpServer.UnitTests\McpServer.UnitTests.csproj -c Release --no-build -v minimal
dotnet test .\tests\McpServer.IntegrationTests\McpServer.IntegrationTests.csproj -c Release --no-build -v minimal
dotnet test .\tests\McpServer.AgentRouter.UnitTests\McpServer.AgentRouter.UnitTests.csproj -c Release --no-build -v minimal
```

Deterministic integration coverage lives in `tests/McpServer.IntegrationTests`; live-provider or stress harnesses remain in `scripts/` and `tools/`.

## Run locally

Run the stdio host:

```powershell
dotnet run --project .\src\McpServer.Host\McpServer.Host.csproj -c Release
```

Run the AgentRouter host directly:

```powershell
dotnet run --project .\src\McpServer.AgentRouter.Host\McpServer.AgentRouter.Host.csproj -c Release
```

Run the preferred local stack script:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Start-AgentRouterStack.ps1
```

## Validation harnesses

Quick router smoke:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-AgentRouter.ps1
```

Router stress:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Stress-AgentRouter.ps1
```

Typed harness:

```powershell
dotnet run --project .\tools\McpServer.AgentRouter.Tools -- smoke
dotnet run --project .\tools\McpServer.AgentRouter.Tools -- stress
```

stdio MCP smoke:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-StdioFramedMcp.ps1
```

## Documentation

- [AgentRouter](docs/agent-router.md)
- [Architecture](docs/architecture.md)
- [Method Summary](docs/method-summary.md)
- [Workspace Configuration](docs/workspace-configuration.md)
- [Ollama Local Inference Tools](docs/ollama-local-inference-tools.md)
- [Codex / VS Code MCP Setup](docs/codex-vscode-mcp-setup.md)
- [Scripts](scripts/README.md)
- [Changelog](CHANGELOG.md)

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE).
