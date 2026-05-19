# Codex / VS Code MCP setup

Use the repo script to generate local client config for the current checkout. Do not check the generated files into source control.

Generated local files:

```text
.codex/config.toml
.vscode/mcp.json
.mcp.json
```

Generate or refresh them from the repo root:

```powershell
.\scripts\Install-LocalMcpClients.ps1 -Build
```

Or skip the build:

```powershell
.\scripts\Install-LocalMcpClients.ps1
```

## Required local model setup

```powershell
ollama pull qwen3-coder:30b
ollama pull qwen2.5-coder:14b
ollama pull devstral-small-2
ollama serve
```

Verify Ollama:

```powershell
curl http://127.0.0.1:11434/api/tags
```

## Build the stdio host

```powershell
dotnet build .\McpServer.slnx -c Release -v minimal
```

The generated config points at the built `McpServer.Host` executable under the current repo checkout.

## Visual Studio

If you debug the stdio host from Visual Studio, use the checked-in launch profile in `src/McpServer.Host/Properties/launchSettings.json`.

That profile already sets the workspace root explicitly to the repository root, so the host does not depend on the current working directory or compiled output folder.

## Safety defaults

The generated configs are intended to stay conservative:

- web disabled unless you opt in
- SSH disabled unless you opt in
- shell limited to a narrow allowlist
- destructive filesystem operations can be denied at the client layer
- Ollama loopback-only by default

Representative environment values:

```text
MCPSERVER__OLLAMA__BASEURL=http://127.0.0.1:11434
MCPSERVER__OLLAMA__ALLOWNONLOOPBACKBASEURL=false
MCPSERVER__WORKSPACE__ROOTPATH=<repo-root>
MCPSERVER__WORKSPACE__ALLOWEDROOTS__0=<repo-root>
```

## Quick prompts

After the MCP server is connected:

```text
Use inference.local_status and tell me if Ollama is reachable.
```

Then:

```text
Use inference.local_plan with the local default model to create a short plan for optimizing MCPServer startup.
```

## Workspace fallback

If you do not set an explicit workspace root, `McpServer.Host` falls back to its application-owned workspace instead of resolving a relative `./workspace` from the process current directory or a compiled output folder. For repo work, prefer explicit `MCPSERVER__WORKSPACE__ROOTPATH` and `MCPSERVER__WORKSPACE__ALLOWEDROOTS__0`.
