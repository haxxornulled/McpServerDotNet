[CmdletBinding()]
param(
    [string]$InferenceBaseUrl = 'http://127.0.0.1:1234',
    [string]$Model,
    [string]$ScenarioPath = '',
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [string]$HostExecutablePath = '',
    [string]$HostProjectPath = '',
    [string[]]$IncludeTool,
    [string[]]$SkipTool,
    [int]$MaxConversationTurns = 4,
    [int]$InferenceTimeoutSeconds = 180,
    [int]$McpTimeoutSeconds = 60,
    [string]$ResultPath,
    [switch]$AllowMissingScenarios,
    [switch]$ListToolsOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-FramedMessage {
    param(
        [Parameter(Mandatory = $true)] [System.IO.Stream] $Stream,
        [Parameter(Mandatory = $true)] [string] $Json
    )

    $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($Json)
    $headerBytes = [System.Text.Encoding]::ASCII.GetBytes("Content-Length: $($bodyBytes.Length)`r`n`r`n")
    $Stream.Write($headerBytes, 0, $headerBytes.Length)
    $Stream.Write($bodyBytes, 0, $bodyBytes.Length)
    $Stream.Flush()
}

function Read-LineFromStream {
    param(
        [Parameter(Mandatory = $true)] [System.IO.Stream] $Stream
    )

    $bytes = [System.Collections.Generic.List[byte]]::new()
    while ($true) {
        $value = $Stream.ReadByte()
        if ($value -lt 0) {
            if ($bytes.Count -eq 0) { return $null }
            break
        }

        if ($value -eq 10) { break }
        [void]$bytes.Add([byte]$value)
    }

    if ($bytes.Count -gt 0 -and $bytes[$bytes.Count - 1] -eq 13) {
        $bytes.RemoveAt($bytes.Count - 1)
    }

    return [System.Text.Encoding]::UTF8.GetString($bytes.ToArray())
}

function Read-JsonRpcMessage {
    param(
        [Parameter(Mandatory = $true)] [System.IO.Stream] $Stream
    )

    $firstLine = Read-LineFromStream -Stream $Stream
    if ($null -eq $firstLine) { return $null }

    if ($firstLine.StartsWith('Content-Length:', [System.StringComparison]::OrdinalIgnoreCase)) {
        $lengthText = $firstLine.Substring('Content-Length:'.Length).Trim()
        $contentLength = [int]::Parse($lengthText, [System.Globalization.CultureInfo]::InvariantCulture)

        while ($true) {
            $headerLine = Read-LineFromStream -Stream $Stream
            if ($null -eq $headerLine -or $headerLine.Length -eq 0) { break }
        }

        $buffer = [byte[]]::new($contentLength)
        $offset = 0
        while ($offset -lt $contentLength) {
            $read = $Stream.Read($buffer, $offset, $contentLength - $offset)
            if ($read -le 0) { throw 'Server closed stdout while reading framed response body.' }
            $offset += $read
        }

        return [System.Text.Encoding]::UTF8.GetString($buffer)
    }

    return $firstLine
}

function Send-RequestAndReadResponse {
    param(
        [Parameter(Mandatory = $true)] [System.IO.Stream] $InputStream,
        [Parameter(Mandatory = $true)] [System.IO.Stream] $OutputStream,
        [Parameter(Mandatory = $true)] [string] $Json
    )

    Write-FramedMessage -Stream $InputStream -Json $Json

    while ($true) {
        $message = Read-JsonRpcMessage -Stream $OutputStream
        if ($null -eq $message) { return $null }

        $document = $message | ConvertFrom-Json
        if ($null -ne $document.id) { return $document }
    }
}

$scriptRoot = if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
    $PSScriptRoot
}
elseif (-not [string]::IsNullOrWhiteSpace($PSCommandPath)) {
    Split-Path -Parent $PSCommandPath
}
else {
    (Get-Location).Path
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot '..'))

if ([string]::IsNullOrWhiteSpace($ScenarioPath)) {
    $ScenarioPath = Join-Path $scriptRoot 'inference-tool-scenarios.json'
}
elseif (-not [System.IO.Path]::IsPathRooted($ScenarioPath)) {
    $ScenarioPath = Join-Path $scriptRoot $ScenarioPath
}

