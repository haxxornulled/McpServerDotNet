<#
.SYNOPSIS
    Smoke-tests the current MCPServer AgentRouter local HTTP surface.

.DESCRIPTION
    Tests:
      - AgentRouter /health
      - AgentRouter /v1/models deterministic model list
      - Ollama /api/tags reachability
      - POST /v1/chat/completions non-streaming success
      - stream=true SSE success
      - unknown model clean 400 rejection
      - empty messages clean 400 rejection
      - GET /agent/mcp/tools stdio catalog success
      - POST /agent/mcp/tools/call allowlisted tool success
      - POST /agent/mcp/tools/call denied-tool rejection
      - POST /agent/runs lifecycle success
      - GET /agent/runs/{id} stored-run lookup
      - durable agent run artifact files
      - missing agent run clean 404 rejection
      - invalid agent run clean 400 rejection
      - small concurrency sanity test
      - optional provider-unavailable 503 test when Ollama is intentionally stopped

    Compatible with Windows PowerShell 5.1.

.EXAMPLE
    .\Test-AgentRouter.ps1

.EXAMPLE
    .\Test-AgentRouter.ps1 -RouterBaseUrl "http://127.0.0.1:5177" -OllamaBaseUrl "http://127.0.0.1:11434"

.EXAMPLE
    # Stop Ollama first, then run:
    .\Test-AgentRouter.ps1 -OnlyProviderUnavailable
#>

[CmdletBinding()]
param(
    [string] $RouterBaseUrl = "http://127.0.0.1:5177",

    [string] $OllamaBaseUrl = "http://127.0.0.1:11434",

    [string] $ChatModel = "fast-local",

    [string[]] $ExpectedModelOrder = @(
        "local-code",
        "fast-local",
        "local-agent"
    ),

    [int] $ConcurrencyCount = 3,

    [string] $RunStorageRootPath = "workspace/artifacts/agent-runs",

    [int] $ExpectedMinimumMcpToolCount = 1,

    [string] $McpToolTraceRootPath = "workspace/artifacts/mcp-tool-calls",

    [switch] $OnlyProviderUnavailable
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:Passed = 0
$script:Failed = 0
$script:Skipped = 0

function Write-Section {
    param([string] $Name)

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host $Name -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan
}

function Write-Pass {
    param([string] $Message)

    $script:Passed++
    Write-Host "[PASS] $Message" -ForegroundColor Green
}

function Write-Fail {
    param([string] $Message)

    $script:Failed++
    Write-Host "[FAIL] $Message" -ForegroundColor Red
}

function Write-Skip {
    param([string] $Message)

    $script:Skipped++
    Write-Host "[SKIP] $Message" -ForegroundColor Yellow
}

function New-ChatBody {
    param(
        [string] $Model,
        [string] $Content,
        [bool] $Stream = $false
    )

    return @{
        model = $Model
        messages = @(
            @{
                role = "user"
                content = $Content
            }
        )
        stream = $Stream
    } | ConvertTo-Json -Depth 20
}

function New-EmptyMessagesBody {
    param(
        [string] $Model
    )

    return @{
        model = $Model
        messages = @()
        stream = $false
    } | ConvertTo-Json -Depth 20
}

function Invoke-JsonRequest {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("GET", "POST")]
        [string] $Method,

        [Parameter(Mandatory = $true)]
        [string] $Uri,

        [string] $Body
    )

    try {
        if ($Method -eq "GET") {
            $result = Invoke-RestMethod -Uri $Uri -Method Get
        }
        else {
            $result = Invoke-RestMethod `
                -Uri $Uri `
                -Method Post `
                -ContentType "application/json" `
                -Body $Body
        }

        return [PSCustomObject] @{
            Success = $true
            StatusCode = 200
            Body = $result
            RawBody = $null
            ErrorMessage = $null
        }
    }
    catch {
        $statusCode = $null
        $rawBody = $null

        if ($_.Exception.Response -ne $null) {
            try {
                $statusCode = $_.Exception.Response.StatusCode.value__

                $stream = $_.Exception.Response.GetResponseStream()
                if ($stream -ne $null) {
                    $reader = New-Object System.IO.StreamReader($stream)
                    $rawBody = $reader.ReadToEnd()
                    $reader.Dispose()
                }
            }
            catch {
                $rawBody = $_.Exception.Message
            }
        }

        return [PSCustomObject] @{
            Success = $false
            StatusCode = $statusCode
            Body = $null
            RawBody = $rawBody
            ErrorMessage = $_.Exception.Message
        }
    }
}

