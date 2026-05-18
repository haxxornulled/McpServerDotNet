[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$ServerName = 'mcpserver',
    [ValidateSet('http','stdio')][string]$Type = 'stdio',
    [string]$Url = '',
    [string]$Command = 'mcpserver',
    [string[]]$Args = @(),
    [ValidateSet('Code','Code - Insiders','All')][string]$Profile = 'Code',
    [string]$AppData = $env:APPDATA
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-VscodeMcpPath {
    param(
        [Parameter(Mandatory = $true)][string]$BasePath,
        [Parameter(Mandatory = $true)][string]$ProfileName
    )

    $userDir = Join-Path -Path $BasePath -ChildPath "$ProfileName\User"
    if (-not (Test-Path -Path $userDir)) {
        New-Item -ItemType Directory -Path $userDir -Force | Out-Null
    }

    return Join-Path -Path $userDir -ChildPath 'mcp.json'
}

function Load-McpConfig {
    param([string]$Path)

    if (Test-Path -Path $Path) {
        $raw = Get-Content -Path $Path -Raw
        if ([string]::IsNullOrWhiteSpace($raw)) {
            return @{}
        }

        try {
            return $raw | ConvertFrom-Json -AsHashtable
        } catch {
            throw "Failed to parse existing mcp.json at '$Path': $_"
        }
    }

    return @{}
}

function Save-McpConfig {
    param(
        [string]$Path,
        [hashtable]$Config
    )

    $Config | ConvertTo-Json -Depth 10 | Set-Content -Path $Path -Encoding UTF8
}

function Build-ServerConfig {
    param(
        [string]$Type,
        [string]$Url,
        [string]$Command,
        [string[]]$Args
    )

    $config = @{ type = $Type }
    if ($Type -eq 'http') {
        if ([string]::IsNullOrWhiteSpace($Url)) {
            throw 'A URL must be provided for http servers.'
        }
        $config['url'] = $Url
    } else {
        if ([string]::IsNullOrWhiteSpace($Command)) {
            throw 'A command must be provided for stdio servers.'
        }

        $resolvedCommand = Get-Command -Name $Command -ErrorAction SilentlyContinue
        if ($null -ne $resolvedCommand) {
            $config['command'] = $resolvedCommand.Name
        } else {
            $config['command'] = $Command
        }

        if ($Args.Count -gt 0) {
            $config['args'] = $Args
        }
    }

    return $config
}

$targets = @()
if ($Profile -eq 'All' -or $Profile -eq 'Code') {
    $targets += 'Code'
}
if ($Profile -eq 'All' -or $Profile -eq 'Code - Insiders') {
    $targets += 'Code - Insiders'
}

$serverConfig = Build-ServerConfig -Type $Type -Url $Url -Command $Command -Args $Args

foreach ($target in $targets) {
    $path = Resolve-VscodeMcpPath -BasePath $AppData -ProfileName $target
    $config = Load-McpConfig -Path $path

    if (-not $config.ContainsKey('servers')) {
        $config['servers'] = @{}
    }

    if ($PSCmdlet.ShouldProcess($path, "Install or update VS Code MCP server '$ServerName'")) {
        $config['servers'][$ServerName] = $serverConfig
        Save-McpConfig -Path $path -Config $config
    }

    Write-Host "Installed or updated '$ServerName' in $path"
}
