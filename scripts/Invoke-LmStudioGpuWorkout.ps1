[CmdletBinding()]
param(
    [string]$ApiBaseUrl = 'http://127.0.0.1:1234',
    [string]$Model = 'cleanunicorn/qwen3-coder-30b-a3b-instruct',
    [string]$ApiToken,
    [int]$ContextLength = 49152,
    [int]$EvalBatchSize = 2048,
    [int]$MaxOutputTokens = 4096,
    [ValidateSet('auto', 'off', 'low', 'medium', 'high', 'on')]
    [string]$Reasoning = 'auto',
    [int]$ParallelSessions = 2,
    [int]$WorkerCount = 1,
    [int]$TurnsPerWorker = 1,
    [int]$PromptRepeatCount = 300,
    [switch]$UnloadModel
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-CompactJson {
    param([Parameter(Mandatory = $true)]$Value)

    return ($Value | ConvertTo-Json -Depth 50 -Compress)
}

function New-LmStudioHeaders {
    param([string]$Token)

    $headers = @{
        Accept = 'application/json'
    }

    if (-not [string]::IsNullOrWhiteSpace($Token)) {
        $headers.Authorization = "Bearer $Token"
    }

    return $headers
}

function Invoke-LmStudioJsonRequest {
    param(
        [Parameter(Mandatory = $true)][string]$BaseUrl,
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][ValidateSet('Get', 'Post')][string]$Method,
        [Parameter(Mandatory = $true)]$Body,
        [string]$Token
    )

    $uri = ($BaseUrl.TrimEnd('/') + $Path)
    $headers = New-LmStudioHeaders -Token $Token
    if ($Method -eq 'Get') {
        return Invoke-RestMethod -Uri $uri -Method $Method -Headers $headers -TimeoutSec 900
    }

    $json = ConvertTo-CompactJson -Value $Body
    return Invoke-RestMethod -Uri $uri -Method $Method -Headers $headers -ContentType 'application/json' -Body $json -TimeoutSec 900
}

function Get-LmStudioModels {
    param(
        [Parameter(Mandatory = $true)][string]$BaseUrl,
        [string]$Token
    )

    $response = Invoke-LmStudioJsonRequest -BaseUrl $BaseUrl -Path '/api/v1/models' -Method Get -Body @{} -Token $Token

    if ($response.PSObject.Properties.Name -contains 'models') {
        return @($response.models)
    }

    if ($response.PSObject.Properties.Name -contains 'data') {
        return @($response.data)
    }

    return @($response)
}

function Load-LmStudioModel {
    param(
        [Parameter(Mandatory = $true)][string]$BaseUrl,
        [Parameter(Mandatory = $true)][string]$ModelName,
        [Parameter(Mandatory = $true)][int]$ContextLength,
        [Parameter(Mandatory = $true)][int]$EvalBatchSize,
        [Parameter(Mandatory = $true)][int]$ParallelSessions,
        [Parameter(Mandatory = $true)][string]$Reasoning,
        [string]$Token
    )

    return Invoke-LmStudioJsonRequest -BaseUrl $BaseUrl -Path '/api/v1/models/load' -Method Post -Token $Token -Body @{
        model = $ModelName
        context_length = $ContextLength
        eval_batch_size = $EvalBatchSize
        parallel = $ParallelSessions
        flash_attention = $true
        offload_kv_cache_to_gpu = $true
        echo_load_config = $true
    }
}

