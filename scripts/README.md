# Scripts

This folder contains the supported repo-local operational scripts for both hosts.

## Core repo workflows

| Script | Purpose |
| --- | --- |
| `Install-LocalMcpClients.ps1` | Generates repo-local MCP client config files under ignored local config paths |
| `install-lmstudio-mcp.ps1` | Installs or updates LM Studio MCP registration |
| `install-vscode-mcp.ps1` | Installs or updates user-level VS Code MCP registration |
| `Test-StdioFramedMcp.ps1` | Exercises the real stdio host with framed JSON-RPC requests |
| `Invoke-InferenceToolSmokeTest.ps1` | Runs MCP tool scenarios through an OpenAI-compatible inference endpoint |
| `Test-LmStudioWorkspaceAccess.ps1` | Verifies workspace-aware MCP operations against a temporary scenario |
| `Invoke-LmStudioStructuredOutputProbe.ps1` | Probes structured-output support on a compatible endpoint |
| `Invoke-LmStudioGpuWorkout.ps1` | Sends heavier local inference traffic for workstation stress testing |
| `Watch-ServerLogs.ps1` | Tails the latest stdio host log file |
| `Stop-RunningMcpServerHost.ps1` | Stops repo-local `McpServer.Host.exe` processes before rebuilds |

## AgentRouter workflows

| Script | Purpose |
| --- | --- |
| `Start-AgentRouterStack.ps1` | Starts the preferred local AgentRouter stack, including runtime prerequisites |
| `Stop-AgentRouterStack.ps1` | Stops the local AgentRouter stack |
| `Test-AgentRouter.ps1` | Runs the basic AgentRouter HTTP smoke checks, including streaming chat completion coverage |
| `Stress-AgentRouter.ps1` | Runs bounded AgentRouter concurrency/stress validation |

`Start-AgentRouterStack.ps1` writes AgentRouter stdout/stderr to `.run/logs/` when launched in `-NoNewWindows` mode and prints the tail of those logs if the `/health` readiness check times out. That makes startup failures much easier to diagnose without changing the normal startup path.

`Invoke-InferenceToolSmokeTest.ps1` and `Test-StdioFramedMcp.ps1` both pin the repository workspace root explicitly. Run them from the repo root so the workspace and allowed-root settings line up with the configured harness instead of the shell current directory.

When you pass `-RunSmoke` to `Start-AgentRouterStack.ps1`, it now uses the typed .NET harness in `tools/McpServer.AgentRouter.Tools`, widens the AgentRouter tool allowlist only for that smoke process, and runs the loopback web scrape coverage alongside the full default MCP tool suite.

Pass `-EnableSshSmoke` to include SSH coverage in that same typed harness. Set the repo-local SSH password environment variable in your shell before launching the stack for a real host run. The script now fails fast if the SSH profile's required secret is not present in the launching shell.

The typed .NET harness in `tools/McpServer.AgentRouter.Tools` is the preferred higher-fidelity smoke/stress path when you want structured reports instead of PowerShell-only output.

## Optional machine-local helpers

These scripts intentionally mutate user-profile state outside the repo:

- `Disable-LmStudioSandbox.ps1`
- `Enable-LmStudioAgentMode.ps1`
- `Test-LmStudioAgentMode.ps1`
- `add-github-packages-source.ps1`

Use them deliberately on shared machines.

## Supporting data

- `inference-tool-scenarios.json` feeds `Invoke-InferenceToolSmokeTest.ps1`

## Notes

- Repo-facing docs should treat `.codex/`, `.vscode/`, and `.mcp.json` as generated local config, not checked-in source.
- Runtime outputs belong under ignored paths such as `.run/`, `logs/`, `artifacts/`, and `workspace/artifacts/`.