function Invoke-StreamRequest {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("GET", "POST")]
        [string] $Method,

        [Parameter(Mandatory = $true)]
        [string] $Uri,

        [string] $Body
    )

    try {
        $webRequestParams = @{
            Uri = $Uri
            Method = $Method
        }

        if ((Get-Command Invoke-WebRequest).Parameters.ContainsKey("UseBasicParsing")) {
            $webRequestParams.UseBasicParsing = $true
        }

        if ($Method -eq "GET") {
            $result = Invoke-WebRequest @webRequestParams
        }
        else {
            $webRequestParams.ContentType = "application/json"
            $webRequestParams.Body = $Body
            $result = Invoke-WebRequest @webRequestParams
        }

        return [PSCustomObject] @{
            Success = $true
            StatusCode = $result.StatusCode
            Body = $null
            RawBody = $result.Content
            ErrorMessage = $null
        }
    }
    catch {
        $statusCode = $null
        $rawBody = $null

        if ($_.Exception.Response -ne $null) {
            try {
                $statusCode = [int] $_.Exception.Response.StatusCode

                $stream = $_.Exception.Response.GetResponseStream()
                if ($stream -ne $null) {
                    $reader = New-Object System.IO.StreamReader($stream)
                    $rawBody = $reader.ReadToEnd()
                    $reader.Dispose()
                }
            }
            catch {
                $rawBody = $_.Exception.Message
            }
        }

        return [PSCustomObject] @{
            Success = $false
            StatusCode = $statusCode
            Body = $null
            RawBody = $rawBody
            ErrorMessage = $_.Exception.Message
        }
    }
}

function ConvertFrom-JsonOrNull {
    param([string] $Json)

    if ([string]::IsNullOrWhiteSpace($Json)) {
        return $null
    }

    try {
        return $Json | ConvertFrom-Json
    }
    catch {
        return $null
    }
}

function Assert-Status {
    param(
        [string] $Name,
        [object] $Response,
        [int] $ExpectedStatusCode
    )

    if ($Response.StatusCode -eq $ExpectedStatusCode) {
        Write-Pass "$Name returned HTTP $ExpectedStatusCode."
        return $true
    }

    Write-Fail "$Name returned HTTP $($Response.StatusCode), expected HTTP $ExpectedStatusCode. Body: $($Response.RawBody)"
    return $false
}

function Assert-ErrorEnvelope {
    param(
        [string] $Name,
        [object] $Response,
        [string] $ExpectedCode,
        [string] $ExpectedType
    )

    $json = ConvertFrom-JsonOrNull -Json $Response.RawBody

    if ($json -eq $null -or $json.error -eq $null) {
        Write-Fail "$Name did not return an OpenAI-style error envelope. Body: $($Response.RawBody)"
        return
    }

    $actualCode = $json.error.code
    $actualType = $json.error.type
    $message = $json.error.message

    if ($actualCode -eq $ExpectedCode -and $actualType -eq $ExpectedType) {
        Write-Pass "$Name returned expected error code '$ExpectedCode' and type '$ExpectedType'. Message: $message"
        return
    }

    Write-Fail "$Name returned code '$actualCode' / type '$actualType', expected code '$ExpectedCode' / type '$ExpectedType'. Message: $message"
}

function Resolve-LocalPath {
    param([string] $PathValue)

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return $null
    }

    $expanded = [Environment]::ExpandEnvironmentVariables($PathValue)
    if ([System.IO.Path]::IsPathRooted($expanded)) {
        return [System.IO.Path]::GetFullPath($expanded)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $expanded))
}


