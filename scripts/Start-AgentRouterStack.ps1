<#
.SYNOPSIS
    Starts the local MCPServer AgentRouter development stack in the correct order.

.DESCRIPTION
    Order:
      1. Ensure Ollama is listening on 127.0.0.1:11434.
      2. Optionally pull/check configured Ollama models.
      3. Optionally build the solution.
      4. Start McpServer.AgentRouter.Host.
      5. Wait for /health.
      6. Optionally run the typed .NET smoke harness in tools/McpServer.AgentRouter.Tools.

    Compatible with Windows PowerShell 5.1.

.EXAMPLE
    .\scripts\Start-AgentRouterStack.ps1

.EXAMPLE
    .\scripts\Start-AgentRouterStack.ps1 -Build -RunSmoke

.EXAMPLE
    .\scripts\Start-AgentRouterStack.ps1 -NoNewWindows -Build

.EXAMPLE
    .\scripts\Start-AgentRouterStack.ps1 -NoNewWindows -RunSmoke -EnableSshSmoke -SshProfile dev
#>

[CmdletBinding()]
param(
    [string] $RouterBaseUrl = "http://127.0.0.1:5177",

    [string] $OllamaBaseUrl = "http://127.0.0.1:11434",

    [string] $Configuration = "Release",

    [string] $SolutionPath = ".\McpServer.slnx",

    [string] $AgentRouterProjectPath = ".\src\McpServer.AgentRouter.Host\McpServer.AgentRouter.Host.csproj",

    [string] $AgentRouterToolsProjectPath = ".\tools\McpServer.AgentRouter.Tools\McpServer.AgentRouter.Tools.csproj",

    [string] $RunStorageRoot = ".\workspace\artifacts\agent-runs",

    [string[]] $RequiredModels = @(
        "qwen2.5-coder:14b"
    ),

    [int] $StartupTimeoutSeconds = 60,

    [switch] $Build,

    [switch] $RunSmoke,

    [switch] $EnableSshSmoke,

    [string] $SshProfile = "dev",

    [string] $SshCommand = "whoami",

    [string] $SshWorkingDirectory = "/tmp",

    [switch] $NoNewWindows
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path ".").Path
$runDirectory = Join-Path $repoRoot ".run"
$logDirectory = Join-Path $runDirectory "logs"
$pidDirectory = Join-Path $runDirectory "pids"

New-Item -ItemType Directory -Force $runDirectory | Out-Null
New-Item -ItemType Directory -Force $logDirectory | Out-Null
New-Item -ItemType Directory -Force $pidDirectory | Out-Null

$routerUri = [Uri] $RouterBaseUrl
$ollamaUri = [Uri] $OllamaBaseUrl
$routerPort = $routerUri.Port
$ollamaPort = $ollamaUri.Port

$resolvedRunStorageRoot = if ([System.IO.Path]::IsPathRooted($RunStorageRoot)) {
    $RunStorageRoot
}
else {
    Join-Path $repoRoot $RunStorageRoot
}

function Get-SshProfilePasswordEnvironmentVariableName {
    param([string] $ProfileName)

    foreach ($candidate in @(
        (Join-Path $repoRoot "config\agentrouter\ssh-profiles.local.json"),
        (Join-Path $repoRoot "config\agentrouter\ssh-profiles.local.example.json")
    )) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            continue
        }

        try {
            $document = Get-Content -LiteralPath $candidate -Raw | ConvertFrom-Json
            $profiles = $document.profiles
            if ($profiles -eq $null) {
                continue
            }

            $profile = $profiles.PSObject.Properties[$ProfileName]
            if ($profile -eq $null -or $profile.Value -eq $null) {
                continue
            }

            $value = $profile.Value.passwordEnvironmentVariable
            if (-not [string]::IsNullOrWhiteSpace($value)) {
                return [string] $value
            }
        }
        catch {
        }
    }

    return $null
}

