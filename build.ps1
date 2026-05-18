param(
    [ValidateSet("restore", "format", "build", "test", "all")]
    [string]$Target = "all"
)

$ErrorActionPreference = "Stop"

switch ($Target) {
    "restore" { dotnet restore }
    "format"  { dotnet format }
    "build"   { dotnet build -c Release }
    "test"    { dotnet test -c Release }
    "all" {
        dotnet restore
        dotnet format
        dotnet build -c Release
        dotnet test -c Release
    }
}
