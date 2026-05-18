#!/usr/bin/env bash
set -euo pipefail

target="${1:-all}"

case "$target" in
  restore) dotnet restore ;;
  format)  dotnet format ;;
  build)   dotnet build -c Release ;;
  test)    dotnet test -c Release ;;
  all)
    dotnet restore
    dotnet format
    dotnet build -c Release
    dotnet test -c Release
    ;;
  *) echo "Unknown target: $target"; exit 1 ;;
esac
