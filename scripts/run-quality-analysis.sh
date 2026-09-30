#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

bash "$repo_root/scripts/install-quality-tools.sh"
cd "$repo_root"

mkdir -p artifacts/quality/crap artifacts/quality/stryker

set +e
dotnet dotnet-crap analyze DesktopComputerUse.sln \
  --run-tests \
  --threshold 20 \
  --min-crap 0 \
  --output artifacts/quality/crap/report.json
crap_exit=$?
set -e

.tools/dotnet-stryker \
  --test-project tests/DesktopComputerUse.Automation.Tests/DesktopComputerUse.Automation.Tests.csproj \
  --project src/DesktopComputerUse.Automation/DesktopComputerUse.Automation.csproj \
  --target-framework net8.0-windows \
  --mutate '**/Resolution/*.cs' \
  --mutate '**/Profiles/*.cs' \
  --mutate '**/Applications/ApplicationProfileStore.cs' \
  --mutate '**/Applications/ApplicationProfileValidator.cs' \
  --mutate '**/Applications/WindowSelectorMatcher.cs' \
  --mutate '**/AutomationExceptionResultMapper.cs' \
  --mutate '**/Discovery/NearbyLabelGeometry.cs' \
  --reporter ClearText \
  --reporter Json \
  --output artifacts/quality/stryker \
  --concurrency 2 \
  --skip-version-check \
  --break-at 80 \
  --threshold-low 80 \
  --threshold-high 90

if [[ "$crap_exit" -ne 0 ]]; then
  echo "CRAP4NET found methods above the configured threshold; see artifacts/quality/crap/report.json." >&2
fi