function Resolve-AgentRunDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RunStorageRootPath,

        [Parameter(Mandatory = $true)]
        [string] $RunId
    )

    $candidateRoots = @()

    $environmentRoot = [Environment]::GetEnvironmentVariable("AgentRouter__RunStorage__RootPath")
    if (-not [string]::IsNullOrWhiteSpace($environmentRoot)) {
        $candidateRoots += (Resolve-LocalPath -PathValue $environmentRoot)
    }

    $candidateRoots += (Resolve-LocalPath -PathValue $RunStorageRootPath)

    if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        $repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
        $candidateRoots += [System.IO.Path]::GetFullPath((Join-Path $repoRoot $RunStorageRootPath))
        $candidateRoots += [System.IO.Path]::GetFullPath((Join-Path $repoRoot (Join-Path "src/McpServer.AgentRouter.Host" $RunStorageRootPath)))
    }

    $distinctRoots = @($candidateRoots |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique)

    foreach ($root in $distinctRoots) {
        $candidateRunDirectory = Join-Path $root $RunId
        if (Test-Path -LiteralPath $candidateRunDirectory -PathType Container) {
            return $candidateRunDirectory
        }
    }

    return (Join-Path $distinctRoots[0] $RunId)
}

function Resolve-McpToolCallTraceDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string] $TraceRootPath,

        [Parameter(Mandatory = $true)]
        [string] $TraceId
    )

    $candidateRoots = @()

    $environmentRoot = [Environment]::GetEnvironmentVariable("AgentRouter__ToolExecution__TraceRootPath")
    if (-not [string]::IsNullOrWhiteSpace($environmentRoot)) {
        $candidateRoots += (Resolve-LocalPath -PathValue $environmentRoot)
    }

    $candidateRoots += (Resolve-LocalPath -PathValue $TraceRootPath)

    if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        $repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
        $candidateRoots += [System.IO.Path]::GetFullPath((Join-Path $repoRoot $TraceRootPath))
        $candidateRoots += [System.IO.Path]::GetFullPath((Join-Path $repoRoot (Join-Path "src/McpServer.AgentRouter.Host" $TraceRootPath)))
    }

    $distinctRoots = @($candidateRoots |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique)

    foreach ($root in $distinctRoots) {
        $candidateTraceDirectory = Join-Path $root $TraceId
        if (Test-Path -LiteralPath $candidateTraceDirectory -PathType Container) {
            return $candidateTraceDirectory
        }
    }

    return (Join-Path $distinctRoots[0] $TraceId)
}

function Test-ProviderUnavailable {
    Write-Section "Provider unavailable test"

    $body = New-ChatBody `
        -Model $ChatModel `
        -Content "Reply with exactly: router online" `
        -Stream $false

    $response = Invoke-JsonRequest `
        -Method POST `
        -Uri "$RouterBaseUrl/v1/chat/completions" `
        -Body $body

    if (Assert-Status -Name "Ollama unavailable chat completion" -Response $response -ExpectedStatusCode 503) {
        Assert-ErrorEnvelope `
            -Name "Ollama unavailable chat completion" `
            -Response $response `
            -ExpectedCode "provider_unavailable" `
            -ExpectedType "service_unavailable_error"
    }
}

if ($OnlyProviderUnavailable) {
    Test-ProviderUnavailable

    Write-Section "Summary"
    Write-Host "Passed:  $script:Passed" -ForegroundColor Green
    Write-Host "Failed:  $script:Failed" -ForegroundColor Red
    Write-Host "Skipped: $script:Skipped" -ForegroundColor Yellow

    if ($script:Failed -gt 0) {
        exit 1
    }

    exit 0
}

Write-Section "Configuration"
Write-Host "RouterBaseUrl:     $RouterBaseUrl"
Write-Host "OllamaBaseUrl:     $OllamaBaseUrl"
Write-Host "ChatModel:         $ChatModel"
Write-Host "Expected models:   $($ExpectedModelOrder -join ', ')"
Write-Host "ConcurrencyCount:  $ConcurrencyCount"
Write-Host "RunStorageRoot:    $RunStorageRootPath"
Write-Host "McpToolTraceRoot:  $McpToolTraceRootPath"
Write-Host "Min MCP tools:     $ExpectedMinimumMcpToolCount"

Write-Section "AgentRouter health"

$health = Invoke-JsonRequest -Method GET -Uri "$RouterBaseUrl/health"

if ($health.Success) {
    Write-Pass "AgentRouter /health responded."
    $health.Body | Format-List | Out-String | Write-Host
}
else {
    Write-Fail "AgentRouter /health failed. Status: $($health.StatusCode). Error: $($health.ErrorMessage). Body: $($health.RawBody)"
}

