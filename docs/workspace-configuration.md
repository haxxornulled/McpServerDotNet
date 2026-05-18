# Workspace Configuration

MCPServer always starts with a safe workspace root.

## Explicit workspace root

For normal Codex or VS Code MCP usage, set the workspace root with environment variables:

```text
MCPSERVER__WORKSPACE__ROOTPATH=D:\2026 Projects\McpServerRepo
MCPSERVER__WORKSPACE__ALLOWEDROOTS__0=D:\2026 Projects\McpServerRepo
```

This makes the repository itself visible to MCPServer tools.

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
