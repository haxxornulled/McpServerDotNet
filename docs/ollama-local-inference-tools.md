# Ollama local inference tools

`McpServer.Host` can expose bounded Ollama-backed helper tools for cheap local scouting while keeping MCPServer as the audit and policy boundary.

## Exposed tools

When `McpServer:Ollama:Enabled` is `true`, the host advertises:

- `inference.local_status`
- `inference.local_complete`
- `inference.local_summarize`
- `inference.local_code_review`
- `inference.local_plan`

These tools do not modify files. They call the configured Ollama endpoint and return structured results.

## Current default local-model baseline

```text
Default model: qwen3-coder:30b
Context length: 131072
Max prompt chars: 500000
Max output chars: 32000
Num predict: 32000
Temperature: 0.15
Fast fallback: qwen2.5-coder:14b
```

## Configuration

Safe repo default remains disabled in `src/McpServer.Host/appsettings.json`:

```json
"Ollama": {
  "Enabled": false,
  "BaseUrl": "http://127.0.0.1:11434",
  "DefaultModel": "qwen3-coder:30b",
  "AllowedModels": [
    "qwen3-coder:30b",
    "qwen2.5-coder:14b",
    "devstral-small-2"
  ],
  "TimeoutSeconds": 240,
  "MaxTimeoutSeconds": 900,
  "MaxPromptChars": 500000,
  "MaxOutputChars": 32000,
  "ContextLength": 131072,
  "NumPredict": 32000,
  "Temperature": 0.15,
  "AllowNonLoopbackBaseUrl": false
}
```

Typical local enablement uses environment variables:

```text
MCPSERVER__OLLAMA__ENABLED=true
MCPSERVER__OLLAMA__BASEURL=http://127.0.0.1:11434
MCPSERVER__OLLAMA__DEFAULTMODEL=qwen3-coder:30b
MCPSERVER__OLLAMA__ALLOWEDMODELS__0=qwen3-coder:30b
MCPSERVER__OLLAMA__ALLOWEDMODELS__1=qwen2.5-coder:14b
MCPSERVER__OLLAMA__ALLOWEDMODELS__2=devstral-small-2
MCPSERVER__OLLAMA__TIMEOUTSECONDS=240
MCPSERVER__OLLAMA__MAXTIMEOUTSECONDS=900
MCPSERVER__OLLAMA__MAXPROMPTCHARS=500000
MCPSERVER__OLLAMA__MAXOUTPUTCHARS=32000
MCPSERVER__OLLAMA__CONTEXTLENGTH=131072
MCPSERVER__OLLAMA__NUMPREDICT=32000
MCPSERVER__OLLAMA__TEMPERATURE=0.15
MCPSERVER__OLLAMA__ALLOWNONLOOPBACKBASEURL=false
```

## Local client config generation

The repo does not keep machine-local `.codex/config.toml`, `.vscode/mcp.json`, or `.mcp.json` checked in. Generate them with:

```text
dotnet run --project .\tools\McpServer.AgentRouter.Tools -- install-local-clients
```

## Start and verify

```text
ollama pull qwen3-coder:30b
ollama pull qwen2.5-coder:14b
ollama pull devstral-small-2
ollama serve
curl http://127.0.0.1:11434/api/tags

dotnet build .\McpServer.slnx -c Release -v minimal
```

Then ask your MCP client:

```text
Use inference.local_status and tell me if Ollama is reachable.
```

Then:

```text
Use inference.local_plan with the local default model to make a short plan for optimizing MCPServer startup.
```

## Intended usage

Use the Ollama helper tools for:

- large file or diff summaries
- cheap first-pass code review
- implementation planning
- low-cost scouting before higher-reasoning work

Use the main coding agent for:

- final implementation
- protocol-sensitive changes
- security-sensitive review
- final build/test interpretation

## Safety notes

- disabled by default in appsettings
- loopback-only by default
- non-loopback requires explicit opt-in
- prompt and output sizes are bounded
- requested models are allowlisted