Write-Section "AgentRouter models"

$modelsResponse = Invoke-JsonRequest -Method GET -Uri "$RouterBaseUrl/v1/models"

if ($modelsResponse.Success) {
    $actualModelOrder = @()
    foreach ($item in $modelsResponse.Body.data) {
        $actualModelOrder += [string] $item.id
    }

    Write-Host "Returned model order:"
    $modelsResponse.Body.data | Select-Object id, object | Format-Table | Out-String | Write-Host

    $expectedJoined = $ExpectedModelOrder -join "|"
    $actualJoined = $actualModelOrder -join "|"

    if ($actualJoined -eq $expectedJoined) {
        Write-Pass "/v1/models returned expected deterministic order."
    }
    else {
        Write-Fail "/v1/models order mismatch. Expected '$expectedJoined', actual '$actualJoined'."
    }
}
else {
    Write-Fail "/v1/models failed. Status: $($modelsResponse.StatusCode). Error: $($modelsResponse.ErrorMessage). Body: $($modelsResponse.RawBody)"
}


Write-Section "MCP host stdio tool catalog"

$mcpToolsResponse = Invoke-JsonRequest -Method GET -Uri "$RouterBaseUrl/agent/mcp/tools"

if ($mcpToolsResponse.Success) {
    $toolCount = [int] $mcpToolsResponse.Body.toolCount
    $serverName = [string] $mcpToolsResponse.Body.server.name
    $serverVersion = [string] $mcpToolsResponse.Body.server.version
    $protocolVersion = [string] $mcpToolsResponse.Body.protocolVersion
    $transport = [string] $mcpToolsResponse.Body.transport
    $status = [string] $mcpToolsResponse.Body.status
    $elapsedMilliseconds = [long] $mcpToolsResponse.Body.elapsedMilliseconds

    Write-Host "MCP catalog summary:"
    [PSCustomObject] @{
        Status = $status
        Transport = $transport
        ProtocolVersion = $protocolVersion
        ToolCount = $toolCount
        ElapsedMilliseconds = $elapsedMilliseconds
        Server = "$serverName $serverVersion"
    } | Format-List | Out-String | Write-Host

    if ($mcpToolsResponse.Body.tools -ne $null) {
        Write-Host "First MCP tools:"
        $mcpToolsResponse.Body.tools |
            Select-Object -First 10 name, title, description |
            Format-Table -AutoSize |
            Out-String |
            Write-Host
    }

    if ($toolCount -ge $ExpectedMinimumMcpToolCount) {
        Write-Pass "/agent/mcp/tools returned $toolCount tools from MCPServer.Host over stdio."
    }
    else {
        Write-Fail "/agent/mcp/tools returned $toolCount tools, expected at least $ExpectedMinimumMcpToolCount."
    }
}
else {
    Write-Fail "/agent/mcp/tools failed. Status: $($mcpToolsResponse.StatusCode). Error: $($mcpToolsResponse.ErrorMessage). Body: $($mcpToolsResponse.RawBody)"
}

Write-Section "MCP host stdio tool call"

$mcpToolCallBody = @{
    toolName = "fs.list_directory"
    arguments = @{
        path = "."
    }
} | ConvertTo-Json -Depth 20

