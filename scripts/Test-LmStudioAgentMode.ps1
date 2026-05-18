param(
    [string]$LmStudioRoot = (Join-Path -Path $env:USERPROFILE -ChildPath '.lmstudio'),
    [string]$ExpectedServerName = 'mcpserver-release'
)

$settingsPath = Join-Path -Path $LmStudioRoot -ChildPath 'settings.json'
$mcpJsonPath = Join-Path -Path $LmStudioRoot -ChildPath 'mcp.json'
$serverLogDir = Join-Path -Path $LmStudioRoot -ChildPath 'server-logs'
$sandboxPluginPath = Join-Path -Path $LmStudioRoot -ChildPath 'extensions\plugins\lmstudio\js-code-sandbox'
$expectedPattern = "mcp/${ExpectedServerName}:*"

function Write-Check {
    param(
        [string]$Label,
        [bool]$Passed,
        [string]$Detail
    )

    $prefix = if ($Passed) { '[PASS]' } else { '[FAIL]' }
    Write-Host "$prefix $Label - $Detail"
}

if (-not (Test-Path -Path $settingsPath)) {
    Write-Error "LM Studio settings not found: $settingsPath"
    exit 1
}

if (-not (Test-Path -Path $mcpJsonPath)) {
    Write-Error "LM Studio MCP config not found: $mcpJsonPath"
    exit 1
}

$settings = Get-Content -Path $settingsPath -Raw | ConvertFrom-Json -Depth 32
$mcpConfig = Get-Content -Path $mcpJsonPath -Raw | ConvertFrom-Json -AsHashtable

$installedServer = $mcpConfig.mcpServers.ContainsKey($ExpectedServerName)
$skipPatterns = @()
if ($settings.chat.skipToolConfirmationPatterns) {
    $skipPatterns = @($settings.chat.skipToolConfirmationPatterns)
}

$releaseSkipPatterns = @(
    $skipPatterns | Where-Object {
        $_ -like "mcp/${ExpectedServerName}:*"
    }
)

$hasSkipPattern = $releaseSkipPatterns.Count -gt 0
$hasWildcardSkipPattern = $skipPatterns -contains $expectedPattern

$latestServerLog = $null
if (Test-Path -Path $serverLogDir) {
    $latestServerLog = Get-ChildItem -Path $serverLogDir -Recurse -File -Filter '*.log' |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
}

$serverLogText = $null
if ($null -ne $latestServerLog) {
    $serverLogText = Get-Content -Path $latestServerLog.FullName -Raw
}

$serverLogShowsMcpHandshake = $false
$serverLogShowsContextOverflow = $false
$serverLogShowsPromptTemplateSafeFilter = $false
$serverLogShowsSandbox = $false
$sandboxPluginPresent = Test-Path -Path $sandboxPluginPath
if ($null -ne $serverLogText) {
    $serverLogShowsMcpHandshake = $serverLogText.Contains("Client=plugin:installed:mcp/$ExpectedServerName", [System.StringComparison]::OrdinalIgnoreCase) -and
        $serverLogText.Contains("Handled tools/list", [System.StringComparison]::OrdinalIgnoreCase)
    $serverLogShowsContextOverflow = $serverLogText.Contains('exceeds the available context size', [System.StringComparison]::OrdinalIgnoreCase)
    $serverLogShowsPromptTemplateSafeFilter = $serverLogText.Contains('Unknown StringValue filter: safe', [System.StringComparison]::OrdinalIgnoreCase)
    $serverLogShowsSandbox = $serverLogText.Contains('lmstudio/js-code-sandbox', [System.StringComparison]::OrdinalIgnoreCase)
}

$pass = $installedServer -and $hasSkipPattern -and $serverLogShowsMcpHandshake

Write-Check -Label 'MCP install' -Passed $installedServer -Detail "Server '$ExpectedServerName' is present in mcp.json."
Write-Check -Label 'Tool skips' -Passed $hasSkipPattern -Detail "Release server skip patterns are present."

if (-not $hasWildcardSkipPattern -and $hasSkipPattern) {
    Write-Host "[WARN] Wildcard skip '$expectedPattern' is missing. The release server is still covered by specific tool skips, but the wildcard keeps agent-mode confirmation cleaner."
}

if ($null -ne $latestServerLog) {
    Write-Check -Label 'Agent mode' -Passed $serverLogShowsMcpHandshake -Detail "Latest LM Studio server log shows the MCP plugin handshake."
    if ($serverLogShowsContextOverflow) {
        Write-Host '[WARN] Latest LM Studio server log shows context overflow errors. Lower the chat context or trim the prompt/tool set before retrying tool calls.'
    }
    if ($serverLogShowsPromptTemplateSafeFilter) {
        Write-Host '[WARN] Latest LM Studio server log shows a model prompt-template error: Unknown StringValue filter: safe. This comes from the selected model chat template, not the MCP server. Use an lmstudio-community variant of the model or edit the model Prompt Template to remove unsupported "| safe" filters.'
    }
    if ($serverLogShowsSandbox -and $sandboxPluginPresent) {
        Write-Host '[WARN] Latest LM Studio server log also contains lmstudio/js-code-sandbox and the plugin is still present on disk.'
    }
    elseif ($serverLogShowsSandbox) {
        Write-Host '[INFO] Historical LM Studio logs mention lmstudio/js-code-sandbox, but the plugin is disabled on disk now.'
    }
} else {
    Write-Host '[WARN] No LM Studio server log found to inspect.'
}

if ($sandboxPluginPresent) {
    Write-Host "[WARN] Sandbox plugin still exists on disk at $sandboxPluginPath, but it is not part of the active MCP release-server skip path."
}

if (-not $pass) {
    Write-Host ''
    Write-Host 'Agent mode is not fully verified yet.'
    exit 1
}

Write-Host ''
Write-Host 'Agent mode looks ready for MCP tool use.'
exit 0
