# Changelog

All notable changes to this project will be documented in this file.

## Unreleased

### Changed

- Rewrote the top-level README to describe the actual repo shape: the stdio MCP host, the AgentRouter HTTP host, the current tool surfaces, the local harnesses, and the real safety defaults.
- Rewrote `docs/architecture.md`, `docs/method-summary.md`, and `docs/agent-router.md` so they match the current dual-host architecture instead of older milestone-only or stdio-only descriptions.
- Aligned the repo docs and AgentRouter smoke script with the current codebase, including the separate AgentRouter domain layer, explicit host-base workspace resolution, and `stream=true` SSE support.
- Rewrote `scripts/README.md` to document the current supported script inventory, including the AgentRouter stack, smoke, and stress scripts.
- Rewrote `docs/codex-vscode-mcp-setup.md` and `docs/ollama-local-inference-tools.md` to stop claiming machine-local config files are checked into the repo and to align the setup guidance with the current config-generation flow.
- Removed the stale root-level `README-AgentRouterStackScripts.md` in favor of the consolidated `docs/agent-router.md` and `scripts/README.md`.
- Cleaned repo-local temporary runtime clutter and ignored artifact leftovers from the working tree.

### Validation

- `dotnet restore .\McpServer.slnx`
- `dotnet build .\McpServer.slnx -c Release --no-restore -v minimal`

## 0.1.6 - 2026-04-16

### Added

- Added GitHub Packages publishing for the `McpServer.Host` .NET tool package on version tag builds in CI.

### Changed

- Added package metadata and README guidance for installing `McpServer.Host` as a `dotnet tool` from the repository's GitHub Packages feed.

### Validation

- `dotnet build .\McpServer.slnx -c Release --no-restore -v minimal`
- `dotnet test .\tests\McpServer.UnitTests\McpServer.UnitTests.csproj -c Release --no-build -v minimal`
- `dotnet test .\tests\McpServer.IntegrationTests\McpServer.IntegrationTests.csproj -c Release --no-build -v minimal`
- `dotnet pack .\src\McpServer.Host\McpServer.Host.csproj -c Release --no-build -p:PackageVersion=0.1.6 -o .\artifacts\nuget`

## 0.1.5 - 2026-04-16

### Added

- Added `scripts/Invoke-InferenceToolSmokeTest.ps1` to validate registered MCP tools through an OpenAI-compatible inference endpoint such as LM Studio.
- Added ordered default inference scenarios in `scripts/inference-tool-scenarios.json` for the core filesystem and `shell.exec` tool set.

### Changed

- Hardened the inference smoke-test harness with dependency-aware scenario skips, immediate per-tool pass/fail output, and clearer failure details for malformed model arguments or MCP error payloads.

### Validation

- `dotnet restore .\McpServer.slnx`
- `dotnet build .\McpServer.slnx -c Release --no-restore -v minimal`
- `dotnet test .\tests\McpServer.UnitTests\McpServer.UnitTests.csproj -c Release --no-build -v minimal`
- `dotnet test .\tests\McpServer.IntegrationTests\McpServer.IntegrationTests.csproj -c Release --no-build -v minimal`
- `pwsh -File .\scripts\Invoke-InferenceToolSmokeTest.ps1 -Model google/gemma-4-26b-a4b -InferenceTimeoutSeconds 180 -ResultPath .\inference-smoke-results.json`

## 0.1.4 - 2026-04-16

### Changed

- Updated the LM Studio setup guidance to launch the built `McpServer.Host.exe` binary directly so local MCP registration uses the latest rebuilt executable.
- Removed the stale README note claiming the repository could not be compile-verified in its original generation environment.
- Added explicit extension-point guidance to repo-level agent instruction files so future changes follow the `IToolHandler<TRequest>`, `IResourceHandler`, `IPromptHandler`, Autofac, and application-abstraction seams already documented in the architecture guide.
- Updated GitHub repository topics to improve discoverability for MCP, LM Studio, GitHub Copilot, JSON-RPC, and SSH automation use cases.

### Validation

- `gh run view 24493206070 --json status,conclusion,url,jobs,displayTitle,headSha,updatedAt`

## 0.1.3 - 2026-04-15

### Added

- Added optional SSH profile configuration with host, port, username, environment-variable-based credentials, host key pinning, and default working directory support.
- Added `ssh.exec` for remote non-interactive shell command execution on configured SSH hosts.
- Added `ssh.write_text` for writing remote configuration files over SFTP with optional parent-directory creation and octal permissions.
- Added focused unit coverage for SSH tool handlers and SSH profile validation behavior.