if ($RunSmoke -and $EnableSshSmoke) {
    $sshPasswordEnvironmentVariable = Get-SshProfilePasswordEnvironmentVariableName -ProfileName $SshProfile
    if ([string]::IsNullOrWhiteSpace($sshPasswordEnvironmentVariable)) {
        Write-Host "[ERROR] SSH smoke requested for profile '$SshProfile', but the local profile file does not define a passwordEnvironmentVariable." -ForegroundColor Red
        exit 1
    }

    $sshSecret = [Environment]::GetEnvironmentVariable($sshPasswordEnvironmentVariable)
    if ([string]::IsNullOrWhiteSpace($sshSecret)) {
        Write-Host "[ERROR] SSH smoke requested for profile '$SshProfile', but environment variable '$sshPasswordEnvironmentVariable' is not set in this shell." -ForegroundColor Red
        exit 1
    }
}

$smokeAllowedTools = @(
    "activity.context.preview",
    "activity.route",
    "activity.run",
    "activity.schemas.list",
    "fs.append_text",
    "fs.copy_path",
    "fs.create_directory",
    "fs.delete_path",
    "fs.get_metadata",
    "fs.list_directory",
    "fs.move_path",
    "fs.read_file",
    "fs.read_text",
    "fs.write_text",
    "workspace.inspect",
    "workspace.open",
    "workspace.select_folder",
    "workspace.set_root",
    "workspace.status"
)

$smokeAllowedToolsBlock = ""
if ($RunSmoke) {
    $smokeAllowedToolLines = @()
    for ($index = 0; $index -lt $smokeAllowedTools.Count; $index++) {
        $smokeAllowedToolLines += "`$env:AgentRouter__ToolExecution__AllowedTools__$index = '$($smokeAllowedTools[$index])'"
    }

    $smokeAllowedToolsBlock = $smokeAllowedToolLines -join "`r`n"
}

$routerEnvironmentBlock = @(
    "`$env:ASPNETCORE_URLS = '$RouterBaseUrl'"
    "`$env:AgentRouter__BindUrl = '$RouterBaseUrl'"
    "`$env:AgentRouter__RunStorage__RootPath = '$resolvedRunStorageRoot'"
)

$routerEnvironmentBlock = ($routerEnvironmentBlock + $smokeAllowedToolsBlock) -join "`r`n"

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

function Write-Bad {
    param([string] $Message)

    Write-Host "[ERROR] $Message" -ForegroundColor Red
}

function Write-ProcessLogsIfPresent {
    param(
        [string] $StdOutPath,
        [string] $StdErrPath
    )

    if ((Test-Path -LiteralPath $StdOutPath) -and ((Get-Item -LiteralPath $StdOutPath).Length -gt 0)) {
        Write-Host "--- AgentRouter stdout ---" -ForegroundColor DarkCyan
        Get-Content -LiteralPath $StdOutPath -Tail 200 | Write-Host
    }

    if ((Test-Path -LiteralPath $StdErrPath) -and ((Get-Item -LiteralPath $StdErrPath).Length -gt 0)) {
        Write-Host "--- AgentRouter stderr ---" -ForegroundColor DarkCyan
        Get-Content -LiteralPath $StdErrPath -Tail 200 | Write-Host
    }
}