function Invoke-LmStudioChat {
    param(
        [Parameter(Mandatory = $true)][string]$BaseUrl,
        [Parameter(Mandatory = $true)][string]$ModelName,
        [Parameter(Mandatory = $true)][string]$Input,
        [Parameter(Mandatory = $true)][int]$ContextLength,
        [Parameter(Mandatory = $true)][int]$MaxOutputTokens,
        [Parameter(Mandatory = $true)][string]$Reasoning,
        [string]$PreviousResponseId,
        [string]$Token
    )

    $body = @{
        model = $ModelName
        input = $Input
        context_length = $ContextLength
        max_output_tokens = $MaxOutputTokens
        reasoning = $Reasoning
        stream = $false
        store = $true
    }

    if (-not [string]::IsNullOrWhiteSpace($PreviousResponseId)) {
        $body.previous_response_id = $PreviousResponseId
    }

    return Invoke-LmStudioJsonRequest -BaseUrl $BaseUrl -Path '/api/v1/chat' -Method Post -Body $body -Token $Token
}

function New-StressPrompt {
    param(
        [Parameter(Mandatory = $true)][int]$WorkerId,
        [Parameter(Mandatory = $true)][int]$RepeatCount
    )

    $block = @"
You are generating a dense technical report for a GPU stress test.
Focus on throughput, memory pressure, token economics, attention cost, KV cache behavior, and tradeoffs in local inference.
Use explicit headings, bullet lists, tables, and concrete examples.
Do not be brief.
"@

    $segments = for ($i = 1; $i -le $RepeatCount; $i++) {
        "Worker $WorkerId segment $i of $RepeatCount`n$block"
    }

    return @"
Write a long-form report titled `GPU Workout Run $WorkerId`.

Requirements:
- Explain why a large local model can stress a GPU even without training.
- Discuss prompt processing, KV cache growth, and generation throughput.
- Include a short comparison of context length versus output length.
- End with a practical checklist for running repeatable inference stress tests.

$($segments -join "`n`n")
"@
}

function Resolve-ReasoningMode {
    param(
        [Parameter(Mandatory = $true)]$ModelMetadata,
        [Parameter(Mandatory = $true)][string]$RequestedMode
    )

    if ($RequestedMode -ne 'auto') {
        return $RequestedMode
    }

    if ($ModelMetadata.PSObject.Properties.Name -notcontains 'capabilities') {
        return 'off'
    }

    $capabilities = $ModelMetadata.capabilities
    if ($capabilities.PSObject.Properties.Name -notcontains 'reasoning') {
        return 'off'
    }

    $reasoning = $capabilities.reasoning
    $allowedOptions = @()
    if ($reasoning.PSObject.Properties.Name -contains 'allowed_options') {
        $allowedOptions = @($reasoning.allowed_options)
    }

    foreach ($candidate in @('high', 'on', 'medium', 'low', 'off')) {
        if ($allowedOptions -contains $candidate) {
            return $candidate
        }
    }

    if ($reasoning.PSObject.Properties.Name -contains 'default' -and -not [string]::IsNullOrWhiteSpace([string]$reasoning.default)) {
        return [string]$reasoning.default
    }

    return 'off'
}

Write-Host "Querying LM Studio models at $ApiBaseUrl"
$models = Get-LmStudioModels -BaseUrl $ApiBaseUrl -Token $ApiToken
$modelList = @($models)
if ($null -eq $modelList -or $modelList.Count -eq 0) {
    throw "No models were returned from $ApiBaseUrl/api/v1/models"
}

if ([string]::IsNullOrWhiteSpace($Model)) {
    $firstModel = $modelList[0]
    $Model = if ($firstModel.PSObject.Properties.Name -contains 'key') {
        [string]$firstModel.key
    }
    elseif ($firstModel.PSObject.Properties.Name -contains 'id') {
        [string]$firstModel.id
    }
    else {
        [string]$firstModel
    }
}

$selectedModel = $modelList | Where-Object {
    ([string]$_.key -eq $Model) -or ([string]$_.id -eq $Model)
} | Select-Object -First 1

if ($null -eq $selectedModel) {
    throw "Model '$Model' was not found in LM Studio's model list."
}

$modelMaxContextLength = if ($selectedModel.PSObject.Properties.Name -contains 'max_context_length') {
    [int]$selectedModel.max_context_length
} else {
    $ContextLength
}