$mcpToolCallResponse = Invoke-JsonRequest `
    -Method POST `
    -Uri "$RouterBaseUrl/agent/mcp/tools/call" `
    -Body $mcpToolCallBody

if ($mcpToolCallResponse.Success) {
    $traceId = [string] $mcpToolCallResponse.Body.traceId
    $status = [string] $mcpToolCallResponse.Body.status
    $toolName = [string] $mcpToolCallResponse.Body.toolName
    $allowed = [bool] $mcpToolCallResponse.Body.allowed
    $elapsedMilliseconds = [long] $mcpToolCallResponse.Body.elapsedMilliseconds

    Write-Host "MCP tool call summary:"
    [PSCustomObject] @{
        Status = $status
        ToolName = $toolName
        Allowed = $allowed
        TraceId = $traceId
        ElapsedMilliseconds = $elapsedMilliseconds
    } | Format-List | Out-String | Write-Host

    if ($status -eq "completed" -and $toolName -eq "fs.list_directory" -and $allowed) {
        Write-Pass "POST /agent/mcp/tools/call executed fs.list_directory through MCP stdio."
    }
    else {
        Write-Fail "POST /agent/mcp/tools/call returned unexpected status '$status' for '$toolName'."
    }

    if (-not [string]::IsNullOrWhiteSpace($traceId)) {
        $traceDirectory = Resolve-McpToolCallTraceDirectory `
            -TraceRootPath $McpToolTraceRootPath `
            -TraceId $traceId

        $traceFile = Join-Path $traceDirectory "trace.json"
        if (Test-Path -LiteralPath $traceFile -PathType Leaf) {
            Write-Pass "MCP tool call trace was written under $traceDirectory."
        }
        else {
            Write-Fail "MCP tool call trace file was not found at $traceFile."
        }
    }
    else {
        Write-Fail "POST /agent/mcp/tools/call did not return a trace id."
    }
}
else {
    Write-Fail "POST /agent/mcp/tools/call failed. Status: $($mcpToolCallResponse.StatusCode). Error: $($mcpToolCallResponse.ErrorMessage). Body: $($mcpToolCallResponse.RawBody)"
}

$blockedToolBody = @{
    toolName = "fs.delete_path"
    arguments = @{
        path = "definitely-do-not-delete"
    }
} | ConvertTo-Json -Depth 20

$blockedToolResponse = Invoke-JsonRequest `
    -Method POST `
    -Uri "$RouterBaseUrl/agent/mcp/tools/call" `
    -Body $blockedToolBody

if (Assert-Status -Name "blocked MCP tool call" -Response $blockedToolResponse -ExpectedStatusCode 403) {
    $blockedJson = ConvertFrom-JsonOrNull -Json $blockedToolResponse.RawBody
    if ($blockedJson -ne $null -and $blockedJson.status -eq "denied" -and $blockedJson.allowed -eq $false) {
        Write-Pass "blocked MCP tool call returned a denied tool-call response with trace id '$($blockedJson.traceId)'."
    }
    else {
        Write-Fail "blocked MCP tool call did not return the expected denied tool-call response. Body: $($blockedToolResponse.RawBody)"
    }
}

Write-Section "Ollama availability"

$ollamaAvailable = $false
$ollamaTags = Invoke-JsonRequest -Method GET -Uri "$OllamaBaseUrl/api/tags"

if ($ollamaTags.Success) {
    $ollamaAvailable = $true
    Write-Pass "Ollama responded at $OllamaBaseUrl."

    if ($ollamaTags.Body.models -ne $null) {
        Write-Host "Available Ollama models:"
        $ollamaTags.Body.models | Select-Object name | Format-Table | Out-String | Write-Host
    }
}
else {
    Write-Fail "Ollama did not respond at $OllamaBaseUrl. Start it with: ollama serve"
    Write-Skip "Skipping Ollama-dependent positive-path tests. For the expected provider-down test, run with -OnlyProviderUnavailable."
}

Write-Section "Non-streaming chat completion"

if (-not $ollamaAvailable) {
    Write-Skip "Non-streaming chat completion requires Ollama to be running."
}
else {
    $chatBody = New-ChatBody `
        -Model $ChatModel `
        -Content "Reply with exactly: router online" `
        -Stream $false

    $chatResponse = Invoke-JsonRequest `
        -Method POST `
        -Uri "$RouterBaseUrl/v1/chat/completions" `
        -Body $chatBody

    if ($chatResponse.Success) {
        $content = $chatResponse.Body.choices[0].message.content

        Write-Host "Completion content:"
        Write-Host $content

        if ($content -match "router online") {
            Write-Pass "Non-streaming chat completion returned router online."
        }
        else {
            Write-Fail "Non-streaming chat completion succeeded but did not contain expected text."
        }
    }
    else {
        Write-Fail "Non-streaming chat completion failed. Status: $($chatResponse.StatusCode). Error: $($chatResponse.ErrorMessage). Body: $($chatResponse.RawBody)"
    }
}

Write-Section "Agent run lifecycle"

