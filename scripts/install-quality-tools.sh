#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cd "$repo_root"
dotnet tool restore

if ! dotnet --list-sdks | grep -q '^10\.'; then
  echo "Stryker.NET 5.0.0 requires a .NET 10 SDK/runtime." >&2
  exit 1
fi

temporary_directory="$(mktemp -d)"
trap 'rm -rf "$temporary_directory"' EXIT

if [[ -x "$repo_root/.tools/dotnet-stryker" ]]; then
  (
    cd "$temporary_directory"
    dotnet tool update dotnet-stryker \
      --tool-path "$repo_root/.tools" \
      --version 5.0.0
  )
else
  (
    cd "$temporary_directory"
    dotnet tool install dotnet-stryker \
      --tool-path "$repo_root/.tools" \
      --version 5.0.0
  )
fi
