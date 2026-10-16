#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
if [[ ! -x Build/Linux/JoshsChessTrainer.x86_64 ]]; then echo "Build missing - run ./build.sh" >&2; exit 1; fi
mkdir -p Logs
exec ./Build/Linux/JoshsChessTrainer.x86_64 -logFile "$PWD/Logs/player.log" "$@"
