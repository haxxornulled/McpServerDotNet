<#
.SYNOPSIS
    Stops the local MCPServer AgentRouter development stack.

.DESCRIPTION
    Stops processes recorded by scripts/Start-AgentRouterStack.ps1.
    Also supports optional port-based cleanup for AgentRouter/Ollama.

    Compatible with Windows PowerShell 5.1.

.EXAMPLE
    .\scripts\Stop-AgentRouterStack.ps1

.EXAMPLE
    .\scripts\Stop-AgentRouterStack.ps1 -KillByPort

.EXAMPLE
    .\scripts\Stop-AgentRouterStack.ps1 -KillOllama
#>

[CmdletBinding()]
param(
    [int] $RouterPort = 5177,

    [int] $OllamaPort = 11434,

    [switch] $KillByPort,

    [switch] $KillOllama
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path ".").Path
$pidDirectory = Join-Path $repoRoot ".run\pids"

function Write-Section {
    param([string] $Name)

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host $Name -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan
}

function Write-Good {
    param([string] $Message)

    Write-Host "[OK] $Message" -ForegroundColor Green
}

function Write-Warn {
    param([string] $Message)

    Write-Host "[WARN] $Message" -ForegroundColor Yellow
}

function Stop-PidFile {
    param(
        [string] $Name,
        [string] $PidFile
    )

    if (-not (Test-Path $PidFile)) {
        Write-Warn "$Name PID file not found: $PidFile"
        return
    }

    $raw = Get-Content -Path $PidFile -Raw
    $pidValue = 0

    if (-not [int]::TryParse($raw.Trim(), [ref] $pidValue)) {
        Write-Warn "$Name PID file does not contain a valid process id: $PidFile"
        return
    }

    $process = Get-Process -Id $pidValue -ErrorAction SilentlyContinue

    if ($process -eq $null) {
        Write-Warn "$Name process $pidValue is not running."
        Remove-Item -Path $PidFile -Force -ErrorAction SilentlyContinue
        return
    }

    Stop-Process -Id $pidValue -Force
    Write-Good "Stopped $Name PID $pidValue."
    Remove-Item -Path $PidFile -Force -ErrorAction SilentlyContinue
}

function Stop-ListenerOnPort {
    param(
        [string] $Name,
        [int] $Port
    )

    $connections = Get-NetTCPConnection `
        -LocalPort $Port `
        -State Listen `
        -ErrorAction SilentlyContinue

    if ($connections -eq $null) {
        Write-Warn "No listener found on port $Port for $Name."
        return
    }

    foreach ($connection in $connections) {
        $pidValue = $connection.OwningProcess
        $process = Get-Process -Id $pidValue -ErrorAction SilentlyContinue

        if ($process -eq $null) {
            continue
        }

        Stop-Process -Id $pidValue -Force
        Write-Good "Stopped $Name listener PID $pidValue on port $Port."
    }
}

Write-Section "Stopping recorded AgentRouter stack processes"

if (Test-Path $pidDirectory) {
    Stop-PidFile -Name "AgentRouter" -PidFile (Join-Path $pidDirectory "agentrouter.pid")
    Stop-PidFile -Name "AgentRouter background wrapper" -PidFile (Join-Path $pidDirectory "agentr-router.pid")

    if ($KillOllama) {
        Stop-PidFile -Name "Ollama" -PidFile (Join-Path $pidDirectory "ollama.pid")
    }
    else {
        Write-Warn "Leaving Ollama running. Pass -KillOllama to stop recorded Ollama process."
    }
}
else {
    Write-Warn "PID directory not found: $pidDirectory"
}

if ($KillByPort) {
    Write-Section "Port cleanup"
    Stop-ListenerOnPort -Name "AgentRouter" -Port $RouterPort

    if ($KillOllama) {
        Stop-ListenerOnPort -Name "Ollama" -Port $OllamaPort
    }
}

Write-Section "Done"
