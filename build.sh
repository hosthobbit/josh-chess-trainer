#!/usr/bin/env bash
# Regenerates all assets + scene and builds the Linux player.
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
mkdir -p Logs
exec /home/mike/Unity/Hub/Editor/6000.6.3f1/Editor/Unity -batchmode -projectPath "$PWD" -executeMethod Kingsfall.EditorTools.KingsfallBuilder.GenerateAndBuild -quit -logFile "$PWD/Logs/build.log"
