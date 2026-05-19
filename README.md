# MCPServer

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![C%23 14](https://img.shields.io/badge/C%23-14-239120?logo=csharp)](https://learn.microsoft.com/dotnet/csharp/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

`MCPServer` is a .NET 10 local automation repo with two production surfaces:

- `McpServer.Host`: a stdio Model Context Protocol server for workspace-aware tools, resources, prompts, and local inference helpers.
- `McpServer.AgentRouter.Host`: a loopback HTTP host with OpenAI-compatible chat completions plus bounded agent, MCP, shell, and SSH execution workflows.

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
- `web.scrape_url`
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
| `tools/McpServer.SshVaultCli` | SSH vault add/delete/list CLI |

## Safety defaults

- `McpServer.Host` keeps shell, SSH, web, and Ollama disabled by default in `src/McpServer.Host/appsettings.json`.
- `McpServer.AgentRouter.Host` is loopback-only by default and disallows cloud providers unless explicitly enabled.
- AgentRouter web search uses a configurable base URL, and loopback/private web targets can be explicitly allowed for controlled local harnesses.
- AgentRouter SSH can swap to a deterministic test backend when explicitly enabled for integration coverage.
- AgentRouter MCP tool execution is allowlist-based by default.
- AgentRouter shell execution is allowlist-based, bounded by a working-directory root, and blocks inline shell command switches by default.
- AgentRouter SSH execution is named-profile based and keeps raw credentials out of request bodies.
- `McpServer.Host` SSH profiles load from `config/mcpserver/ssh-profiles.local.json` by default, with user-local overrides in `%LOCALAPPDATA%\McpServer\ssh-profiles.json`.
- SSH vault items are managed with `tools/McpServer.SshVaultCli`; profiles can point at a named vault item through `passwordVaultItemName` instead of embedding a secret or reading from an environment variable. See [SSH Vault](docs/ssh-vault.md).
- Both hosts write operational traces/logs to repo-local runtime paths instead of mixing them into source directories.

## Preferred validation

```text
dotnet run --project .\tools\McpServer.AgentRouter.Tools -- verify
```

That typed C# harness restores, builds, and runs the full repo test suite without relying on shell-script orchestration. It now streams the child `dotnet` output into your terminal instead of hiding the process.

## CLI quick reference

| Command | Purpose | When to use |
| --- | --- | --- |
| `dotnet run --project .\tools\McpServer.AgentRouter.Tools -- verify` | Restore, build, and test the repo in the supported C# CLI path. | Use for the default repo validation pass. |
| `dotnet run --project .\tools\McpServer.AgentRouter.Tools -- chat --prompt "Say hello"` | Open a chat console against the local router or send a one-shot prompt. | Use when you want to see the raw model response from Ollama or LM Studio through AgentRouter. |
| `dotnet run --project .\tools\McpServer.AgentRouter.Tools -- chat --prompt-file .\prompts\hello.txt --transcript .\transcripts\hello.json` | Run chat from a file and save a durable JSON transcript. | Use when you want repeatable prompts or a record for extension/debug workflows. |
| `dotnet run --project .\tools\McpServer.AgentRouter.Tools -- smoke` | Run the higher-fidelity AgentRouter runtime harness. | Use when you want end-to-end runtime validation beyond build/test. |
| `dotnet run --project .\tools\McpServer.AgentRouter.Tools -- stress` | Run the AgentRouter stress harness. | Use for repeatable workload and response-shape checks. |
| `dotnet run --project .\tools\McpServer.AgentRouter.Tools -- provider-unavailable` | Probe the provider-failure path. | Use when validating fallback and error handling. |
| `dotnet run --project .\tools\McpServer.SshVaultCli -- help` | Show SSH vault CLI usage. | Use when adding, listing, verifying, or deleting vault items. |
| `dotnet run --project .\tools\McpServer.SshVaultCli -- add dev --secret-file .\secrets\dev-password.txt --description "Standard dev credential"` | Add a vault item from a local secret file. | Use when you already have the secret in a file and want a repeatable import. |
| `dotnet run --project .\tools\McpServer.SshVaultCli -- verify dev` | Verify that a vault item decrypts correctly. | Use before wiring the secret into an SSH profile. |
| `dotnet run --project .\tools\McpServer.SshVaultCli -- profile upsert root --host 173.255.205.169 --username root --password-vault-item root --working-directory /root --allow-sudo-command true --allow-all-commands true` | Create or update an SSH profile that links a user to a vault item. | Use when you want the CLI to manage the username, host, and credential relationship in one place. |
| `dotnet run --project .\tools\McpServer.SshVaultCli -- profile link root --password-vault-item root` | Update only the credential reference on an existing profile. | Use when you rotate secrets without changing host or username settings. |
| `dotnet run --project .\tools\McpServer.SshVaultCli -- profile list` | List SSH profiles with their credential references. | Use when you want to confirm which user maps to which credential. |
| `dotnet run --project .\tools\McpServer.SshVaultCli -- profile unlink root` | Clear the credential reference on an existing profile. | Use when you want to detach a secret before re-linking or rotation. |
| `dotnet run --project .\tools\McpServer.SshVaultCli -- profile show root` | Show the full SSH profile as JSON. | Use when you want to inspect the exact host, username, and credential mapping. |
| `dotnet run --project .\tools\McpServer.SshVaultCli -- delete dev` | Remove a vault item. | Use when rotating or cleaning up credentials. |
| `cmd.exe /c "set MCPSERVER_INTEGRATION_LIVE_SSH=1&& dotnet test .\tests\McpServer.IntegrationTests\McpServer.IntegrationTests.csproj -c Release --no-build --filter FullyQualifiedName~Ssh -v minimal"` | Run the live SSH integration slice against the repo-local profile and vault. | Use when you need to prove real host login and command parsing. |

`verify` streams child `dotnet` output into your terminal. When SSH execution is enabled, `smoke` also echoes SSH stdout/stderr blocks back to the terminal for each request.

Add `--output json` to any `tools/McpServer.AgentRouter.Tools` or `tools/McpServer.SshVaultCli` command when you need machine-readable output for an editor extension or automation script.
For chat, `--output json` returns a structured single-turn payload that is easy to consume from a VS Code extension.
For longer prompts, `--prompt-file` avoids shell quoting issues, and `--transcript` writes a JSON transcript to disk.
Human chat output is framed into a session card plus prompt and assistant panels, and markdown/code fences are rendered into a cleaner transcript instead of raw source text. Emojis are normalized to `0` in the console transcript. Live redraw only kicks in on ANSI-capable terminals; otherwise the final formatted panel is emitted safely.

## Build and test baseline

If you need to mirror CI directly, run the raw .NET commands below:

```text
dotnet restore .\McpServer.slnx
dotnet build .\McpServer.slnx -c Release --no-restore -v minimal
dotnet test .\tests\McpServer.UnitTests\McpServer.UnitTests.csproj -c Release --no-build -v minimal
dotnet test .\tests\McpServer.IntegrationTests\McpServer.IntegrationTests.csproj -c Release --no-build -v minimal
dotnet test .\tests\McpServer.AgentRouter.UnitTests\McpServer.AgentRouter.UnitTests.csproj -c Release --no-build -v minimal
```

Deterministic integration coverage lives in `tests/McpServer.IntegrationTests`; live-provider and stress harnesses live in `tools/`.

## Run locally

Run the stdio host:

```text
dotnet run --project .\src\McpServer.Host\McpServer.Host.csproj -c Release
```

Run the AgentRouter host directly:

```text
dotnet run --project .\src\McpServer.AgentRouter.Host\McpServer.AgentRouter.Host.csproj -c Release
```

Run the preferred AgentRouter runtime harness:

```text
dotnet run --project .\tools\McpServer.AgentRouter.Tools -- smoke
```

Use `dotnet run --project .\tools\McpServer.AgentRouter.Tools -- verify` for the full repo build/test validation path. Use `smoke` when you want the higher-fidelity AgentRouter runtime harness. The supported CLI path is C#-only, and the quick reference above is the copy/paste matrix.

For the full vault workflow, file layout, and privileged profile guidance, see [SSH Vault](docs/ssh-vault.md).

## Documentation

- [AgentRouter](docs/agent-router.md)
- [Architecture](docs/architecture.md)
- [Method Summary](docs/method-summary.md)
- [Workspace Configuration](docs/workspace-configuration.md)
- [SSH Vault](docs/ssh-vault.md)
- [Ollama Local Inference Tools](docs/ollama-local-inference-tools.md)
- [Codex / VS Code MCP Setup](docs/codex-vscode-mcp-setup.md)
- [Changelog](CHANGELOG.md)

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE).
