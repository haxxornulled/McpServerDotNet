[CmdletBinding()]
param(
    [string]$ApiBaseUrl = 'http://127.0.0.1:1234',
    [string]$Model = 'cleanunicorn/qwen3-coder-30b-a3b-instruct',
    [string]$ApiToken,
    [int]$MaxTokens = 512,
    [double]$Temperature = 0,
    [switch]$PrettyPrint
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

function Invoke-LmStudioOpenAiRequest {
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
        return Invoke-RestMethod -Uri $uri -Method $Method -Headers $headers -TimeoutSec 300
    }

    return Invoke-RestMethod -Uri $uri -Method $Method -Headers $headers -ContentType 'application/json' -Body (ConvertTo-CompactJson -Value $Body) -TimeoutSec 300
}

function Get-LmStudioModelMetadata {
    param(
        [Parameter(Mandatory = $true)][string]$BaseUrl,
        [string]$Token
    )

    $response = Invoke-LmStudioOpenAiRequest -BaseUrl $BaseUrl -Path '/api/v1/models' -Method Get -Body @{} -Token $Token
    if ($response.PSObject.Properties.Name -contains 'models') {
        return @($response.models)
    }

    if ($response.PSObject.Properties.Name -contains 'data') {
        return @($response.data)
    }

    return @($response)
}

function Get-StructuredOutputSchema {
    param([Parameter(Mandatory = $true)][string]$ModelName)

    return @{
        type = 'json_schema'
        json_schema = @{
            name = 'gpu_workout_plan'
            strict = $true
            schema = @{
                type = 'object'
                additionalProperties = $false
                properties = @{
                    model = @{
                        type = 'string'
                        enum = @($ModelName)
                    }
                    summary = @{
                        type = 'string'
                    }
                    tuning_plan = @{
                        type = 'array'
                        minItems = 4
                        items = @{
                            type = 'object'
                            additionalProperties = $false
                            properties = @{
                                step = @{
                                    type = 'string'
                                }
                                why_it_matters = @{
                                    type = 'string'
                                }
                            }
                            required = @('step', 'why_it_matters')
                        }
                    }
                    validation_checks = @{
                        type = 'array'
                        minItems = 3
                        items = @{
                            type = 'string'
                        }
                    }
                }
                required = @('model', 'summary', 'tuning_plan', 'validation_checks')
            }
        }
    }
}

$modelList = @(Get-LmStudioModelMetadata -BaseUrl $ApiBaseUrl -Token $ApiToken)
if ($modelList.Count -eq 0) {
    throw "No models were returned from $ApiBaseUrl/api/v1/models"
}

$selectedModel = $modelList | Where-Object {
    ([string]$_.key -eq $Model) -or ([string]$_.id -eq $Model)
} | Select-Object -First 1

if ($null -eq $selectedModel) {
    throw "Model '$Model' was not found in LM Studio's model list."
}

Write-Host "Requesting structured output from $Model"

$response = Invoke-LmStudioOpenAiRequest -BaseUrl $ApiBaseUrl -Path '/v1/chat/completions' -Method Post -Token $ApiToken -Body @{
    model = $Model
    messages = @(
        @{
            role = 'system'
            content = 'Return only valid JSON that conforms to the provided schema.'
        }
        @{
            role = 'user'
            content = 'Create a practical GPU workout plan for LM Studio that uses a large local model, pushes context length, and verifies that schema-constrained generation works.'
        }
    )
    response_format = Get-StructuredOutputSchema -ModelName $Model
    temperature = $Temperature
    max_tokens = $MaxTokens
    stream = $false
}

$content = [string]$response.choices[0].message.content
if ([string]::IsNullOrWhiteSpace($content)) {
    throw 'Structured output response was empty.'
}

$parsed = $content | ConvertFrom-Json -Depth 50
if ($PrettyPrint) {
    $parsed | ConvertTo-Json -Depth 50
}
else {
    $parsed
}

Write-Host "Structured output probe succeeded."
