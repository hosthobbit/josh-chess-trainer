#!/usr/bin/env bash
# Regenerates all assets + scene and builds the Linux player.
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
mkdir -p Logs
# the grandmaster's voice ships inside the build
[[ -x Tools/piper/piper ]] || Tools/setup_piper.sh
UNITY="${UNITY:-$HOME/Unity/Hub/Editor/6000.6.3f1/Editor/Unity}"
exec "$UNITY" -batchmode -nographics -projectPath "$PWD" -executeMethod Kingsfall.EditorTools.KingsfallBuilder.GenerateAndBuild -quit -logFile "$PWD/Logs/build.log"
