[CmdletBinding()]
param(
    [string]$ServerExe = (Join-Path $PSScriptRoot '..\src\McpServer.Host\bin\Release\net10.0\McpServer.Host.exe'),
    [string]$WorkspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [int]$TimeoutSeconds = 15,
    [switch]$AttemptGracefulShutdown
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

$fullServerExe = [System.IO.Path]::GetFullPath($ServerExe)
if (-not (Test-Path -LiteralPath $fullServerExe)) {
    throw "MCP server executable was not found: $fullServerExe"
}

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $fullServerExe
$startInfo.WorkingDirectory = $WorkspaceRoot
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.Environment['ASPNETCORE_ENVIRONMENT'] = 'Development'
$startInfo.Environment['MCPSERVER__WORKSPACE__ROOTPATH'] = $WorkspaceRoot
$startInfo.Environment['MCPSERVER__WORKSPACE__ALLOWEDROOTS__0'] = $WorkspaceRoot
$startInfo.Environment['MCPSERVER__SHELL__ENABLED'] = 'false'
$startInfo.Environment['MCPSERVER__WEBACCESS__ENABLED'] = 'false'
$startInfo.Environment['MCPSERVER__SSH__ENABLED'] = 'false'

$process = [System.Diagnostics.Process]::new()
$process.StartInfo = $startInfo

$initializeResponse = $null
$toolsResponse = $null
$stderrText = ''
$terminatedAfterVerification = $false
$verificationPassed = $false
$exitCode = 1

try {
    if (-not $process.Start()) { throw 'Failed to start MCP server process.' }

    $stdin = $process.StandardInput.BaseStream
    $stdout = $process.StandardOutput.BaseStream

    $initialize = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"framed-smoke","version":"1.0.0"}}}'
    $initializeResponse = Send-RequestAndReadResponse -InputStream $stdin -OutputStream $stdout -Json $initialize
    if ($null -eq $initializeResponse) { throw 'No initialize response received.' }

    Write-FramedMessage -Stream $stdin -Json '{"jsonrpc":"2.0","method":"notifications/initialized"}'

    $toolsList = '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'
    $toolsResponse = Send-RequestAndReadResponse -InputStream $stdin -OutputStream $stdout -Json $toolsList
    if ($null -eq $toolsResponse) { throw 'No tools/list response received.' }

    if ($AttemptGracefulShutdown) {
        try {
            [void](Send-RequestAndReadResponse -InputStream $stdin -OutputStream $stdout -Json '{"jsonrpc":"2.0","id":3,"method":"shutdown","params":{}}')
            Write-FramedMessage -Stream $stdin -Json '{"jsonrpc":"2.0","method":"exit"}'
            $process.StandardInput.Close()
            [void]$process.WaitForExit([TimeSpan]::FromSeconds(2))
        }
        catch {
            Write-Verbose "Graceful shutdown attempt did not complete cleanly: $($_.Exception.Message)"
        }
    }

    $verificationPassed = $true
    $exitCode = 0
}
catch {
    Write-Error $_
    $verificationPassed = $false
    $exitCode = 1
}
finally {
    if ($verificationPassed -and -not $process.HasExited) {
        $terminatedAfterVerification = $true
        $process.Kill($true)
        $process.WaitForExit()
    }
    elseif (-not $process.HasExited) {
        $process.Kill($true)
        $process.WaitForExit()
    }

    $stderrText = $process.StandardError.ReadToEnd()
    $process.Dispose()
}

if ($verificationPassed) {
    $fatalTransportErrorFound =
        $stderrText.Contains('Failed to deserialize JSON-RPC request line', [System.StringComparison]::Ordinal) -or
        $stderrText.Contains('Failed to deserialize JSON-RPC request message', [System.StringComparison]::Ordinal)

    if ($fatalTransportErrorFound) {
        Write-Error 'Transport verification failed: server logged a JSON-RPC deserialization error for correctly framed input.'
        if (-not [string]::IsNullOrWhiteSpace($stderrText)) {
            Write-Host $stderrText
        }

        exit 1
    }

    [pscustomobject]@{
        InitializeProtocolVersion = $initializeResponse.result.protocolVersion
        ServerName = $initializeResponse.result.serverInfo.name
        ServerVersion = $initializeResponse.result.serverInfo.version
        ToolCount = @($toolsResponse.result.tools).Count
    } | Format-List

    Write-Host 'Collected expected initialize response.'
    Write-Host 'Collected expected tools/list response.'

    if ($terminatedAfterVerification) {
        Write-Host 'Server remained open waiting for additional stdio JSON-RPC messages.'
        Write-Host 'Smoke harness terminated the process after verification.'
    }
    else {
        Write-Host 'Server exited after verification.'
    }

    Write-Host 'Transport verification passed.'
    exit 0
}

if (-not [string]::IsNullOrWhiteSpace($stderrText)) {
    Write-Host $stderrText
}

exit $exitCode
