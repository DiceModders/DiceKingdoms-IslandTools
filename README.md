# DiceKingdoms Island Tools (`DiceModders.IslandTools.dll`)

In-game island editor and island save / load for Dice Kingdoms. A mod for **Dice Kingdoms** (BepInEx 6 IL2CPP). It is one self-contained project: it shares no code with other mods.

## What it does

* **Island Editor** - open it with the **hammer button** in the game's top-right button bar (during a match), **F9** (configurable) or Right Ctrl + E. Paint land and nature (point, fill, line, rect, square, circle, oval, spray, symmetry, coast tools), place and remove buildings, check them for problems, load an island from the game / the clipboard, and apply it back. The window can be dragged and switched to full screen from its title bar; **Help** opens an 8-page tutorial.
* **Save / load your island** through the real Windows clipboard (Right Ctrl + K / L). Island codes are the same as in the browser editor `tools/island-editor.html`.
* Pick **another player's island** as source or target (host experiment, risky and untested).
* **Island radius** (`[Island] HardLimitRadius`) - land radius applied when an island is generated. Everybody in the lobby must use the same value.

Keys while the editor is open: Esc closes it (tutorial first), R rotates / T mirrors the building brush, Ctrl+Z / Ctrl+Y undo / redo.

### Players dashboard (2.1.0)

The **Players...** button in the editor (or the `OpenPlayers` hotkey) opens a dashboard with one card per player of the match:

* a **live picture of their island** (refreshed one player at a time, about twice a second in total; switch off with *Live pictures*),
* a tick box per player (*All / None / Only me*), and **Edit** / **Copy code** per player - click the picture to open that island in the editor,
* bulk actions on all **ticked** players at once: *Apply editor island*, *Coast Grow / Shrink / Outline*, *Clear nature under buildings*, *All HP default*, *Copy codes* (one per line).
  Before every change the island of each player is saved as a backup, *Undo (backups)* puts it back (backups are kept until the next match).

Changing other players' islands is experimental and may only work for the host (see the notes above).

## Hotkeys (hold Right Ctrl; configurable in the .cfg)

| Key | Action |
|---|---|
| E | open / close the editor |
| K / L | save my island to / load an island from the clipboard |
| Home | print the hotkeys to the log |

## Config highlights

`[Editor] WindowWidth / WindowHeight`, `ShowLauncher`, `LauncherAlways`, `LauncherOrder`, `OpenKey`, `KeyboardShortcuts`; `[Island] HardLimitRadius`, `LocalPlayerName`, `BypassPlacementWhileImporting`.

## Code map

`ModPlugin.cs`, `EditorUi.cs` (open / close, keys, top-bar button), `EditorWindow.cs` (the editor, via UiKit), `PlayersWindow.cs` (the players dashboard), `EditorHelp.cs` (tutorial), `EditorModel.cs` / `IslandData.cs` / `BuildingTable.cs` (pure logic, no Unity), `IslandOps.cs`, `Clipboard.cs`, `BuildingDump.cs`, `Patches.cs`, `State.cs`, `Common/`.
`tools/island-editor.html` is the browser version of the editor (open it in any browser).

## Requirements

* Dice Kingdoms with **BepInEx 6 IL2CPP (x64)** - see the wiki's [Mods Installation](https://dicemodders.github.io/DiceKingdoms-Modding-Wiki/guides/mod-installation/) guide. Start the game once so BepInEx creates its folders.
* **[UiKit](https://github.com/DiceModders/UIKit_Another_Version)** (`DiceModders.UiKit.dll`) in `BepInEx\plugins` - a separate plugin that draws the interface. This mod does **not** contain a copy of it.

## Install (players)

Copy `DiceModders.IslandTools.dll` into `BepInEx\plugins` (next to `DiceModders.UiKit.dll`). Settings are created in `BepInEx\config` (`org.dicemodders.dicekingdoms.islandtools.cfg`).

## Build (developers)

Needs the .NET SDK (6 or newer) and PowerShell. Game files are never committed: the scripts link them from your game folder.

```powershell
.\scripts\link-interop.ps1 "D:\SteamLibrary\steamapps\common\Dice-Kingdoms"   # once: BepInEx\interop -> .\interop
.\scripts\link-uikit.ps1                                                  # once: copies DiceModders.UiKit.dll to .\libs (build UiKit and install it into the game first)
.\scripts\build.ps1 -GameDir "D:\SteamLibrary\steamapps\common\Dice-Kingdoms"   # normal build: Release, installed, console and log OFF (path is remembered)
.\scripts\build-dev.ps1                                                  # development build: Debug (defines DEVLOG), console and log ON
```

If PowerShell refuses to run scripts: `powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1`.

UiKit may be updated; it is kept backward compatible. Re-run `link-uikit.ps1` to build against a newer one.

## License

MIT - see [LICENSE](LICENSE).
