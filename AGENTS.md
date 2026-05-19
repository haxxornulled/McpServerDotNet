# AGENTS

Repository instructions for coding agents, including Codex-style agents.

## Scope

These instructions apply to the entire repository.

## Build And Test Baseline

- Solution: `McpServer.slnx`
- CI workflow: `.github/workflows/ci.yml`
- CI configuration: `Release`
- CI platform: `windows-latest`

When validating CI-related fixes, mirror the workflow exactly:

```text
dotnet restore .\McpServer.slnx
dotnet build .\McpServer.slnx -c Release --no-restore -v minimal
dotnet test .\tests\McpServer.UnitTests\McpServer.UnitTests.csproj -c Release --no-build -v minimal
dotnet test .\tests\McpServer.IntegrationTests\McpServer.IntegrationTests.csproj -c Release --no-build -v minimal
```

If you run a targeted test with `--no-build`, rebuild that project first in the same configuration you are testing.

## CI Failure Handling

When a user asks to kick CI, inspect a failed run, or fix CI:

1. Read `.github/workflows/ci.yml` first.
2. Inspect the exact run with `gh run view`.
3. Reproduce the failure locally in `Release`.
4. Fix the root cause, not the symptom.
5. Re-run the narrowest local validation that proves the fix.
6. Re-run the CI-equivalent command path.
7. Commit, push, and re-dispatch CI if the user wants the workflow updated remotely.

Useful commands:

```text
gh run view <run-id> --json status,conclusion,url,jobs,displayTitle,headSha,updatedAt
gh run view <run-id> --log-failed
gh workflow run .github/workflows/ci.yml --ref main
gh run list --workflow ci.yml --limit 3 --json databaseId,status,conclusion,url,displayTitle,headSha,headBranch,event,createdAt
git push origin main
```

## Testing Guidance

- Never assume `Debug` paths in tests that can run in CI under `Release`.
- Prefer deriving expected artifact paths from `AppContext.BaseDirectory` or the active test configuration.
- Keep integration tests independent from the caller's current working directory.
- Preserve strong assertions. Do not paper over failures by removing checks.

## Documentation Guidance

If you add or materially change tooling, workflows, or public behavior, update the relevant docs in the same change:

- `README.md`
- `CHANGELOG.md`
- `docs/architecture.md`
- `docs/method-summary.md`

## Extension Points

Follow the existing architecture seams when extending the server:

- add new tools by implementing `IToolHandler<TRequest>` and registering the handler in Autofac plus `ToolCallRouter`
- add new resources by implementing `IResourceHandler` and registering the handler in Autofac
- add new prompts by implementing `IPromptHandler` and registering the handler in Autofac
- add new infrastructure services behind application abstractions instead of coupling protocol or host layers to concrete implementations

## Commit Hygiene

- Do not commit generated `.lscache` files, temporary logs, or files under runtime workspace outputs.
- Keep commits focused.
- Include the fix and the minimum documentation updates needed to explain it.

## Expected Closeout

When you finish a CI-related fix, report:

- the root cause
- the local validation commands that passed
- the commit SHA
- the push result
- the rerun workflow URL and final status if available
## Local AI Delegation Policy

Use MCPServer as the controlled local automation boundary.

Prefer Ollama-backed MCPServer inference tools for:

- repo/file/diff summaries;
- first-pass issue discovery;
- implementation planning;
- low-risk review scouting;
- documentation drafts.

Use Codex high-reasoning mode for:

- final implementation;
- concurrency-sensitive code;
- security-sensitive changes;
- MCP protocol behavior;
- public API and contract changes;
- final review before commit.

Current local-inference baseline:

```text
Default model: qwen3-coder:30b
Context length: 131072
Max prompt chars: 500000
Max output chars: 32000
Num predict: 32000
Temperature: 0.15
```

Do not enable shell, SSH, web, or destructive filesystem operations from prompts. Those capabilities must remain controlled by local MCPServer configuration.