if ([string]::IsNullOrWhiteSpace($HostExecutablePath)) {
    $HostExecutablePath = Join-Path $scriptRoot '../src/McpServer.Host/bin/Release/net10.0/McpServer.Host.exe'
}
elseif (-not [System.IO.Path]::IsPathRooted($HostExecutablePath)) {
    $HostExecutablePath = Join-Path $scriptRoot $HostExecutablePath
}

if ([string]::IsNullOrWhiteSpace($HostProjectPath)) {
    $HostProjectPath = Join-Path $scriptRoot '../src/McpServer.Host/McpServer.Host.csproj'
}
elseif (-not [System.IO.Path]::IsPathRooted($HostProjectPath)) {
    $HostProjectPath = Join-Path $scriptRoot $HostProjectPath
}

function Resolve-AbsolutePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction SilentlyContinue
    if ($null -ne $resolved) {
        return $resolved.Path
    }

    return [System.IO.Path]::GetFullPath($Path)
}

function Start-McpServerProcess {
    param(
        [Parameter(Mandatory = $true)][string]$ExecutablePath,
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$ConfigurationName
    )

    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $resolvedExecutable = Resolve-AbsolutePath -Path $ExecutablePath
    $resolvedProject = Resolve-AbsolutePath -Path $ProjectPath

    if (Test-Path -LiteralPath $resolvedExecutable) {
        $psi.FileName = $resolvedExecutable
        $psi.Arguments = ''
        Write-Host "Starting MCP host executable $resolvedExecutable"
    }
    else {
        $psi.FileName = 'dotnet'
        $psi.Arguments = "run --project `"$resolvedProject`" -c $ConfigurationName --no-build"
        Write-Host "Starting MCP host via dotnet run for $resolvedProject"
    }

    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.WorkingDirectory = $repoRoot
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development"
    $psi.Environment["MCPSERVER__WORKSPACE__ROOTPATH"] = $repoRoot
    $psi.Environment["MCPSERVER__WORKSPACE__ALLOWEDROOTS__0"] = $repoRoot
    $psi.Environment["MCPSERVER__WORKSPACE__ALLOWRUNTIMEWORKSPACEOPEN"] = "true"
    $psi.Environment["MCPSERVER__SHELL__ENABLED"] = "false"
    $psi.Environment["MCPSERVER__WEBACCESS__ENABLED"] = "false"
    $psi.Environment["MCPSERVER__SSH__ENABLED"] = "false"

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $psi

    if (-not $process.Start()) {
        throw 'Failed to start MCP server process.'
    }

    Start-Sleep -Milliseconds 250

    if ($process.HasExited) {
        throw 'MCP server exited during startup.'
    }

    return $process
}

function Stop-McpServerProcess {
    param([Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process)

    if (-not $Process.HasExited) {
        try {
            $Process.Kill($true)
        }
        catch {
            $Process.Kill()
        }
        $Process.WaitForExit()
    }

    $Process.Dispose()
}

function ConvertTo-CompactJson {
    param([Parameter(Mandatory = $true)]$Value)

    return ($Value | ConvertTo-Json -Depth 50 -Compress)
}

function Send-McpMessage {
    param(
        [Parameter(Mandatory = $true)][System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)]$Payload,
        [int]$TimeoutSeconds = 15,
        [switch]$Notification
    )

    $json = ConvertTo-CompactJson -Value $Payload
    if ($json.Contains("`n") -or $json.Contains("`r")) {
        throw 'Serialized MCP message must not contain newlines.'
    }

    Write-FramedMessage -Stream $Process.StandardInput.BaseStream -Json $json

    if ($Notification) {
        return $null
    }

    $expectedId = $null
    if ($Payload -is [System.Collections.IDictionary] -and $Payload.Contains('id')) {
        $expectedId = $Payload['id']
    }
    elseif ($Payload.PSObject.Properties.Name -contains 'id') {
        $expectedId = $Payload.id
    }

    while ($true) {
        $messageJson = Read-JsonRpcMessage -Stream $Process.StandardOutput.BaseStream
        if ($null -eq $messageJson) {
            throw 'Received end of stream while waiting for MCP response.'
        }

        $message = $messageJson | ConvertFrom-Json
        if ($null -eq $expectedId) {
            return $message
        }

        if ($message.PSObject.Properties.Name -contains 'id' -and $message.id -eq $expectedId) {
            return $message
        }
    }
}

function Get-ModelList {
    param([Parameter(Mandatory = $true)][string]$BaseUrl)

    $uri = "$($BaseUrl.TrimEnd('/'))/v1/models"
    $response = Invoke-RestMethod -Uri $uri -Method Get -TimeoutSec 15 -Headers @{ Accept = 'application/json' }
    if ($null -eq $response.data -or $response.data.Count -eq 0) {
        throw "No models were returned from $uri"
    }

    return $response.data
}

function Invoke-ChatCompletion {
    param(
        [Parameter(Mandatory = $true)][string]$BaseUrl,
        [Parameter(Mandatory = $true)][string]$ModelName,
        [Parameter(Mandatory = $true)][object[]]$Messages,
        [Parameter(Mandatory = $true)][object[]]$Tools,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds,
        [string]$ToolName
    )

    $uri = "$($BaseUrl.TrimEnd('/'))/v1/chat/completions"
    $body = @{
        model = $ModelName
        messages = $Messages
        tools = $Tools
        temperature = 0
    }

    if (-not [string]::IsNullOrWhiteSpace($ToolName)) {
        $body.tool_choice = @{
            type = 'function'
            function = @{ name = $ToolName }
        }
    }

    try {
        return Invoke-RestMethod -Uri $uri -Method Post -TimeoutSec $TimeoutSeconds -ContentType 'application/json' -Body (ConvertTo-CompactJson -Value $body)
    }
    catch {
        if ([string]::IsNullOrWhiteSpace($ToolName)) {
            throw
        }

        $body.Remove('tool_choice')
        return Invoke-RestMethod -Uri $uri -Method Post -TimeoutSec $TimeoutSeconds -ContentType 'application/json' -Body (ConvertTo-CompactJson -Value $body)
    }
}

function Convert-McpToolToOpenAiTool {
    param([Parameter(Mandatory = $true)]$Tool)

    return @{
        type = 'function'
        function = @{
            name = $Tool.name
            description = $Tool.description
            parameters = $Tool.inputSchema
        }
    }
}

function Get-ScenarioProperty {
    param(
        [Parameter(Mandatory = $true)]$Scenario,
        [Parameter(Mandatory = $true)][string]$PropertyName,
        $DefaultValue = $null
    )

    if ($Scenario.PSObject.Properties.Name -contains $PropertyName) {
        return $Scenario.$PropertyName
    }

    return $DefaultValue
}

function Get-ObjectPropertyValue {
    param(
        [Parameter(Mandatory = $true)]$InputObject,
        [Parameter(Mandatory = $true)][string]$PropertyName
    )

    if ($null -eq $InputObject) {
        return $null
    }

    $property = $InputObject.PSObject.Properties[$PropertyName]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Get-McpErrorText {
    param([Parameter(Mandatory = $true)]$Payload)

    if ($Payload.PSObject.Properties.Name -notcontains 'error' -or $null -eq $Payload.error) {
        return $null
    }

    $code = $null
    $message = $null

    if ($Payload.error.PSObject.Properties.Name -contains 'code') {
        $code = $Payload.error.code
    }

    if ($Payload.error.PSObject.Properties.Name -contains 'message') {
        $message = $Payload.error.message
    }

    return "MCP error" + $(if ($null -ne $code) { " $code" } else { '' }) + $(if (-not [string]::IsNullOrWhiteSpace([string]$message)) { ": $message" } else { '' })
}

function Write-ScenarioStatus {
    param(
        [Parameter(Mandatory = $true)][string]$ToolName,
        [Parameter(Mandatory = $true)][string]$Status,
        [Parameter(Mandatory = $true)][string]$Detail
    )

    $line = "[{0}] {1}: {2}" -f $ToolName, $Status, $Detail
    Write-Output $line
}

function Get-ToolResultText {
    param([Parameter(Mandatory = $true)]$ToolCallResult)

    $result = Get-ObjectPropertyValue -InputObject $ToolCallResult -PropertyName 'result'
    if ($null -eq $result) {
        return ConvertTo-CompactJson -Value $ToolCallResult
    }

    return ConvertTo-CompactJson -Value $result
}

function Test-ScenarioExpectations {
    param(
        [Parameter(Mandatory = $true)]$Scenario,
        [Parameter(Mandatory = $true)]$ToolCallResult,
        [string]$AssistantText
    )

    $serializedToolResult = Get-ToolResultText -ToolCallResult $ToolCallResult
    $isError = $false
    $result = Get-ObjectPropertyValue -InputObject $ToolCallResult -PropertyName 'result'
    if ($null -ne $result) {
        $isErrorValue = Get-ObjectPropertyValue -InputObject $result -PropertyName 'isError'
        if ($null -ne $isErrorValue) {
            $isError = [bool]$isErrorValue
        }
    }

    $expectedError = $false
    $expectToolError = Get-ScenarioProperty -Scenario $Scenario -PropertyName 'expectToolError'
    if ($null -ne $expectToolError) {
        $expectedError = [bool]$expectToolError
    }

    if ($isError -ne $expectedError) {
        throw "Expected tool error state '$expectedError' but received '$isError'. Result: $serializedToolResult"
    }

    $toolResultContains = Get-ScenarioProperty -Scenario $Scenario -PropertyName 'toolResultContains'
    if ($null -ne $toolResultContains) {
        foreach ($fragment in $toolResultContains) {
            if (-not $serializedToolResult.Contains([string]$fragment, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Expected tool result to contain '$fragment'. Result: $serializedToolResult"
            }
        }
    }

    $finalAssistantContains = Get-ScenarioProperty -Scenario $Scenario -PropertyName 'finalAssistantContains'
    if ($null -ne $finalAssistantContains) {
        foreach ($fragment in $finalAssistantContains) {
            if (-not $AssistantText.Contains([string]$fragment, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Expected final assistant response to contain '$fragment'. Response: $AssistantText"
            }
        }
    }
}

$resolvedScenarioPath = Resolve-AbsolutePath -Path $ScenarioPath
if (-not (Test-Path -LiteralPath $resolvedScenarioPath)) {
    throw "Scenario file was not found: $resolvedScenarioPath"
}

$resolvedResultPath = $null
if (-not [string]::IsNullOrWhiteSpace($ResultPath)) {
    $resolvedResultPath = Resolve-AbsolutePath -Path $ResultPath
}

$scenarios = Get-Content -LiteralPath $resolvedScenarioPath -Raw | ConvertFrom-Json
if ($null -eq $scenarios -or $scenarios.Count -eq 0) {
    throw "Scenario file did not contain any scenarios: $resolvedScenarioPath"
}

$process = Start-McpServerProcess -ExecutablePath $HostExecutablePath -ProjectPath $HostProjectPath -ConfigurationName $Configuration

try {
    $initializeResponse = Send-McpMessage -Process $process -TimeoutSeconds $McpTimeoutSeconds -Payload @{
        jsonrpc = '2.0'
        id = 1
        method = 'initialize'
        params = @{
            protocolVersion = '2025-03-26'
            capabilities = @{}
            clientInfo = @{
                name = 'inference-tool-smoke'
                version = '1.0.0'
            }
        }
    }

    if ($null -eq (Get-ObjectPropertyValue -InputObject $initializeResponse -PropertyName 'result')) {
        throw 'Initialize did not return a result.'
    }

    Send-McpMessage -Process $process -Notification -Payload @{
        jsonrpc = '2.0'
        method = 'notifications/initialized'
    } | Out-Null

    $toolListResponse = Send-McpMessage -Process $process -TimeoutSeconds $McpTimeoutSeconds -Payload @{
        jsonrpc = '2.0'
        id = 2
        method = 'tools/list'
        params = @{}
    }

    $toolListResult = Get-ObjectPropertyValue -InputObject $toolListResponse -PropertyName 'result'
    if ($null -eq $toolListResult -or $null -eq (Get-ObjectPropertyValue -InputObject $toolListResult -PropertyName 'tools')) {
        throw 'tools/list did not return any tools.'
    }

    $registeredTools = @(Get-ObjectPropertyValue -InputObject $toolListResult -PropertyName 'tools')
    $registeredToolNames = @($registeredTools | ForEach-Object { [string]$_.name })
    Write-Host ('Registered tools: ' + ($registeredToolNames -join ', '))

    if ($ListToolsOnly) {
        return
    }

    $scenarioByTool = @{}
    $scenarioOrder = New-Object System.Collections.Generic.List[string]
    foreach ($scenario in $scenarios) {
        $toolName = [string]$scenario.tool
        if (-not $scenarioByTool.ContainsKey($toolName)) {
            $scenarioByTool[$toolName] = New-Object System.Collections.Generic.List[object]
            $scenarioOrder.Add($toolName)
        }

        $scenarioByTool[$toolName].Add($scenario)
    }

    $registeredToolByName = @{}
    foreach ($registeredTool in $registeredTools) {
        $registeredToolByName[[string]$registeredTool.name] = $registeredTool
    }

    $includeNames = New-Object System.Collections.Generic.List[string]
    foreach ($name in @($IncludeTool)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$name)) {
            $includeNames.Add([string]$name)
        }
    }

    $skipNames = New-Object System.Collections.Generic.List[string]
    foreach ($name in @($SkipTool)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$name)) {
            $skipNames.Add([string]$name)
        }
    }

    $selectedTools = @()
    if ($includeNames.Count -gt 0) {
        $includeSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($name in $includeNames) {
            [void]$includeSet.Add($name)
        }

        foreach ($name in $includeNames) {
            if ($registeredToolByName.ContainsKey([string]$name)) {
                $selectedTools += $registeredToolByName[[string]$name]
            }
        }
    }
    else {
        foreach ($toolName in $scenarioOrder) {
            if ($registeredToolByName.ContainsKey($toolName)) {
                $selectedTools += $registeredToolByName[$toolName]
            }
        }

        foreach ($registeredTool in $registeredTools) {
            $toolName = [string]$registeredTool.name
            if ($scenarioOrder -notcontains $toolName) {
                $selectedTools += $registeredTool
            }
        }
    }

    if ($skipNames.Count -gt 0) {
        $skipSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($name in $skipNames) {
            [void]$skipSet.Add($name)
        }

        $selectedTools = @($selectedTools | Where-Object { -not $skipSet.Contains([string]$_.name) })
    }

    Write-Host ('Selected tools: ' + (($selectedTools | ForEach-Object { [string]$_.name }) -join ', '))

    $missingScenarios = @()
    foreach ($tool in $selectedTools) {
        if (-not $scenarioByTool.ContainsKey([string]$tool.name)) {
            $missingScenarios += [string]$tool.name
        }
    }

    if ($missingScenarios.Count -gt 0 -and -not $AllowMissingScenarios) {
        throw ('No inference scenarios were defined for registered tools: ' + ($missingScenarios -join ', '))
    }

    $models = Get-ModelList -BaseUrl $InferenceBaseUrl
    if ([string]::IsNullOrWhiteSpace($Model)) {
        $Model = [string]$models[0].id
    }

    Write-Host "Using model $Model against $InferenceBaseUrl"

    $results = New-Object System.Collections.Generic.List[object]
    $resultByTool = @{}
    $nextRequestId = 100

    function Add-ToolResult {
        param(
            [Parameter(Mandatory = $true)][string]$ToolName,
            [Parameter(Mandatory = $true)]$Result
        )

        if (-not $resultByTool.ContainsKey($ToolName)) {
            $resultByTool[$ToolName] = New-Object System.Collections.Generic.List[object]
        }

        $resultByTool[$ToolName].Add($Result)
    }

    foreach ($tool in $selectedTools) {
        $toolName = [string]$tool.name
        if (-not $scenarioByTool.ContainsKey($toolName)) {
            $results.Add([pscustomobject]@{
                Tool = $toolName
                Status = 'Skipped'
                Detail = 'No scenario defined.'
            })
            continue
        }

        $scenarioEntries = $scenarioByTool[$toolName]
        for ($scenarioIndex = 0; $scenarioIndex -lt $scenarioEntries.Count; $scenarioIndex++) {
            $scenario = $scenarioEntries[$scenarioIndex]
            $scenarioLabel = if ($scenarioEntries.Count -gt 1) {
                "$toolName ($($scenarioIndex + 1)/$($scenarioEntries.Count))"
            }
            else {
                $toolName
            }

            Write-Host "Running inference scenario for $scenarioLabel"

            $dependencyNames = @(Get-ScenarioProperty -Scenario $scenario -PropertyName 'dependsOn' -DefaultValue @())
            $blockingDependencies = @(
                foreach ($dependencyName in $dependencyNames) {
                    $dependencyResults = @($results.ToArray() | Where-Object { [string]$_.Tool -eq [string]$dependencyName })
                    if ($dependencyResults.Count -eq 0) {
                        [string]$dependencyName
                        continue
                    }

                    if ($dependencyResults | Where-Object { $_.Status -eq 'Failed' -or $_.Status -eq 'Skipped' }) {
                        [string]$dependencyName
                    }
                }
            )

            if ($blockingDependencies.Count -gt 0) {
                $dependencyDetail = 'Skipped because required scenario(s) did not pass: ' + ($blockingDependencies -join ', ')
                $skippedResult = [pscustomobject]@{
                    Tool = $toolName
                    Status = 'Skipped'
                    Detail = $dependencyDetail
                    Scenario = [string]$scenario.prompt
                }
                $results.Add($skippedResult)
                Add-ToolResult -ToolName $toolName -Result $skippedResult
                Write-ScenarioStatus -ToolName $scenarioLabel -Status 'Skipped' -Detail $dependencyDetail
                continue
            }

            try {
                $messages = @(
                    @{
                        role = 'system'
                        content = 'You are validating MCP tool integration. Call the provided tool with the exact arguments requested by the user. After the tool result is returned, summarize the outcome in one short sentence.'
                    },
                    @{
                        role = 'user'
                        content = [string]$scenario.prompt
                    }
                )

                $openAiTool = @(Convert-McpToolToOpenAiTool -Tool $tool)
                $assistantText = ''
                $toolCallResult = $null
                $completed = $false
                $lastToolArguments = $null
                $lastToolResultText = $null
                $lastAssistantMessage = $null
                $lastModelResponse = $null

                for ($turn = 0; $turn -lt $MaxConversationTurns; $turn++) {
                    $forcedToolName = if ($null -eq $toolCallResult) { $toolName } else { '' }
                    $completion = Invoke-ChatCompletion -BaseUrl $InferenceBaseUrl -ModelName $Model -Messages $messages -Tools $openAiTool -TimeoutSeconds $InferenceTimeoutSeconds -ToolName $forcedToolName
                    $lastModelResponse = ConvertTo-CompactJson -Value $completion
                    $choice = $completion.choices[0].message
                    $lastAssistantMessage = [string]$choice.content
                    $toolCalls = @($choice.tool_calls)

                    if ($toolCalls.Count -eq 0) {
                        $assistantText = [string]$choice.content
                        if ($null -eq $toolCallResult) {
                            throw 'Model returned a final assistant message before issuing a tool call.'
                        }

                        Test-ScenarioExpectations -Scenario $scenario -ToolCallResult $toolCallResult -AssistantText $assistantText
                        $completed = $true
                        break
                    }

                    $toolCall = $toolCalls[0]
                    if ([string]$toolCall.function.name -ne $toolName) {
                        throw "Model called unexpected tool '$($toolCall.function.name)' while testing '$toolName'."
                    }

                    $arguments = [string]$toolCall.function.arguments
                    if ([string]::IsNullOrWhiteSpace($arguments)) {
                        throw "Model returned empty arguments for tool '$toolName'."
                    }

                    $lastToolArguments = $arguments

                    try {
                        $parsedArguments = $arguments | ConvertFrom-Json
                    }
                    catch {
                        throw "Model returned invalid JSON arguments for '$toolName': $arguments"
                    }

                    $toolCallResult = Send-McpMessage -Process $process -TimeoutSeconds $McpTimeoutSeconds -Payload @{
                        jsonrpc = '2.0'
                        id = $nextRequestId
                        method = 'tools/call'
                        params = @{
                            name = $toolName
                            arguments = $parsedArguments
                        }
                    }
                    $nextRequestId++

                    $mcpErrorText = Get-McpErrorText -Payload $toolCallResult
                    $lastToolResultText = Get-ToolResultText -ToolCallResult $toolCallResult
                    if (-not [string]::IsNullOrWhiteSpace($mcpErrorText)) {
                        throw "$mcpErrorText. Arguments: $arguments. Payload: $lastToolResultText"
                    }

                    $messages += @{
                        role = 'assistant'
                        content = $choice.content
                        tool_calls = @(
                            @{
                                id = [string]$toolCall.id
                                type = 'function'
                                function = @{
                                    name = $toolName
                                    arguments = $arguments
                                }
                            }
                        )
                    }

                    $messages += @{
                        role = 'tool'
                        tool_call_id = [string]$toolCall.id
                        content = $lastToolResultText
                    }
                }

                if (-not $completed) {
                    throw "Scenario for '$toolName' exceeded $MaxConversationTurns turns without a final assistant message."
                }

                $passedResult = [pscustomobject]@{
                    Tool = $toolName
                    Status = 'Passed'
                    Detail = $assistantText
                    Scenario = [string]$scenario.prompt
                }
                $results.Add($passedResult)
                Add-ToolResult -ToolName $toolName -Result $passedResult
                Write-ScenarioStatus -ToolName $scenarioLabel -Status 'Passed' -Detail $assistantText
            }
            catch {
                $detail = $_.Exception.Message
                $failureContext = New-Object System.Collections.Generic.List[string]

                if ($null -ne $lastToolArguments) {
                    $failureContext.Add('Arguments: ' + $lastToolArguments)
                }

                if ($null -ne $lastAssistantMessage -and -not [string]::IsNullOrWhiteSpace($lastAssistantMessage)) {
                    $failureContext.Add('Assistant: ' + $lastAssistantMessage)
                }

                if ($null -ne $lastToolResultText) {
                    $failureContext.Add('ToolResult: ' + $lastToolResultText)
                }

                if ($null -eq $lastToolResultText -and $null -ne $lastModelResponse) {
                    $failureContext.Add('ModelResponse: ' + $lastModelResponse)
                }

                if ($failureContext.Count -gt 0) {
                    $detail = $detail + ' | ' + ($failureContext -join ' | ')
                }

                $failedResult = [pscustomobject]@{
                    Tool = $toolName
                    Status = 'Failed'
                    Detail = $detail
                    Scenario = [string]$scenario.prompt
                }
                $results.Add($failedResult)
                Add-ToolResult -ToolName $toolName -Result $failedResult
                Write-ScenarioStatus -ToolName $scenarioLabel -Status 'Failed' -Detail $detail
            }
        }
    }

    if ($null -ne $resolvedResultPath) {
        $resultDirectory = Split-Path -Path $resolvedResultPath -Parent
        if (-not [string]::IsNullOrWhiteSpace($resultDirectory) -and -not (Test-Path -LiteralPath $resultDirectory)) {
            New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
        }

        $json = ConvertTo-Json -InputObject ([object[]]$results.ToArray()) -Depth 20
        [System.IO.File]::WriteAllText($resolvedResultPath, $json, [System.Text.UTF8Encoding]::new($false))
        Write-Host "Wrote result report to $resolvedResultPath"
    }

    $results | Format-Table -AutoSize | Out-String | Write-Host

    $failed = @($results | Where-Object { $_.Status -eq 'Failed' })
    if ($failed.Count -gt 0) {
        throw ('Inference tool smoke test failed for: ' + (($failed | ForEach-Object { $_.Tool }) -join ', '))
    }
}
finally {
    Stop-McpServerProcess -Process $process
}
