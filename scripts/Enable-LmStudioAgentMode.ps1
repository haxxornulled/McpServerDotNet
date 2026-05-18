param(
    [string]$LmStudioRoot = (Join-Path -Path $env:USERPROFILE -ChildPath '.lmstudio'),
    [string]$ExpectedServerName = 'mcpserver-release'
)

$settingsPath = Join-Path -Path $LmStudioRoot -ChildPath 'settings.json'
$expectedPattern = "mcp/${ExpectedServerName}:*"

if (-not (Test-Path -Path $settingsPath)) {
    throw "LM Studio settings not found: $settingsPath"
}

& (Join-Path -Path $PSScriptRoot -ChildPath 'Disable-LmStudioSandbox.ps1') -LmStudioRoot $LmStudioRoot

$settings = Get-Content -Path $settingsPath -Raw | ConvertFrom-Json -Depth 32

if (-not $settings.chat.skipToolConfirmationPatterns) {
    $settings.chat.skipToolConfirmationPatterns = @()
}

$patterns = @($settings.chat.skipToolConfirmationPatterns)
if ($patterns -notcontains $expectedPattern) {
    $patterns += $expectedPattern
    $settings.chat.skipToolConfirmationPatterns = $patterns
    $settings | ConvertTo-Json -Depth 32 | Set-Content -Path $settingsPath -Encoding UTF8
    Write-Host "Added '$expectedPattern' to LM Studio skip tool confirmation patterns."
} else {
    Write-Host "'$expectedPattern' is already present in LM Studio skip tool confirmation patterns."
}

Write-Host 'LM Studio agent mode has been enabled for the MCP release server.'
