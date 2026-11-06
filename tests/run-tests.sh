#!/usr/bin/env bash
# Runs the core unit tests with Mono (mcs + mono). No NinjaTrader needed.   bash tests/run-tests.sh
# On Windows, tests\run-tests.ps1 does the same with the compiler that ships with .NET Framework.
set -u
cd "$(dirname "$0")/.."
OUT="${TMPDIR:-/tmp}/dtn-unit"; rm -rf "$OUT"; mkdir -p "$OUT"
mcs -langversion:5 -out:"$OUT/CoreTests.exe" -r:System.Core.dll \
  src/DirectTelegramNotifier/DirectTelegramNotifier.Core.cs tests/CoreTests.cs || { echo "did not compile"; exit 2; }
mono "$OUT/CoreTests.exe"