function Get-ListenerProcessId {
    param([int] $Port)

    $connection = Get-NetTCPConnection `
        -LocalPort $Port `
        -State Listen `
        -ErrorAction SilentlyContinue |
        Select-Object -First 1

    if ($connection -eq $null) {
        return $null
    }

    return $connection.OwningProcess
}

function Test-Endpoint {
    param([string] $Uri)

    try {
        Invoke-RestMethod -Uri $Uri -Method Get -TimeoutSec 5 | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

function Wait-Endpoint {
    param(
        [string] $Name,
        [string] $Uri,
        [int] $TimeoutSeconds
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (Test-Endpoint -Uri $Uri) {
            Write-Good "$Name is ready at $Uri"
            return $true
        }

        Start-Sleep -Milliseconds 500
    }

    Write-Bad "$Name did not become ready at $Uri within $TimeoutSeconds seconds."
    return $false
}

function Start-VisiblePowerShell {
    param(
        [string] $Title,
        [string] $Command,
        [string] $PidFileName
    )

    $escapedTitle = $Title.Replace("'", "''")
    $wrappedCommand = @"
`$Host.UI.RawUI.WindowTitle = '$escapedTitle'
Set-Location '$repoRoot'
$Command
"@

    $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($wrappedCommand))

    $process = Start-Process `
        -FilePath "powershell.exe" `
        -ArgumentList "-NoExit -NoProfile -ExecutionPolicy Bypass -EncodedCommand $encodedCommand" `
        -WorkingDirectory $repoRoot `
        -PassThru

    $pidPath = Join-Path $pidDirectory $PidFileName
    Set-Content -Path $pidPath -Value $process.Id -Encoding ASCII

    Write-Good "Started $Title in a new PowerShell window. PID: $($process.Id)"
}

function Start-BackgroundCommand {
    param(
        [string] $Name,
        [string] $FilePath,
        [string] $Arguments,
        [string] $PidFileName,
        [string] $StdOutName,
        [string] $StdErrName
    )

    $stdout = Join-Path $logDirectory $StdOutName
    $stderr = Join-Path $logDirectory $StdErrName

    $process = Start-Process `
        -FilePath $FilePath `
        -ArgumentList $Arguments `
        -WorkingDirectory $repoRoot `
        -RedirectStandardOutput $stdout `
        -RedirectStandardError $stderr `
        -WindowStyle Hidden `
        -PassThru

    $pidPath = Join-Path $pidDirectory $PidFileName
    Set-Content -Path $pidPath -Value $process.Id -Encoding ASCII

    Write-Good "Started $Name in the background. PID: $($process.Id)"
    Write-Host "stdout: $stdout"
    Write-Host "stderr: $stderr"
}

Write-Section "MCPServer AgentRouter stack startup"
Write-Host "RepoRoot:        $repoRoot"
Write-Host "RouterBaseUrl:   $RouterBaseUrl"
Write-Host "OllamaBaseUrl:   $OllamaBaseUrl"
Write-Host "RunStorageRoot:  $resolvedRunStorageRoot"
Write-Host "Configuration:   $Configuration"

Write-Section "Ollama"

$ollamaPid = Get-ListenerProcessId -Port $ollamaPort

if ($ollamaPid -ne $null) {
    Write-Good "Ollama already appears to be listening on port $ollamaPort. PID: $ollamaPid"
}
else {
    if ($NoNewWindows) {
        Start-BackgroundCommand `
            -Name "Ollama" `
            -FilePath "ollama" `
            -Arguments "serve" `
            -PidFileName "ollama.pid" `
            -StdOutName "ollama.out.log" `
            -StdErrName "ollama.err.log"
    }
    else {
        Start-VisiblePowerShell `
            -Title "Ollama Serve" `
            -Command "ollama serve" `
            -PidFileName "ollama.pid"
    }

    if (-not (Wait-Endpoint -Name "Ollama" -Uri "$OllamaBaseUrl/api/tags" -TimeoutSeconds $StartupTimeoutSeconds)) {
        exit 1
    }
}

if (-not (Wait-Endpoint -Name "Ollama" -Uri "$OllamaBaseUrl/api/tags" -TimeoutSeconds $StartupTimeoutSeconds)) {
    exit 1
}

if ($RequiredModels.Count -gt 0) {
    Write-Host "Checking required models..."

    $tags = Invoke-RestMethod -Uri "$OllamaBaseUrl/api/tags" -Method Get
    $existingModelNames = @()

    if ($tags.models -ne $null) {
        foreach ($model in $tags.models) {
            $existingModelNames += [string] $model.name
        }
    }

    foreach ($modelName in $RequiredModels) {
        if ($existingModelNames -contains $modelName) {
            Write-Good "Model available: $modelName"
        }
        else {
            Write-Warn "Model missing: $modelName"
            Write-Host "Pulling $modelName..."
            & ollama pull $modelName
        }
    }
}

