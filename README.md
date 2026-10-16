# Josh's Chess Trainer

3D battle chess built in Unity 6000.6.3f1 (URP). Every mesh, texture, sound and the scene are generated from code.

- Play: `./play.sh` (or the "Josh's Chess Trainer" entry in the app launcher)
- Rebuild everything: `./build.sh` (~3 min; log in `Logs/build.log`)
- Screenshot test run: `./Build/Linux/JoshsChessTrainer.x86_64 --autoshot <dir>`

Controls: left-click to select/move, right-drag or A/D to orbit, scroll to zoom, Space to skip a battle, Esc for the menu, Hint button for the engine's suggestion.

Code: `Assets/Kingsfall/` (internal namespace `Kingsfall`). Rules engine + AI in `Scripts/ChessEngine.cs` / `ChessAI.cs` (perft-verified via `Editor/EngineTests.cs`), capture battles in `Scripts/Choreographer.cs`, asset/scene generator in `Editor/KingsfallBuilder.cs`.

## Fonts

The UI uses Noto Serif, Noto Serif Display and Noto Sans Symbols 2 (`Assets/Kingsfall/Resources/*.ttf`), licensed under the SIL Open Font License 1.1.