if ($ContextLength -gt $modelMaxContextLength) {
    Write-Warning "Requested context length $ContextLength exceeds model max $modelMaxContextLength. Clamping to the model maximum."
    $ContextLength = $modelMaxContextLength
}

$EffectiveReasoning = Resolve-ReasoningMode -ModelMetadata $selectedModel -RequestedMode $Reasoning
Write-Host "Model max context length $modelMaxContextLength"
Write-Host "Using reasoning mode $EffectiveReasoning"

Write-Host "Loading model $Model with context_length=$ContextLength eval_batch_size=$EvalBatchSize parallel=$ParallelSessions"
$loadResponse = Load-LmStudioModel -BaseUrl $ApiBaseUrl -ModelName $Model -ContextLength $ContextLength -EvalBatchSize $EvalBatchSize -ParallelSessions $ParallelSessions -Reasoning $Reasoning -Token $ApiToken
if ($null -eq $loadResponse.instance_id) {
    throw 'Model load did not return an instance_id.'
}

$instanceId = [string]$loadResponse.instance_id
Write-Host "Loaded model instance $instanceId"

$jobScript = {
    param(
        [string]$BaseUrl,
        [string]$ModelName,
        [string]$Token,
        [int]$ContextLength,
        [int]$MaxOutputTokens,
        [int]$ParallelSessions,
        [string]$Reasoning,
        [int]$PromptRepeatCount,
        [int]$TurnsPerWorker,
        [int]$WorkerId
    )

    Set-StrictMode -Version Latest
    $ErrorActionPreference = 'Stop'

    function ConvertTo-CompactJson {
        param([Parameter(Mandatory = $true)]$Value)
        return ($Value | ConvertTo-Json -Depth 50 -Compress)
    }

    function Invoke-LmStudioJsonRequest {
        param(
            [Parameter(Mandatory = $true)][string]$BaseUrlInner,
            [Parameter(Mandatory = $true)][string]$Path,
            [Parameter(Mandatory = $true)][ValidateSet('Get', 'Post')][string]$Method,
            [Parameter(Mandatory = $true)]$Body,
            [string]$TokenInner
        )

        $headers = @{
            Accept = 'application/json'
        }

        if (-not [string]::IsNullOrWhiteSpace($TokenInner)) {
            $headers.Authorization = "Bearer $TokenInner"
        }

        $uri = ($BaseUrlInner.TrimEnd('/') + $Path)
        return Invoke-RestMethod -Uri $uri -Method $Method -Headers $headers -ContentType 'application/json' -Body (ConvertTo-CompactJson -Value $Body) -TimeoutSec 900
    }

    function New-StressPrompt {
        param(
            [Parameter(Mandatory = $true)][int]$WorkerIdInner,
            [Parameter(Mandatory = $true)][int]$RepeatCountInner
        )

        $block = @"
You are generating a dense technical report for a GPU stress test.
Focus on throughput, memory pressure, token economics, attention cost, KV cache behavior, and tradeoffs in local inference.
Use explicit headings, bullet lists, tables, and concrete examples.
Do not be brief.
"@

        $segments = for ($i = 1; $i -le $RepeatCountInner; $i++) {
            "Worker $WorkerIdInner segment $i of $RepeatCountInner`n$block"
        }

        return @"
Write a long-form report titled `GPU Workout Run $WorkerIdInner`.

Requirements:
- Explain why a large local model can stress a GPU even without training.
- Discuss prompt processing, KV cache growth, and generation throughput.
- Include a short comparison of context length versus output length.
- End with a practical checklist for running repeatable inference stress tests.

$($segments -join "`n`n")
"@
    }

    $prompt = New-StressPrompt -WorkerIdInner $WorkerId -RepeatCountInner $PromptRepeatCount
    $responseId = $null
    $turns = New-Object System.Collections.Generic.List[object]

    for ($turn = 1; $turn -le $TurnsPerWorker; $turn++) {
        $requestText = if ($turn -eq 1) {
            $prompt
        }
        else {
            "Continue with a more technical second pass and keep the same report structure. Expand the analysis instead of repeating earlier points."
        }

        $requestStart = [DateTimeOffset]::UtcNow
        $body = @{
            model = $ModelName
            input = $requestText
            context_length = $ContextLength
            max_output_tokens = $MaxOutputTokens
            stream = $false
            store = $true
        }

        if ($Reasoning -ne 'off') {
            $body.reasoning = $Reasoning
        }

        if (-not [string]::IsNullOrWhiteSpace($responseId)) {
            $body.previous_response_id = $responseId
        }

        $response = Invoke-LmStudioJsonRequest -BaseUrlInner $BaseUrl -Path '/api/v1/chat' -Method Post -TokenInner $Token -Body $body
        $elapsedSeconds = ([DateTimeOffset]::UtcNow - $requestStart).TotalSeconds
        $responseId = [string]$response.response_id

        $messageText = @(
            foreach ($item in @($response.output)) {
                if ([string]$item.type -eq 'message' -and -not [string]::IsNullOrWhiteSpace([string]$item.content)) {
                    [string]$item.content
                }
            }
        ) -join "`n"

        $turns.Add([pscustomobject]@{
            Worker = $WorkerId
            Turn = $turn
            ResponseId = $responseId
            ElapsedSeconds = [Math]::Round($elapsedSeconds, 2)
            InputTokens = $response.stats.input_tokens
            OutputTokens = $response.stats.total_output_tokens
            TokensPerSecond = $response.stats.tokens_per_second
            TimeToFirstTokenSeconds = $response.stats.time_to_first_token_seconds
            Summary = if ([string]::IsNullOrWhiteSpace($messageText)) {
                ''
            }
            else {
                $messageText.Substring(0, [Math]::Min($messageText.Length, 220))
            }
        })
    }

    [pscustomobject]@{
        Worker = $WorkerId
        Turns = $turns
    }
}

