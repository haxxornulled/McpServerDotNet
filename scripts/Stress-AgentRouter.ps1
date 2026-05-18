<#
.SYNOPSIS
    Stress-tests the MCPServer AgentRouter local runtime.

.DESCRIPTION
    Exercises the stable AgentRouter surfaces with bounded concurrency:
      - GET  /health
      - GET  /v1/models
      - GET  /agent/mcp/tools
      - POST /v1/chat/completions
      - POST /agent/runs
      - POST /agent/mcp/tools/call using fs.list_directory

    Writes machine-readable reports under:
      workspace/artifacts/stress-runs/{runId}/

    Compatible with Windows PowerShell 5.1.

.EXAMPLE
    .\scripts\Stress-AgentRouter.ps1

.EXAMPLE
    .\scripts\Stress-AgentRouter.ps1 -ChatRequests 25 -ChatConcurrency 5 -AgentRunRequests 10 -AgentRunConcurrency 3
#>

[CmdletBinding()]
param(
    [string] $RouterBaseUrl = "http://127.0.0.1:5177",

    [string] $ChatModel = "fast-local",

    [string] $ReportRootPath = "workspace/artifacts/stress-runs",

    [int] $ChatRequests = 12,

    [int] $ChatConcurrency = 3,

    [int] $AgentRunRequests = 6,

    [int] $AgentRunConcurrency = 2,

    [int] $McpCatalogRequests = 20,

    [int] $McpCatalogConcurrency = 4,

    [int] $McpToolCallRequests = 12,

    [int] $McpToolCallConcurrency = 3,

    [int] $RequestTimeoutSeconds = 120,

    [switch] $SkipChat,

    [switch] $SkipAgentRuns,

    [switch] $SkipMcpCatalog,

    [switch] $SkipMcpToolCalls
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:Failed = 0
$script:Passed = 0
$script:AllResults = New-Object System.Collections.Generic.List[object]

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

function Write-Info {
    param([string] $Message)

    Write-Host "[INFO] $Message" -ForegroundColor Gray
}

function Resolve-RepoPath {
    param([string] $Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path (Resolve-Path ".").Path $Path
}

function Invoke-PreflightGet {
    param(
        [string] $Name,
        [string] $Uri
    )

    try {
        $response = Invoke-RestMethod -Uri $Uri -Method Get -TimeoutSec 15
        Write-Pass "$Name responded."
        return $response
    }
    catch {
        Write-Fail "$Name failed at $Uri. $($_.Exception.Message)"
        return $null
    }
}

function Get-Percentile {
    param(
        [double[]] $Values,
        [double] $Percentile
    )

    if ($Values.Count -eq 0) {
        return 0
    }

    $sorted = @($Values | Sort-Object)

    if ($sorted.Count -eq 1) {
        return [Math]::Round([double] $sorted[0], 2)
    }

    $rank = ($Percentile / 100.0) * ($sorted.Count - 1)
    $lower = [Math]::Floor($rank)
    $upper = [Math]::Ceiling($rank)

    if ($lower -eq $upper) {
        return [Math]::Round([double] $sorted[$lower], 2)
    }

    $weight = $rank - $lower
    $value = ([double] $sorted[$lower] * (1.0 - $weight)) + ([double] $sorted[$upper] * $weight)
    return [Math]::Round($value, 2)
}

function Add-Results {
    param([object[]] $Results)

    foreach ($result in $Results) {
        $script:AllResults.Add($result) | Out-Null
    }
}

function Write-WorkloadSummary {
    param(
        [string] $Name,
        [object[]] $Results
    )

    $total = @($Results).Count
    $successes = @($Results | Where-Object { $_.Success })
    $failures = @($Results | Where-Object { -not $_.Success })
    $latencies = @($Results | ForEach-Object { [double] $_.ElapsedMilliseconds })

    $avg = 0
    $min = 0
    $max = 0
    $p50 = 0
    $p95 = 0

    if ($latencies.Count -gt 0) {
        $avg = [Math]::Round((($latencies | Measure-Object -Average).Average), 2)
        $min = [Math]::Round((($latencies | Measure-Object -Minimum).Minimum), 2)
        $max = [Math]::Round((($latencies | Measure-Object -Maximum).Maximum), 2)
        $p50 = Get-Percentile -Values $latencies -Percentile 50
        $p95 = Get-Percentile -Values $latencies -Percentile 95
    }

    [PSCustomObject] @{
        Workload = $Name
        Total = $total
        Success = $successes.Count
        Failed = $failures.Count
        MinMs = $min
        AvgMs = $avg
        P50Ms = $p50
        P95Ms = $p95
        MaxMs = $max
    } | Format-List | Out-String | Write-Host

    if ($failures.Count -eq 0) {
        Write-Pass "$Name completed $total requests with 0 failures."
    }
    else {
        Write-Fail "$Name had $($failures.Count) failures out of $total requests."
        $failures | Select-Object -First 5 Workload, Index, StatusCode, Error | Format-Table -AutoSize | Out-String | Write-Host
    }
}

function Invoke-BoundedJobBatch {
    param(
        [string] $Name,
        [int] $TotalRequests,
        [int] $Concurrency,
        [scriptblock] $JobScript,
        [object[]] $CommonArguments
    )

    if ($TotalRequests -le 0) {
        Write-Info "$Name skipped because request count is $TotalRequests."
        return @()
    }

    if ($Concurrency -le 0) {
        throw "$Name concurrency must be greater than zero."
    }

    Write-Section "$Name workload"
    Write-Host "Requests:    $TotalRequests"
    Write-Host "Concurrency: $Concurrency"

    # Windows PowerShell 5.1 is picky about generic List[object] being passed
    # to Wait-Job -Job. Keep this as a normal PowerShell array so the engine
    # binds BackgroundJob instances correctly.
    $runningJobs = @()
    $results = New-Object System.Collections.Generic.List[object]
    $nextIndex = 1

    while ($nextIndex -le $TotalRequests -or $runningJobs.Count -gt 0) {
        while ($nextIndex -le $TotalRequests -and $runningJobs.Count -lt $Concurrency) {
            $arguments = @($Name, $nextIndex) + @($CommonArguments)
            $job = Start-Job -ScriptBlock $JobScript -ArgumentList $arguments
            $runningJobs += $job
            $nextIndex++
        }

        $completed = Wait-Job -Job $runningJobs -Any -Timeout 1

        if ($completed -eq $null) {
            continue
        }

        foreach ($job in @($completed)) {
            try {
                $jobResults = Receive-Job -Job $job
                foreach ($item in @($jobResults)) {
                    $results.Add($item) | Out-Null
                }
            }
            finally {
                Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
                $runningJobs = @($runningJobs | Where-Object { $_.Id -ne $job.Id })
            }
        }
    }

    $array = @($results)
    Write-WorkloadSummary -Name $Name -Results $array
    return $array
}

$stressRunId = "stress-{0:yyyyMMdd-HHmmss}-{1}" -f [DateTime]::UtcNow, ([Guid]::NewGuid().ToString("N").Substring(0, 8))
$reportRoot = Resolve-RepoPath -Path $ReportRootPath
$reportDirectory = Join-Path $reportRoot $stressRunId
New-Item -ItemType Directory -Force $reportDirectory | Out-Null

Write-Section "AgentRouter stress harness"
Write-Host "StressRunId:        $stressRunId"
Write-Host "RouterBaseUrl:      $RouterBaseUrl"
Write-Host "ChatModel:          $ChatModel"
Write-Host "ReportDirectory:    $reportDirectory"
Write-Host "TimeoutSeconds:     $RequestTimeoutSeconds"

Write-Section "Preflight"
$health = Invoke-PreflightGet -Name "AgentRouter /health" -Uri "$RouterBaseUrl/health"
$models = Invoke-PreflightGet -Name "AgentRouter /v1/models" -Uri "$RouterBaseUrl/v1/models"
$mcpTools = Invoke-PreflightGet -Name "AgentRouter /agent/mcp/tools" -Uri "$RouterBaseUrl/agent/mcp/tools"

if ($health -eq $null -or $models -eq $null -or $mcpTools -eq $null) {
    Write-Fail "Preflight failed. Stress run aborted."
    exit 1
}

Write-Host "MCP tool count: $($mcpTools.toolCount)"

$chatJob = {
    param(
        [string] $Workload,
        [int] $Index,
        [string] $RouterBaseUrl,
        [string] $ChatModel,
        [int] $RequestTimeoutSeconds
    )

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $statusCode = 0
    $success = $false
    $errorMessage = $null
    $contentPreview = $null

    try {
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

        $response = Invoke-RestMethod `
            -Uri "$RouterBaseUrl/v1/chat/completions" `
            -Method Post `
            -ContentType "application/json" `
            -Body $body `
            -TimeoutSec $RequestTimeoutSeconds

        $contentPreview = [string] $response.choices[0].message.content
        $statusCode = 200
        $success = $true
    }
    catch {
        $errorMessage = $_.Exception.Message
        if ($_.Exception.Response -ne $null) {
            $statusCode = $_.Exception.Response.StatusCode.value__
        }
    }
    finally {
        $stopwatch.Stop()
    }

    [PSCustomObject] @{
        Workload = $Workload
        Index = $Index
        Success = $success
        StatusCode = $statusCode
        ElapsedMilliseconds = $stopwatch.ElapsedMilliseconds
        Error = $errorMessage
        ContentPreview = $contentPreview
        StartedAtUtc = [DateTime]::UtcNow.ToString("O")
    }
}

$agentRunJob = {
    param(
        [string] $Workload,
        [int] $Index,
        [string] $RouterBaseUrl,
        [string] $ChatModel,
        [int] $RequestTimeoutSeconds
    )

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $statusCode = 0
    $success = $false
    $errorMessage = $null
    $contentPreview = $null
    $runId = $null

    try {
        $body = @{
            model = $ChatModel
            goal = "Reply with exactly: stress agent ok"
        } | ConvertTo-Json -Depth 20

        $response = Invoke-RestMethod `
            -Uri "$RouterBaseUrl/agent/runs" `
            -Method Post `
            -ContentType "application/json" `
            -Body $body `
            -TimeoutSec $RequestTimeoutSeconds

        $runId = [string] $response.id
        $contentPreview = [string] $response.result
        $statusCode = 200
        $success = ([string] $response.status) -eq "completed"

        if (-not $success) {
            $errorMessage = "Agent run status was '$($response.status)'."
        }
    }
    catch {
        $errorMessage = $_.Exception.Message
        if ($_.Exception.Response -ne $null) {
            $statusCode = $_.Exception.Response.StatusCode.value__
        }
    }
    finally {
        $stopwatch.Stop()
    }

    [PSCustomObject] @{
        Workload = $Workload
        Index = $Index
        Success = $success
        StatusCode = $statusCode
        ElapsedMilliseconds = $stopwatch.ElapsedMilliseconds
        Error = $errorMessage
        ContentPreview = $contentPreview
        RunId = $runId
        StartedAtUtc = [DateTime]::UtcNow.ToString("O")
    }
}

$mcpCatalogJob = {
    param(
        [string] $Workload,
        [int] $Index,
        [string] $RouterBaseUrl,
        [int] $RequestTimeoutSeconds
    )

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $statusCode = 0
    $success = $false
    $errorMessage = $null
    $contentPreview = $null

    try {
        $response = Invoke-RestMethod `
            -Uri "$RouterBaseUrl/agent/mcp/tools" `
            -Method Get `
            -TimeoutSec $RequestTimeoutSeconds

        $statusCode = 200
        $success = ([string] $response.status) -eq "ok" -and ([int] $response.toolCount) -gt 0
        $contentPreview = "toolCount=$($response.toolCount)"

        if (-not $success) {
            $errorMessage = "Unexpected MCP catalog status '$($response.status)' toolCount '$($response.toolCount)'."
        }
    }
    catch {
        $errorMessage = $_.Exception.Message
        if ($_.Exception.Response -ne $null) {
            $statusCode = $_.Exception.Response.StatusCode.value__
        }
    }
    finally {
        $stopwatch.Stop()
    }

    [PSCustomObject] @{
        Workload = $Workload
        Index = $Index
        Success = $success
        StatusCode = $statusCode
        ElapsedMilliseconds = $stopwatch.ElapsedMilliseconds
        Error = $errorMessage
        ContentPreview = $contentPreview
        StartedAtUtc = [DateTime]::UtcNow.ToString("O")
    }
}

$mcpToolCallJob = {
    param(
        [string] $Workload,
        [int] $Index,
        [string] $RouterBaseUrl,
        [int] $RequestTimeoutSeconds
    )

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $statusCode = 0
    $success = $false
    $errorMessage = $null
    $contentPreview = $null
    $traceId = $null

    try {
        $body = @{
            toolName = "fs.list_directory"
            arguments = @{
                path = "."
            }
        } | ConvertTo-Json -Depth 20

        $response = Invoke-RestMethod `
            -Uri "$RouterBaseUrl/agent/mcp/tools/call" `
            -Method Post `
            -ContentType "application/json" `
            -Body $body `
            -TimeoutSec $RequestTimeoutSeconds

        $statusCode = 200
        $traceId = [string] $response.traceId
        $success = ([string] $response.status) -eq "completed" -and ([bool] $response.allowed)
        $contentPreview = "traceId=$traceId elapsed=$($response.elapsedMilliseconds)ms"

        if (-not $success) {
            $errorMessage = "Unexpected tool call status '$($response.status)' allowed '$($response.allowed)'."
        }
    }
    catch {
        $errorMessage = $_.Exception.Message
        if ($_.Exception.Response -ne $null) {
            $statusCode = $_.Exception.Response.StatusCode.value__
        }
    }
    finally {
        $stopwatch.Stop()
    }

    [PSCustomObject] @{
        Workload = $Workload
        Index = $Index
        Success = $success
        StatusCode = $statusCode
        ElapsedMilliseconds = $stopwatch.ElapsedMilliseconds
        Error = $errorMessage
        ContentPreview = $contentPreview
        TraceId = $traceId
        StartedAtUtc = [DateTime]::UtcNow.ToString("O")
    }
}

if (-not $SkipMcpCatalog) {
    $results = Invoke-BoundedJobBatch `
        -Name "MCP catalog" `
        -TotalRequests $McpCatalogRequests `
        -Concurrency $McpCatalogConcurrency `
        -JobScript $mcpCatalogJob `
        -CommonArguments @($RouterBaseUrl, $RequestTimeoutSeconds)
    Add-Results -Results $results
}

if (-not $SkipMcpToolCalls) {
    $results = Invoke-BoundedJobBatch `
        -Name "MCP tool call" `
        -TotalRequests $McpToolCallRequests `
        -Concurrency $McpToolCallConcurrency `
        -JobScript $mcpToolCallJob `
        -CommonArguments @($RouterBaseUrl, $RequestTimeoutSeconds)
    Add-Results -Results $results
}

if (-not $SkipChat) {
    $results = Invoke-BoundedJobBatch `
        -Name "Chat completion" `
        -TotalRequests $ChatRequests `
        -Concurrency $ChatConcurrency `
        -JobScript $chatJob `
        -CommonArguments @($RouterBaseUrl, $ChatModel, $RequestTimeoutSeconds)
    Add-Results -Results $results
}

if (-not $SkipAgentRuns) {
    $results = Invoke-BoundedJobBatch `
        -Name "Agent run" `
        -TotalRequests $AgentRunRequests `
        -Concurrency $AgentRunConcurrency `
        -JobScript $agentRunJob `
        -CommonArguments @($RouterBaseUrl, $ChatModel, $RequestTimeoutSeconds)
    Add-Results -Results $results
}

Write-Section "Combined summary"
$all = @($script:AllResults)
$summary = @()

foreach ($group in ($all | Group-Object Workload)) {
    $latencies = @($group.Group | ForEach-Object { [double] $_.ElapsedMilliseconds })
    $successes = @($group.Group | Where-Object { $_.Success })
    $failures = @($group.Group | Where-Object { -not $_.Success })

    $summary += [PSCustomObject] @{
        Workload = $group.Name
        Total = $group.Count
        Success = $successes.Count
        Failed = $failures.Count
        AvgMs = if ($latencies.Count -gt 0) { [Math]::Round((($latencies | Measure-Object -Average).Average), 2) } else { 0 }
        P50Ms = Get-Percentile -Values $latencies -Percentile 50
        P95Ms = Get-Percentile -Values $latencies -Percentile 95
        MaxMs = if ($latencies.Count -gt 0) { [Math]::Round((($latencies | Measure-Object -Maximum).Maximum), 2) } else { 0 }
    }
}

$summary | Sort-Object Workload | Format-Table -AutoSize | Out-String | Write-Host

$resultsPath = Join-Path $reportDirectory "results.json"
$summaryPath = Join-Path $reportDirectory "summary.json"
$csvPath = Join-Path $reportDirectory "results.csv"
$readmePath = Join-Path $reportDirectory "README.txt"

$all | ConvertTo-Json -Depth 20 | Set-Content -Path $resultsPath -Encoding UTF8
$summary | ConvertTo-Json -Depth 20 | Set-Content -Path $summaryPath -Encoding UTF8
$all | Export-Csv -Path $csvPath -NoTypeInformation -Encoding UTF8

@"
AgentRouter stress run
Run id: $stressRunId
Router: $RouterBaseUrl
Model: $ChatModel
Created UTC: $([DateTime]::UtcNow.ToString("O"))

Files:
- results.json: per-request result data
- summary.json: workload aggregate data
- results.csv: per-request result data for spreadsheets
"@ | Set-Content -Path $readmePath -Encoding UTF8

Write-Host "Reports written:"
Write-Host "  $resultsPath"
Write-Host "  $summaryPath"
Write-Host "  $csvPath"

$totalFailures = @($all | Where-Object { -not $_.Success }).Count

Write-Section "Stress result"
if ($script:Failed -eq 0 -and $totalFailures -eq 0) {
    Write-Pass "Stress run completed with zero request failures."
    exit 0
}

Write-Fail "Stress run completed with $totalFailures request failures and $script:Failed failed checks."
exit 1
