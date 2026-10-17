#!/usr/bin/env python3
"""Generates the captured-piece voice lines with Piper and writes them into
Assets/Kingsfall/Resources/Voices as WAV clips + lines.txt (file|text).

Each capture plays one "last words" line as the battle starts and one short
cry at the moment the piece is destroyed. Run after editing the lines below:
    python3 Tools/make_voices.py
"""
import os, shutil, subprocess, sys, tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PIPER = ROOT / "Tools/piper/piper"
VOICES = ROOT / "Tools/piper"
OUT = ROOT / "Assets/Kingsfall/Resources/Voices"

# voice per piece: (model, speaker id or None, pitch factor, tempo factor)
CAST = {
    "pawn":   ("en_GB-semaine-medium", 3, 1.32, 1.08),   # poppy, chipmunked
    "knight": ("en_GB-semaine-medium", 1, 1.0, 1.05),    # spike
    "bishop": ("en_GB-semaine-medium", 2, 0.94, 0.95),   # obadiah, pompous
    "rook":   ("en_GB-northern_english_male-medium", None, 0.78, 0.95),
    "queen":  ("en_GB-jenny_dioco-medium", None, 1.05, 1.0),
    "king":   ("en_GB-semaine-medium", 2, 0.82, 0.9),
}

LINES = {
    "pawn": [
        "Wait! I was going to be a queen one day!",
        "Tell my mum I was a good pawn!",
        "I'm only little!",
        "Not fair! I can only go forwards!",
        "I knew I should have stayed at home.",
        "Is this what they meant by pawn sacrifice?",
        "Nobody ever remembers the pawns!",
        "I didn't even get my lunch break!",
        "Can I have a do-over?",
        "Mummyyyy!",
    ],
    "knight": [
        "Neigh! I mean, no!",
        "Hold your horses!",
        "I should have galloped the other way!",
        "Not the face, not the face!",
        "This is the worst L shaped day ever!",
        "My horse is scared and so am I!",
        "I only jumped in to help!",
        "Whoa there, let's talk about this!",
    ],
    "bishop": [
        "This is most irregular!",
        "I shall be writing a strongly worded letter!",
        "I only ever move diagonally, how did I end up here?",
        "Oh dear, oh dear, oh dear!",
        "Have mercy! I've got a sermon on Sunday!",
        "Well, this is frightfully rude.",
        "I say! Unhand me at once!",
        "Bless me, I am done for!",
    ],
    "rook": [
        "I'm a castle! You can't knock down a castle!",
        "I've been standing in that corner all game!",
        "Ow! My battlements!",
        "Did anybody check the castle insurance?",
        "I should have castled when I had the chance.",
        "Crumbling! I'm crumbling!",
        "Me walls! Me lovely walls!",
        "Right, that's it, I'm going back in the box.",
    ],
    "queen": [
        "How dare you! I am the queen!",
        "Do you know who I am?",
        "Not my crown! It took ages to polish!",
        "The king is never going to manage without me.",
        "This is absolutely not in my diary.",
        "Guards! Guards! Anybody?",
        "You will regret this, you peasant!",
        "Fine. Somebody else can do all the work now.",
    ],
    "king": [
        "This is not how the story was supposed to end!",
        "But I'm the king!",
        "I demand a recount!",
        "Somebody fetch my queen!",
    ],
}

CRIES = {
    "pawn":   ["Eeeeek!", "Waaaah!", "Nooooo!", "Ow ow ow!", "Yikes!"],
    "knight": ["Aaaargh!", "Neeeeigh!", "Whoa!", "Oof!"],
    "bishop": ["Good heavens!", "Oh my!", "Gaaah!", "Heavens above!"],
    "rook":   ["Ooooof!", "Aaargh!", "Timber!", "Ugh!"],
    "queen":  ["Aaaaah!", "How rude!", "Nooooo!", "Eeeek!"],
    "king":   ["Noooooo!", "My kingdom!", "Aaaargh!"],
}


def synth(text, model, speaker, pitch, tempo, dest):
    with tempfile.TemporaryDirectory() as tmp:
        raw = Path(tmp) / "raw.wav"
        cmd = [str(PIPER), "-m", str(VOICES / f"{model}.onnx"), "-f", str(raw), "-q"]
        if speaker is not None:
            cmd += ["-s", str(speaker)]
        subprocess.run(cmd, input=text.encode(), check=True, capture_output=True)
        # pitch shift by resampling, then restore/adjust the speed with atempo
        rate = 22050
        filt = f"asetrate={int(rate * pitch)},aresample={rate},atempo={tempo / pitch:.4f}," \
               "highpass=f=70,loudnorm=I=-16:TP=-1.5:LRA=11"
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", str(raw), "-af", filt,
                        "-ar", str(rate), "-ac", "1", "-c:a", "pcm_s16le", str(dest)], check=True)


def main():
    if not PIPER.exists():
        sys.exit("piper missing: see Tools/README in project README")
    if OUT.exists():
        for f in OUT.glob("*.wav*"):
            f.unlink()
    OUT.mkdir(parents=True, exist_ok=True)
    index = []
    for kind, table in (("line", LINES), ("cry", CRIES)):
        for piece, lines in table.items():
            model, spk, pitch, tempo = CAST[piece]
            for i, text in enumerate(lines):
                name = f"{piece}_{kind}_{i:02d}"
                synth(text, model, spk, pitch, tempo, OUT / f"{name}.wav")
                index.append(f"{name}|{text}")
                print(name, text)
    (OUT / "lines.txt").write_text("\n".join(index) + "\n")
    print(f"{len(index)} clips -> {OUT}")


if __name__ == "__main__":
    main()