$results = @()
if ($WorkerCount -eq 1) {
    $results = @(
        & $jobScript `
            -BaseUrl $ApiBaseUrl `
            -ModelName $Model `
            -Token $ApiToken `
            -ContextLength $ContextLength `
            -MaxOutputTokens $MaxOutputTokens `
            -ParallelSessions $ParallelSessions `
            -Reasoning $EffectiveReasoning `
            -PromptRepeatCount $PromptRepeatCount `
            -TurnsPerWorker $TurnsPerWorker `
            -WorkerId 1
    )
} else {
    $jobs = @()
    for ($workerId = 1; $workerId -le $WorkerCount; $workerId++) {
        $jobs += Start-Job -ScriptBlock $jobScript -ArgumentList $ApiBaseUrl, $Model, $ApiToken, $ContextLength, $MaxOutputTokens, $ParallelSessions, $EffectiveReasoning, $PromptRepeatCount, $TurnsPerWorker, $workerId
    }

    Wait-Job -Job $jobs | Out-Null
    $results = Receive-Job -Job $jobs
    Remove-Job -Job $jobs -Force
}

$flattenedTurns = @(
    foreach ($workerResult in $results) {
        foreach ($turn in @($workerResult.Turns)) {
            $turn
        }
    }
)

$flattenedTurns | Sort-Object Worker, Turn | Format-Table -AutoSize | Out-String | Write-Host

if ($UnloadModel) {
    Write-Host "Unloading model instance $instanceId"
    try {
        Invoke-LmStudioJsonRequest -BaseUrl $ApiBaseUrl -Path '/api/v1/models/unload' -Method Post -Token $ApiToken -Body @{
            instance_id = $instanceId
        } | Out-Null
    }
    catch {
        Write-Warning "Model unload request failed: $($_.Exception.Message)"
    }
}

Write-Host "GPU workout complete."