if (-not $ollamaAvailable) {
    Write-Skip "Agent run positive-path lifecycle requires Ollama to be running."
}
else {
    $agentRunBody = @{
        model = $ChatModel
        goal = "Reply with exactly: agent run online"
    } | ConvertTo-Json -Depth 20

    $agentRunResponse = Invoke-JsonRequest `
        -Method POST `
        -Uri "$RouterBaseUrl/agent/runs" `
        -Body $agentRunBody

    $agentRunId = $null

    if ($agentRunResponse.Success) {
        $agentRunId = $agentRunResponse.Body.id
        $agentRunStatus = $agentRunResponse.Body.status
        $agentRunResult = $agentRunResponse.Body.result

        Write-Host "Agent run id:     $agentRunId"
        Write-Host "Agent run status: $agentRunStatus"
        Write-Host "Agent run result:"
        Write-Host $agentRunResult

        if ($agentRunStatus -eq "completed" -and $agentRunResult -match "agent run online") {
            Write-Pass "POST /agent/runs created a completed run."
        }
        else {
            Write-Fail "POST /agent/runs returned status '$agentRunStatus' and result '$agentRunResult'."
        }
    }
    else {
        Write-Fail "POST /agent/runs failed. Status: $($agentRunResponse.StatusCode). Error: $($agentRunResponse.ErrorMessage). Body: $($agentRunResponse.RawBody)"
    }

    if (-not [string]::IsNullOrWhiteSpace($agentRunId)) {
        $getRunResponse = Invoke-JsonRequest `
            -Method GET `
            -Uri "$RouterBaseUrl/agent/runs/$agentRunId"

        if ($getRunResponse.Success) {
            if ($getRunResponse.Body.id -eq $agentRunId -and $getRunResponse.Body.status -eq "completed") {
                Write-Pass "GET /agent/runs/{id} returned the stored run."
            }
            else {
                Write-Fail "GET /agent/runs/{id} returned unexpected run payload."
            }
        }
        else {
            Write-Fail "GET /agent/runs/{id} failed. Status: $($getRunResponse.StatusCode). Error: $($getRunResponse.ErrorMessage). Body: $($getRunResponse.RawBody)"
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($agentRunId) -and $agentRunStatus -eq "completed") {
        $runDirectory = Resolve-AgentRunDirectory `
            -RunStorageRootPath $RunStorageRootPath `
            -RunId $agentRunId

        $expectedRunFiles = @(
            "request.json",
            "response.json",
            "artifacts.json",
            "plan.md",
            "generation.md",
            "trace.json"
        )

        $missingRunFiles = @()
        foreach ($fileName in $expectedRunFiles) {
            $candidate = Join-Path $runDirectory $fileName
            if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
                $missingRunFiles += $fileName
            }
        }

        if ($missingRunFiles.Count -eq 0) {
            Write-Pass "Durable run storage files were written under $runDirectory."
        }
        else {
            Write-Fail "Durable run storage missing files under ${runDirectory}: $($missingRunFiles -join ', ')"
        }
    }
}

$missingRunResponse = Invoke-JsonRequest `
    -Method GET `
    -Uri "$RouterBaseUrl/agent/runs/run-does-not-exist"

if (Assert-Status -Name "missing agent run" -Response $missingRunResponse -ExpectedStatusCode 404) {
    Assert-ErrorEnvelope `
        -Name "missing agent run" `
        -Response $missingRunResponse `
        -ExpectedCode "run_not_found" `
        -ExpectedType "not_found_error"
}

$invalidAgentRunBody = @{
    model = $ChatModel
    goal = ""
} | ConvertTo-Json -Depth 20

$invalidAgentRunResponse = Invoke-JsonRequest `
    -Method POST `
    -Uri "$RouterBaseUrl/agent/runs" `
    -Body $invalidAgentRunBody

if (Assert-Status -Name "invalid agent run" -Response $invalidAgentRunResponse -ExpectedStatusCode 400) {
    Assert-ErrorEnvelope `
        -Name "invalid agent run" `
        -Response $invalidAgentRunResponse `
        -ExpectedCode "goal_required" `
        -ExpectedType "invalid_request_error"
}


Write-Section "stream=true SSE response"

$streamBody = New-ChatBody `
    -Model $ChatModel `
    -Content "test" `
    -Stream $true

$streamResponse = Invoke-StreamRequest `
    -Method POST `
    -Uri "$RouterBaseUrl/v1/chat/completions" `
    -Body $streamBody

if (Assert-Status -Name "stream=true chat completion" -Response $streamResponse -ExpectedStatusCode 200) {
    if (($streamResponse.RawBody -match "data:") -and ($streamResponse.RawBody -match "\[DONE\]")) {
        Write-Pass "stream=true chat completion returned SSE chunks and a terminal [DONE] marker."
    }
    else {
        Write-Fail "stream=true chat completion did not return expected SSE output. Body: $($streamResponse.RawBody)"
    }
}

Write-Section "Unknown model rejection"

$unknownModelBody = New-ChatBody `
    -Model "does-not-exist" `
    -Content "test" `
    -Stream $false

$unknownModelResponse = Invoke-JsonRequest `
    -Method POST `
    -Uri "$RouterBaseUrl/v1/chat/completions" `
    -Body $unknownModelBody

if (Assert-Status -Name "unknown model chat completion" -Response $unknownModelResponse -ExpectedStatusCode 400) {
    Assert-ErrorEnvelope `
        -Name "unknown model chat completion" `
        -Response $unknownModelResponse `
        -ExpectedCode "unknown_model" `
        -ExpectedType "invalid_request_error"
}

Write-Section "Empty messages rejection"

$emptyMessagesBody = New-EmptyMessagesBody -Model $ChatModel

$emptyMessagesResponse = Invoke-JsonRequest `
    -Method POST `
    -Uri "$RouterBaseUrl/v1/chat/completions" `
    -Body $emptyMessagesBody

if (Assert-Status -Name "empty messages chat completion" -Response $emptyMessagesResponse -ExpectedStatusCode 400) {
    Assert-ErrorEnvelope `
        -Name "empty messages chat completion" `
        -Response $emptyMessagesResponse `
        -ExpectedCode "messages_required" `
        -ExpectedType "invalid_request_error"
}

Write-Section "Small concurrency sanity test"

if (-not $ollamaAvailable) {
    Write-Skip "Concurrency sanity test requires Ollama to be running."
}
else {
    $jobs = @()

    for ($index = 1; $index -le $ConcurrencyCount; $index++) {
        $jobs += Start-Job -ScriptBlock {
            param(
                [string] $RouterBaseUrl,
                [string] $ChatModel,
                [int] $Index
            )

            $body = @{
                model = $ChatModel
                messages = @(
                    @{
                        role = "user"
                        content = "Reply with exactly one word: ok"
                    }
                )
                stream = $false
            } | ConvertTo-Json -Depth 20

            try {
                $response = Invoke-RestMethod `
                    -Uri "$RouterBaseUrl/v1/chat/completions" `
                    -Method Post `
                    -ContentType "application/json" `
                    -Body $body

                [PSCustomObject] @{
                    Index = $Index
                    Success = $true
                    Content = $response.choices[0].message.content
                    Error = $null
                }
            }
            catch {
                [PSCustomObject] @{
                    Index = $Index
                    Success = $false
                    Content = $null
                    Error = $_.Exception.Message
                }
            }
        } -ArgumentList $RouterBaseUrl, $ChatModel, $index
    }

    $jobResults = $jobs | Wait-Job | Receive-Job

    foreach ($job in $jobs) {
        Remove-Job -Job $job -Force
    }

    $jobResults | Sort-Object Index | Format-Table Index, Success, Content, Error -AutoSize | Out-String | Write-Host

    $failedJobs = @($jobResults | Where-Object { -not $_.Success })

    if ($failedJobs.Count -eq 0) {
        Write-Pass "All $ConcurrencyCount concurrent chat requests completed."
    }
    else {
        Write-Fail "$($failedJobs.Count) of $ConcurrencyCount concurrent chat requests failed."
    }
}

Write-Section "Optional provider-unavailable test"

Write-Skip "To test patched 503 behavior: stop Ollama, leave AgentRouter running, then run: .\Test-AgentRouter.ps1 -OnlyProviderUnavailable"

Write-Section "Summary"
Write-Host "Passed:  $script:Passed" -ForegroundColor Green
Write-Host "Failed:  $script:Failed" -ForegroundColor Red
Write-Host "Skipped: $script:Skipped" -ForegroundColor Yellow

if ($script:Failed -gt 0) {
    exit 1
}

exit 0
