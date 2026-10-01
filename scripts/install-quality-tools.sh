#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cd "$repo_root"

crap_revision="9e155ec73008af7314b78b8e7f51e5edc2fd2a41"
crap_repository="$repo_root/.tools/crap4csharp"
if [[ ! -d "$crap_repository" ]]; then
  git clone --quiet --no-checkout https://github.com/microsoft/crap4csharp.git "$crap_repository"
  git -C "$crap_repository" checkout --quiet --detach "$crap_revision"
fi
if [[ "$(git -C "$crap_repository" rev-parse HEAD)" != "$crap_revision" ]]; then
  echo "Microsoft CRAP source must be pinned to $crap_revision; refusing to overwrite another checkout." >&2
  exit 1
fi

if ! dotnet --list-sdks | grep -q '^10\.'; then
  echo "Stryker.NET 5.0.0 requires a .NET 10 SDK/runtime." >&2
  exit 1
fi

(
  cd "$repo_root/scripts/QualityAnalysis"
  dotnet build --configuration Release --verbosity quiet
)

temporary_directory="$(mktemp -d)"
trap 'rm -rf "$temporary_directory"' EXIT

if [[ ! -x "$repo_root/.tools/dotnet-stryker" ]]; then
  (
    cd "$temporary_directory"
    dotnet tool install dotnet-stryker \
      --tool-path "$repo_root/.tools" \
      --version 5.0.0
  )
fi