### Changed

- Updated dependency injection and tool routing so SSH tools are only exposed when SSH profiles are enabled and configured.
- Updated README and architecture/method documentation to describe the new remote automation path and production-safe SSH configuration patterns.

### Validation

- `dotnet build .\src\McpServer.Host\McpServer.Host.csproj -c Debug`
- `dotnet test .\tests\McpServer.UnitTests\McpServer.UnitTests.csproj -c Debug --filter "FullyQualifiedName~Ssh"`

## 0.1.2 - 2026-04-15

### Added

- Added the `shell.exec` MCP tool for non-interactive command execution inside the configured workspace, including structured exit code, stdout, stderr, timeout, and truncation details.
- Added unit and integration coverage for `shell.exec`, `ping`, JSON-RPC success response shape, protocol negotiation fallback, and LM Studio virtual workspace aliases.
- Added README guidance for registering the server in LM Studio and invoking the new command tool.

### Changed

- Updated MCP protocol negotiation to preserve supported versions and fall back to `2025-03-26` for unknown client versions so current LM Studio builds can connect successfully.
- Added `ping` handling in the stdio host for MCP clients that probe server health before using tools.
- Switched host content-root and workspace resolution to `AppContext.BaseDirectory` so the server still works when launched from unrelated working directories.
- Omitted null JSON-RPC response fields on successful calls to keep the transport compliant with stricter MCP hosts.
- Expanded workspace path handling to treat both `/workspace/...` and LM Studio's `/mcpserver-filesystem/...` alias as the same virtual root.

### Validation

- `dotnet build .\src\McpServer.Host\McpServer.Host.csproj -c Debug`
- `dotnet test .\tests\McpServer.UnitTests\McpServer.UnitTests.csproj -c Debug --filter "FullyQualifiedName~PathPolicyTests|FullyQualifiedName~InitializeHandlerTests|FullyQualifiedName~StdioMessageTransportTests|FullyQualifiedName~ShellExecToolHandlerTests"`
- `dotnet test .\tests\McpServer.IntegrationTests\McpServer.IntegrationTests.csproj -c Debug --filter "FullyQualifiedName~StdioLifecycleIntegrationTests"`

## 0.1.1 - 2026-04-15

### Fixed

- Aligned filesystem tool path handling with the configured workspace root so relative tool paths resolve correctly.
- Fixed resource URI translation for `file:///workspace/...` and `dir:///workspace` so MCP resource reads map into the host workspace.
- Registered path policy and resource URI translation from the same resolved workspace root to eliminate runtime path mismatches.

### Added

- Unit tests covering workspace-relative path normalization and resource URI translation.
- Integration coverage for an end-to-end `fs.write_text` plus `resources/read` round-trip.

### Validation

- `dotnet build .\McpServer.slnx -v minimal`
- `dotnet test .\tests\McpServer.UnitTests\McpServer.UnitTests.csproj -v minimal --no-build`
- `dotnet test .\tests\McpServer.IntegrationTests\McpServer.IntegrationTests.csproj -v minimal`

## 0.1.0 - 2026-04-15

### Added

- Cleanly layered solution structure across Host, Protocol, Application, Infrastructure, Contracts, and Domain projects.
- STDIO-based MCP host with JSON-RPC request/response transport.
- MCP lifecycle handling for initialize, initialized, shutdown, and exit flows.
- Filesystem MCP tools for write, append, create directory, move, copy, and delete operations.
- Filesystem MCP resources for file text, directory listing, and file metadata reads.
- Prompt support for summarizing files and reviewing directories.
- Optional web access services and MCP tools for URL fetch and web search.
- Autofac-based dependency injection and Serilog-based logging bootstrap.
- Unit and integration test coverage for transport, routing, lifecycle, and host startup flows.
- Architecture documentation in `docs/architecture.md`.
- Public API and method inventory in `docs/method-summary.md`.

### Changed

- Corrected shared MSBuild configuration so the solution loads, restores, and builds correctly under the installed .NET 10 SDK.
- Fixed multiple protocol, application, infrastructure, host, and test compilation issues across the initial scaffold.
- Replaced `FluentAssertions` with xUnit assertions to keep the test stack fully open source.

### Validation

- `dotnet build .\McpServer.slnx -v minimal`
- `dotnet test .\tests\McpServer.UnitTests\McpServer.UnitTests.csproj -v minimal`
- `dotnet test .\tests\McpServer.IntegrationTests\McpServer.IntegrationTests.csproj -v minimal`
