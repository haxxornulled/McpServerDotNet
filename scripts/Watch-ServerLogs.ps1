<#
Watch the McpServer host log files and stream new entries to the console.

Usage:
  # Follow the newest log file under the default repo logs directory:
  .\scripts\Watch-ServerLogs.ps1

  # Follow a different logs folder:
  .\scripts\Watch-ServerLogs.ps1 -LogDirectory C:\temp\mcp-logs

Notes:
- The watcher follows the newest `mcp-server-*.log` file and switches to a newer file if one appears.
- Use Ctrl+C to stop the monitor.
#>

param(
    [string]$LogDirectory,
    [string]$LogPattern = 'mcp-server-*.log',
    [int]$PollIntervalMs = 1000,
    [int]$InitialTailLines = 50
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($LogDirectory)) {
    $repoRoot = (Resolve-Path (Join-Path -Path $PSScriptRoot -ChildPath '..')).Path
    $LogDirectory = Join-Path -Path $repoRoot -ChildPath 'logs'
}

function Get-LatestLogFile {
    param(
        [string]$Directory,
        [string]$Pattern
    )

    if (-not (Test-Path -Path $Directory)) {
        return $null
    }

    return Get-ChildItem -Path $Directory -File -Filter $Pattern |
        Sort-Object LastWriteTime, Name -Descending |
        Select-Object -First 1
}

function Write-InitialTail {
    param(
        [string]$Path,
        [int]$TailLines
    )

    if (-not (Test-Path -Path $Path)) {
        return
    }

    Write-Host "[logs] Following: $Path"
    $lines = Get-Content -Path $Path -Tail $TailLines -ErrorAction Stop
    foreach ($line in $lines) {
        Write-Host $line
    }
}

function Follow-LogFile {
    param(
        [string]$Path,
        [long]$StartOffset
    )

    $file = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        if ($StartOffset -gt $file.Length) {
            $StartOffset = 0
        }

        $file.Seek($StartOffset, [System.IO.SeekOrigin]::Begin) | Out-Null
        $reader = [System.IO.StreamReader]::new($file)
        try {
            while (-not $reader.EndOfStream) {
                $line = $reader.ReadLine()
                if ($null -ne $line) {
                    Write-Host $line
                }
            }
            return $file.Position
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $file.Dispose()
    }
}

Write-Host "[logs] Watching $LogDirectory for $LogPattern. Press Ctrl+C to stop."

$currentFile = $null
$currentOffset = 0L

try {
    while ($true) {
        $latest = Get-LatestLogFile -Directory $LogDirectory -Pattern $LogPattern

        if ($null -eq $latest) {
            if ($null -ne $currentFile) {
                Write-Host "[logs] Waiting for a new log file..."
                $currentFile = $null
                $currentOffset = 0L
            }

            Start-Sleep -Milliseconds $PollIntervalMs
            continue
        }

        if ($null -eq $currentFile -or $latest.FullName -ne $currentFile) {
            $currentFile = $latest.FullName
            $currentOffset = 0L
            Write-InitialTail -Path $currentFile -TailLines $InitialTailLines
            $currentOffset = (Get-Item -LiteralPath $currentFile).Length
            Start-Sleep -Milliseconds $PollIntervalMs
            continue
        }

        try {
            $newOffset = Follow-LogFile -Path $currentFile -StartOffset $currentOffset
            if ($newOffset -gt $currentOffset) {
                $currentOffset = $newOffset
            }
        }
        catch {
            Write-Warning "[logs] Unable to read $currentFile right now: $($_.Exception.Message)"
        }

        Start-Sleep -Milliseconds $PollIntervalMs
    }
}
finally {
    Write-Host '[logs] Exiting.'
}
