# Backlog

## MCPTOOLS-001 — Add structured-output auto-router

### Goal

Add enterprise-grade structured output support without coupling existing filesystem, shell, web, or SSH tools directly to the inference backend.

### Scope

- Add an `Inference` bounded context.
- Add a rule-first output-mode router.
- Add a schema registry for:
  - `review_code`
  - `plan_work`
  - `write_code`
  - `fix_build`
  - `commands`
  - `architecture`
  - `explain`
- Add an OpenAI-compatible local inference client.
- Add structured-output validation.
- Add one repair attempt for invalid JSON or schema validation failures.
- Add MCP tools:
  - `inference.route`
  - `inference.schemas.list`
  - `inference.complete_structured`
- Add Serilog and OpenTelemetry instrumentation.
- Add unit tests for routing, schema lookup, validation failure, repair attempt, and successful structured completion.

### Non-goals

- Do not modify existing filesystem, shell, web, or SSH tools to depend directly on the inference backend.
- Do not hard-code inference branches into the MCP tool router.
- Do not make structured output a system-prompt-only behavior.

### Acceptance criteria

- Existing tools continue to work through the generic MCP tool registry.
- Structured inference tools are optional and independently registered.
- Schema selection happens before the inference request.
- Response JSON is validated before being returned to MCP clients.
- Invalid structured output performs at most one repair attempt.
- Telemetry records mode, schema name, validation status, attempt count, duration, and failure reason.
