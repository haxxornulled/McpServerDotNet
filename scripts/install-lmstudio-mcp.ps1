[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$ServerName = 'mcpserver-release',
    [string]$Command = (Join-Path $PSScriptRoot '..\src\McpServer.Host\bin\Release\net10.0\McpServer.Host.exe'),
    [string]$WorkspaceRoot = (Join-Path $PSScriptRoot '..'),
    [string[]]$AllowedRoots = @(),
    [string[]]$Args = @(),
    [switch]$EnableShell
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-LmStudioCommand {
    param([Parameter(Mandatory = $true)][string]$Candidate)

    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        throw 'A command value must be provided.'
    }

    if ([System.IO.Path]::IsPathRooted($Candidate)) {
        $resolvedPath = [System.IO.Path]::GetFullPath($Candidate)
        if (Test-Path -Path $resolvedPath) {
            return $resolvedPath
        }

        if ($Candidate.EndsWith('.exe', [System.StringComparison]::OrdinalIgnoreCase)) {
            $toolCommand = Get-Command -Name 'mcpserver' -ErrorAction SilentlyContinue
            if ($null -ne $toolCommand) {
                Write-Warning "Release host exe was not found at '$resolvedPath'. Falling back to the installed 'mcpserver' command."
                return 'mcpserver'
            }
        }

        throw "LM Studio command path was not found: $resolvedPath"
    }

    $resolvedCommand = Get-Command -Name $Candidate -ErrorAction SilentlyContinue
    if ($null -ne $resolvedCommand) {
        return $resolvedCommand.Name
    }

    return $Candidate
}

function Resolve-FullPathValue {
    param([Parameter(Mandatory = $true)][string]$PathValue)

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        throw 'Path value cannot be empty.'
    }

    return [System.IO.Path]::GetFullPath($PathValue)
}

$lmStudioDir = Join-Path -Path $env:USERPROFILE -ChildPath '.lmstudio'
$mcpJsonPath = Join-Path -Path $lmStudioDir -ChildPath 'mcp.json'

if (-not (Test-Path -Path $lmStudioDir)) {
    New-Item -ItemType Directory -Path $lmStudioDir -Force | Out-Null
}

if (Test-Path -Path $mcpJsonPath) {
    $config = Get-Content -Path $mcpJsonPath -Raw | ConvertFrom-Json -AsHashtable
} else {
    $config = @{}
}

if (-not $config.ContainsKey('mcpServers')) {
    $config['mcpServers'] = @{}
}

$resolvedWorkspaceRoot = Resolve-FullPathValue -PathValue $WorkspaceRoot
$effectiveAllowedRoots = New-Object System.Collections.Generic.List[string]
$effectiveAllowedRoots.Add($resolvedWorkspaceRoot)

foreach ($root in $AllowedRoots) {
    if (-not [string]::IsNullOrWhiteSpace($root)) {
        $effectiveAllowedRoots.Add((Resolve-FullPathValue -PathValue $root))
    }
}

$distinctAllowedRoots = @($effectiveAllowedRoots | Select-Object -Unique)

$envVars = @{
    'MCPSERVER__WORKSPACE__ROOTPATH' = $resolvedWorkspaceRoot
    'MCPSERVER__WORKSPACE__ALLOWRUNTIMEWORKSPACEOPEN' = 'true'
    'MCPSERVER__SHELL__ENABLED' = $(if ($EnableShell) { 'true' } else { 'false' })
}

for ($i = 0; $i -lt $distinctAllowedRoots.Count; $i++) {
    $envVars["MCPSERVER__WORKSPACE__ALLOWEDROOTS__$i"] = $distinctAllowedRoots[$i]
}

$serverConfig = @{
    command = (Resolve-LmStudioCommand -Candidate $Command)
    args = @($Args)
    env = $envVars
}

if ($PSCmdlet.ShouldProcess($mcpJsonPath, "Install or update LM Studio MCP server '$ServerName'")) {
    $config['mcpServers'][$ServerName] = $serverConfig
    $config | ConvertTo-Json -Depth 20 | Set-Content -Path $mcpJsonPath -Encoding UTF8
}

Write-Host "Installed or updated '$ServerName' in $mcpJsonPath"
Write-Host "Workspace root: $resolvedWorkspaceRoot"
Write-Host "Allowed roots:"
foreach ($root in $distinctAllowedRoots) {
    Write-Host "  - $root"
}
Write-Host "Shell enabled: $($EnableShell.IsPresent)"
