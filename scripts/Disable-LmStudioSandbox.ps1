param(
    [string]$LmStudioRoot = (Join-Path -Path $env:USERPROFILE -ChildPath '.lmstudio')
)

$settingsPath = Join-Path -Path $LmStudioRoot -ChildPath 'settings.json'
$sandboxPluginPath = Join-Path -Path $LmStudioRoot -ChildPath 'extensions\plugins\lmstudio\js-code-sandbox'
$sandboxBackupPath = Join-Path -Path $LmStudioRoot -ChildPath 'extensions\plugins\lmstudio\js-code-sandbox.disabled'

if (-not (Test-Path -Path $settingsPath)) {
    throw "LM Studio settings not found: $settingsPath"
}

$settings = Get-Content -Path $settingsPath -Raw | ConvertFrom-Json -Depth 32

$removedPatterns = 0
if ($settings.chat.skipToolConfirmationPatterns) {
    $patterns = @($settings.chat.skipToolConfirmationPatterns)
    $filteredPatterns = @(
        $patterns | Where-Object {
            $_ -notlike 'lmstudio/js-code-sandbox:*'
        }
    )

    $removedPatterns = $patterns.Count - $filteredPatterns.Count
    $settings.chat.skipToolConfirmationPatterns = @($filteredPatterns)
}

if ($settings.chat.pinnedPlugins) {
    $settings.chat.pinnedPlugins = @(
        @($settings.chat.pinnedPlugins) | Where-Object {
            $_ -ne 'lmstudio/js-code-sandbox'
        }
    )
}

$settings | ConvertTo-Json -Depth 32 | Set-Content -Path $settingsPath -Encoding UTF8

if (Test-Path -Path $sandboxPluginPath) {
    if (Test-Path -Path $sandboxBackupPath) {
        Remove-Item -LiteralPath $sandboxPluginPath -Recurse -Force
    } else {
        Move-Item -LiteralPath $sandboxPluginPath -Destination $sandboxBackupPath -Force
    }
}

Write-Host "Sandbox plugin disabled in $settingsPath"
if ($removedPatterns -gt 0) {
    Write-Host "Removed $removedPatterns sandbox skip pattern(s) from LM Studio settings."
}
if (Test-Path -Path $sandboxBackupPath) {
    Write-Host "Sandbox plugin moved to $sandboxBackupPath"
}