if ($Build) {
    Write-Section "Build"
    dotnet build $SolutionPath -c $Configuration
}

Write-Section "AgentRouter"

$routerPid = Get-ListenerProcessId -Port $routerPort

if ($routerPid -ne $null) {
    if ($RunSmoke) {
        Write-Bad "AgentRouter is already listening on port $routerPort. Full smoke coverage requires a fresh AgentRouter process so the smoke allowlist can be applied."
        exit 1
    }

    Write-Warn "Something is already listening on port $routerPort. PID: $routerPid"
    Write-Host "Reusing existing AgentRouter listener."
}
else {
    $routerCommand = @"
$routerEnvironmentBlock
dotnet run --project '$AgentRouterProjectPath' -c '$Configuration'
"@

    $routerProcessId = $null

    if ($NoNewWindows) {
        $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($routerCommand))
        Start-BackgroundCommand `
            -Name "AgentRouter" `
            -FilePath "powershell.exe" `
            -Arguments "-NoProfile -ExecutionPolicy Bypass -EncodedCommand $encoded" `
            -PidFileName "agentrouter.pid" `
            -StdOutName "agentrouter.out.log" `
            -StdErrName "agentrouter.err.log"

        $routerProcessId = Get-ListenerProcessId -Port $routerPort
    }
    else {
        Start-VisiblePowerShell `
            -Title "MCPServer AgentRouter" `
            -Command $routerCommand `
            -PidFileName "agentrouter.pid"
    }
}

if (-not (Wait-Endpoint -Name "AgentRouter" -Uri "$RouterBaseUrl/health" -TimeoutSeconds $StartupTimeoutSeconds)) {
    if ($routerProcessId -ne $null) {
        Write-Warn "AgentRouter process PID: $routerProcessId"
    }

    Write-ProcessLogsIfPresent `
        -StdOutPath (Join-Path $logDirectory "agentrouter.out.log") `
        -StdErrPath (Join-Path $logDirectory "agentrouter.err.log")

    exit 1
}

Write-Section "Verification"

try {
    $models = Invoke-RestMethod -Uri "$RouterBaseUrl/v1/models" -Method Get
    Write-Good "AgentRouter /v1/models responded."
    $models.data | Select-Object id, object | Format-Table | Out-String | Write-Host
}
catch {
    Write-Bad "AgentRouter /v1/models failed: $($_.Exception.Message)"
    exit 1
}

try {
    $tools = Invoke-RestMethod -Uri "$RouterBaseUrl/agent/mcp/tools" -Method Get
    Write-Good "AgentRouter /agent/mcp/tools responded."
    $tools | Select-Object status, transport, protocolVersion, toolCount | Format-List | Out-String | Write-Host
}
catch {
    Write-Warn "AgentRouter /agent/mcp/tools failed: $($_.Exception.Message)"
}

if ($RunSmoke) {
    Write-Section "Smoke test"

    $smokeArguments = @(
        "smoke",
        "--router-base-url",
        $RouterBaseUrl
    )

    if ($EnableSshSmoke) {
        $smokeArguments += @(
            "--enable-ssh",
            "--ssh-profile",
            $SshProfile,
            "--ssh-command",
            $SshCommand,
            "--ssh-working-directory",
            $SshWorkingDirectory
        )
    }

    & dotnet run --project $AgentRouterToolsProjectPath -c $Configuration -- @smokeArguments

    if ($LASTEXITCODE -ne 0) {
        Write-Bad "Typed smoke harness failed with exit code $LASTEXITCODE."
        exit $LASTEXITCODE
    }
}

Write-Section "Stack ready"
Write-Good "Ollama:      $OllamaBaseUrl"
Write-Good "AgentRouter: $RouterBaseUrl"
Write-Host ""
Write-Host "Stop with:"
Write-Host "  powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Stop-AgentRouterStack.ps1"
