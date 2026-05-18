param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$normalizedPath = [System.IO.Path]::GetFullPath($ExecutablePath)

Get-Process McpServer.Host -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $normalizedPath } |
    Stop-Process -Force -ErrorAction SilentlyContinue
