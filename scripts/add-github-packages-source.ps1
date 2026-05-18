# Add-GitHubPackagesSource
# Adds the GitHub Packages NuGet source using the GH_PACKAGE_TOKEN environment variable.
# Usage:
#   $env:GH_PACKAGE_TOKEN = '<NEW_PAT>'
#   .\scripts\add-github-packages-source.ps1 -Username my-github-username

[CmdletBinding()]
Param(
  [Parameter(Mandatory=$false)]
  [string]$Username
)

function Fail([string]$msg) {
  Write-Error $msg
  exit 1
}

$token = $env:GH_PACKAGE_TOKEN
if (-not $token) {
  Fail 'Environment variable GH_PACKAGE_TOKEN is not set. Create a PAT and set it in $env:GH_PACKAGE_TOKEN before running this script.'
}

if (-not $Username) {
  $Username = Read-Host 'GitHub username (will be stored with the source)'
  if (-not $Username) { Fail 'GitHub username is required.' }
}

$sourceName = 'mcpserver-gh'
$sourceUrl = 'https://nuget.pkg.github.com/haxxornulled/index.json'

Write-Host "Registering NuGet source '$sourceName' -> $sourceUrl" -ForegroundColor Cyan

$nuget = Get-Command nuget.exe -ErrorAction SilentlyContinue
if ($nuget) {
  Write-Host 'Using nuget.exe (preferred; stores credentials securely).' -ForegroundColor Green
  & $nuget.Path sources add -Name $sourceName -Source $sourceUrl -UserName $Username -Password $token
  if ($LASTEXITCODE -ne 0) { Fail 'nuget.exe failed to add the source.' }
  Write-Host 'Source added with nuget.exe.' -ForegroundColor Green
} else {
  Write-Host 'nuget.exe not found; falling back to dotnet CLI (may store token in cleartext).' -ForegroundColor Yellow
  dotnet nuget add source $sourceUrl -n $sourceName -u $Username -p $token --store-password-in-clear-text
  if ($LASTEXITCODE -ne 0) { Fail 'dotnet failed to add the source.' }
  Write-Host 'Source added with dotnet CLI.' -ForegroundColor Green
}

# Show configured sources
dotnet nuget list source

Write-Host 'Done. Do NOT commit or echo your token. Consider unsetting the env var:' -ForegroundColor Cyan
Write-Host '  Remove-Item Env:GH_PACKAGE_TOKEN' -ForegroundColor Cyan
