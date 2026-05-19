# Workspace Configuration

MCPServer always starts with a safe workspace root.

## Explicit workspace root

For normal Codex or VS Code MCP usage, set the workspace root with environment variables:

```text
MCPSERVER__WORKSPACE__ROOTPATH=D:\2026 Projects\McpServerRepo
MCPSERVER__WORKSPACE__ALLOWEDROOTS__0=D:\2026 Projects\McpServerRepo
```

This makes the repository itself visible to MCPServer tools.

## Visual Studio

When you debug `McpServer.Host` from Visual Studio, use the checked-in launch profile under `src/McpServer.Host/Properties/launchSettings.json`.

That profile already pins the workspace root, allowed roots, and runtime workspace-open flag to the repository root, so the host does not have to infer anything from the current working directory.

If your solution lives under a parent folder, still point the workspace variables at the repository root explicitly. Do not rely on folder heuristics.

The AgentRouter HTTP host has its own launch profile under `src/McpServer.AgentRouter.Host/Properties/launchSettings.json`, and it follows the same rule: set the workspace root explicitly instead of guessing it from the shell or IDE working directory.

## VS Code and Codex

Generate the local MCP client configs from the repo root:

```text
dotnet run --project .\tools\McpServer.AgentRouter.Tools -- install-local-clients --build
```

That command writes `.codex/config.toml`, `.vscode/mcp.json`, and `.mcp.json` with the repository root wired into the workspace env vars.

## CLI smoke and workspace root

The supported C# harnesses also pin the workspace root explicitly instead of guessing from the current directory. Use the typed `verify`, `smoke`, and `stress` commands from `tools/McpServer.AgentRouter.Tools` rather than shell-based helpers.

## Default workspace root

If no explicit workspace root is configured, MCPServer creates or reuses an application-owned default workspace instead of resolving `./workspace` relative to the host content root or current working directory.

Default locations follow the current OS user profile:

```text
Windows: %LOCALAPPDATA%\McpServer\workspace
Linux:   ~/.local/share/McpServer/workspace, depending on .NET LocalApplicationData
macOS:   user-local application data path, depending on .NET LocalApplicationData
```

You can override the application default with:

```text
MCPSERVER_DEFAULT_WORKSPACE_ROOT=D:\McpServer\workspace
```

## Why this exists

A relative `./workspace` fallback is dangerous when the host is launched from a compiled output folder or from an arbitrary shell directory. Without this resolver, the effective workspace can accidentally become:

```text
src\McpServer.Host\bin\Debug\net10.0\workspace
```

That is almost never what a developer wants. MCPServer now treats a missing root, empty root, or legacy `./workspace` placeholder as an application default unless `MCPSERVER__WORKSPACE__ROOTPATH` is explicitly set, and relative configured paths resolve against the host content root rather than the process current directory.

## SSH profile store

The shared `McpServer.Host` SSH tool reads named profiles from a file-backed store instead of environment variables.

Default locations:

```text
Repo-local profile file:  config/mcpserver/ssh-profiles.local.json
Repo example file:        config/mcpserver/ssh-profiles.local.example.json
Repo-local vault file:     config/mcpserver/ssh-vault.local.json
Repo example vault file:   config/mcpserver/ssh-vault.local.example.json
User-local profile file:   %LOCALAPPDATA%\McpServer\ssh-profiles.json
Vault key file:           %LOCALAPPDATA%\McpServer\ssh-vault.key
```

The repo-local files are resolved from the host content root, so they stay stable when the host is launched from Visual Studio, VS Code, or `dotnet run`.

Passwords should normally be stored as encrypted vault items in `ssh-vault.local.json`, then referenced from `ssh-profiles.local.json` via `passwordVaultItemName`. The dedicated vault CLI manages those entries:

```text
dotnet run --project .\tools\McpServer.SshVaultCli -- help
dotnet run --project .\tools\McpServer.SshVaultCli -- add dev --secret "..."
dotnet run --project .\tools\McpServer.SshVaultCli -- verify dev
dotnet run --project .\tools\McpServer.SshVaultCli -- delete dev
```

Credentials should live in the vault. Profiles should point to vault items with `passwordVaultItemName`, and private-key passphrases should use `privateKeyPassphraseVaultItemName`.

For the full workflow, command reference, and privileged profile guidance, see [SSH Vault](ssh-vault.md).
