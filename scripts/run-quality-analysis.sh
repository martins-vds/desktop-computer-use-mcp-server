#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
quality_directory="${1:-artifacts/quality}"

cd "$repo_root"

crap_runner="$repo_root/scripts/QualityAnalysis/bin/Release/net10.0/QualityAnalysis.dll"
stryker_runner="$repo_root/.tools/dotnet-stryker"
if [[ -x "$stryker_runner.exe" ]]; then
  stryker_runner="$stryker_runner.exe"
fi
if [[ ! -f "$crap_runner" ]] ||
   [[ ! -x "$stryker_runner" ]]; then
  echo "Quality tools are missing. Run scripts/install-quality-tools.sh first." >&2
  exit 1
fi

mkdir -p "$quality_directory/crap" "$quality_directory/stryker" "$quality_directory/coverage"

dotnet build DesktopComputerUse.sln --no-incremental --verbosity quiet
coverage_directory="$(mktemp -d "$quality_directory/coverage/run-XXXXXX")"
dotnet test DesktopComputerUse.sln \
  --no-build \
  --collect:"XPlat Code Coverage" \
  --results-directory "$coverage_directory" \
  --verbosity minimal
python3 scripts/merge-cobertura.py "$coverage_directory" "$quality_directory/coverage/merged.cobertura.xml"

set +e
dotnet "$crap_runner" src \
  "$quality_directory/coverage/merged.cobertura.xml" \
  "$quality_directory/crap/report.json"
crap_exit=$?
set -e

set +e
"$stryker_runner" \
  --test-project tests/DesktopComputerUse.Automation.Tests/DesktopComputerUse.Automation.Tests.csproj \
  --test-project tests/DesktopComputerUse.Native.Tests/DesktopComputerUse.Native.Tests.csproj \
  --project src/DesktopComputerUse.Automation/DesktopComputerUse.Automation.csproj \
  --target-framework net10.0-windows \
  --mutation-level Complete \
  --mutate '**/Resolution/*.cs' \
  --mutate '**/Profiles/*.cs' \
  --mutate '**/Applications/ApplicationProfileStore.cs' \
  --mutate '**/Applications/ApplicationProfileValidator.cs' \
  --mutate '**/Applications/WindowSelectorMatcher.cs' \
  --mutate '**/AutomationExceptionResultMapper.cs' \
  --mutate '**/Discovery/NearbyLabelGeometry.cs' \
  --mutate '**/Discovery/ApplicationSnapshotBuilder.cs' \
  --mutate '**/FlaUi/ControlObserver.cs' \
  --mutate '**/FlaUi/ControlValueReader.cs' \
  --mutate '**/FlaUi/ProviderActionExecutor.cs' \
  --mutate '**/FlaUi/SafeAutomationElementReader.cs' \
  --mutate '**/FlaUi/SafeAutomationTraversal.cs' \
  --mutate '**/CapturePrivacy.cs' \
  --mutate '**/Selectors/*.cs' \
  --mutate '**/Applications/*.cs' \
  --mutate '**/Windows/*.cs' \
  --mutate '**/DesktopAutomationController*.cs' \
  --reporter ClearText \
  --reporter Json \
  --output "$quality_directory/stryker" \
  --concurrency 2 \
  --skip-version-check \
  --break-at 80 \
  --threshold-low 80 \
  --threshold-high 90
automation_mutation_exit=$?

"$stryker_runner" \
  --test-project tests/DesktopComputerUse.Automation.Tests/DesktopComputerUse.Automation.Tests.csproj \
  --test-project tests/DesktopComputerUse.Native.Tests/DesktopComputerUse.Native.Tests.csproj \
  --project src/DesktopComputerUse.Contracts/DesktopComputerUse.Contracts.csproj \
  --target-framework net10.0 \
  --mutation-level Complete \
  --mutate '**/*.cs' \
  --reporter ClearText \
  --reporter Json \
  --output "$quality_directory/stryker-contracts" \
  --concurrency 2 \
  --skip-version-check \
  --break-at 80 \
  --threshold-low 80 \
  --threshold-high 90
contracts_mutation_exit=$?
set -e

if [[ "$crap_exit" -ne 0 ]]; then
  echo "Microsoft CRAP found members at or above 20; see $quality_directory/crap/report.json." >&2
fi

if [[ "$crap_exit" -ne 0 || "$automation_mutation_exit" -ne 0 || "$contracts_mutation_exit" -ne 0 ]]; then
  exit 1
fi
