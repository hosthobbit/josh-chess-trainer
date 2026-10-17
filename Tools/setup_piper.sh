#!/usr/bin/env bash
# Downloads the Piper text-to-speech engine and voices (not kept in git, ~380 MB):
#   Tools/piper/                    engine + all voices, used by Tools/make_voices.py
#   Assets/StreamingAssets/Piper/   engine + the grandmaster's voice, shipped in the build
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
P=Tools/piper
VOICES="en/en_GB/alan/medium/en_GB-alan-medium
en/en_GB/jenny_dioco/medium/en_GB-jenny_dioco-medium
en/en_GB/northern_english_male/medium/en_GB-northern_english_male-medium
en/en_GB/semaine/medium/en_GB-semaine-medium"
if [[ ! -x $P/piper ]]; then
    mkdir -p Tools
    curl -fsSL https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_linux_x86_64.tar.gz | tar xz -C Tools
fi
for v in $VOICES; do
    n=$(basename "$v")
    for ext in onnx onnx.json; do
        [[ -s $P/$n.$ext ]] || curl -fsSL -o "$P/$n.$ext" "https://huggingface.co/rhasspy/piper-voices/resolve/main/$v.$ext"
    done
done
D=Assets/StreamingAssets/Piper
mkdir -p "$D"
cp -L $P/piper $P/libespeak-ng.so.1 $P/libpiper_phonemize.so.1 $P/libonnxruntime.so.1.14.1 \
      $P/en_GB-alan-medium.onnx $P/en_GB-alan-medium.onnx.json "$D"/
rm -rf "$D/espeak-ng-data" && cp -r $P/espeak-ng-data "$D"/
echo "Piper ready: $P and $D"
