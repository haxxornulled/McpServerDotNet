param(
    [string]$InferenceBaseUrl = 'http://127.0.0.1:1234',
    [string]$Model = '',
    [string]$WorkspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string]$ExpectedEntry = 'README.md',
    [int]$InferenceTimeoutSeconds = 180
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $WorkspaceRoot)) {
    throw "Workspace root does not exist: $WorkspaceRoot"
}

if (-not (Test-Path -LiteralPath (Join-Path -Path $WorkspaceRoot -ChildPath $ExpectedEntry))) {
    throw "Expected entry '$ExpectedEntry' was not found under $WorkspaceRoot"
}

Write-Host "Testing LM Studio workspace access through $InferenceBaseUrl"
Write-Host "Workspace root: $WorkspaceRoot"
Write-Host "Expected entry: $ExpectedEntry"

$harness = Join-Path -Path $PSScriptRoot -ChildPath 'Invoke-InferenceToolSmokeTest.ps1'
$includeTools = @('workspace.set_root', 'fs.list_directory', 'workspace.inspect')
$scenarioPath = Join-Path -Path ([System.IO.Path]::GetTempPath()) -ChildPath ("mcpserver-workspace-scenarios-" + [System.Guid]::NewGuid().ToString('N') + ".json")

$scenarioContent = @(
    @{
        tool = 'workspace.set_root'
        prompt = "Call workspace.set_root with path ""$WorkspaceRoot""."
        expectToolError = $false
        toolResultContains = @($WorkspaceRoot)
    }
    @{
        tool = 'fs.list_directory'
        dependsOn = @('workspace.set_root')
        prompt = 'Call fs.list_directory with path "workspace".'
        expectToolError = $false
        toolResultContains = @($ExpectedEntry)
    }
    @{
        tool = 'workspace.inspect'
        dependsOn = @('workspace.set_root')
        prompt = 'Call workspace.inspect with path "workspace", maxDepth 6, maxFiles 200, maxFileBytes 8000, and maxTotalFileBytes 12000 so you can begin a code review without flooding the model context.'
        expectToolError = $false
        toolResultContains = @($ExpectedEntry, 'README.md')
    }
)

$scenarioContent | ConvertTo-Json -Depth 10 | Set-Content -Path $scenarioPath -Encoding UTF8
try {
    if ([string]::IsNullOrWhiteSpace($Model)) {
        & $harness `
            -InferenceBaseUrl $InferenceBaseUrl `
            -ScenarioPath $scenarioPath `
            -IncludeTool $includeTools `
            -InferenceTimeoutSeconds $InferenceTimeoutSeconds `
            -McpTimeoutSeconds 30 `
            -MaxConversationTurns 4
    } else {
        & $harness `
            -InferenceBaseUrl $InferenceBaseUrl `
            -Model $Model `
            -ScenarioPath $scenarioPath `
            -IncludeTool $includeTools `
            -InferenceTimeoutSeconds $InferenceTimeoutSeconds `
            -McpTimeoutSeconds 30 `
            -MaxConversationTurns 4
    }
}
finally {
    if (Test-Path -LiteralPath $scenarioPath) {
        Remove-Item -LiteralPath $scenarioPath -Force
    }
}
