# DiceKingdoms Island Tools (`DiceModders.IslandTools.dll`)

In-game island editor and players window for **Dice Kingdoms** (BepInEx 6 IL2CPP).

## What it does

* **Island Editor**: open it with the **hammer button** in the game's top bar (during a match) or from **Settings > Game > Island Tools**. Paint land and nature (point, fill, line, rect, square, circle, oval, spray, symmetry, coast tools), place and remove buildings and **hills**, check them for problems, load an island from the game or the clipboard, and apply it back. An island of another size (for example a normal 49 x 49 code in a match with bigger islands) is placed in the middle of the target grid; buildings outside a smaller grid are left out, and the old hills are removed first so no stray land is left behind. If the game refuses the island halfway (a damaged code, or a building it does not accept), the island is put back as it was and the status line says so. The window can be dragged, resized (- / + in its title bar) and switched to full screen; **Help** opens an 8-page tutorial.
* **Map**: drawn at the screen's own resolution, so it stays sharp at any window size. The **mouse wheel** zooms around the pointer, dragging with the **right or middle button** moves the view, **Fit map** shows the whole island; big islands (up to 201 x 201) are as easy to edit as normal ones.
* **Hills**: the *Hills* layer places the game's hill pieces (Hill 3x3 and 2x2, Slope 2x2, 1x2 and 1x1), rotated and mirrored like buildings, each with its own 3D model in the game. Hills painted with the Ground brush *Inner cliff* stay invisible in the game but still block building (striped on the map); **Make painted hills visible** covers them with pieces, big ones first. A new piece cannot be placed on another piece; *Check buildings* also reports hill pieces outside the island, not on hill ground or overlapping, and hill tiles without a model. Hill pieces the game has no model for are left out when an island is applied.
* **Island codes** through the clipboard (*Copy code* / *Paste code*). They are the same codes as in the browser editor `tools/island-editor.html` (open it in any browser).
* **Keys** while the editor is open: **R** rotates / **T** mirrors the building, **Ctrl+Z** / **Ctrl+Y** undo / redo. They are rebindable in Settings > Input > Mod keys; **Escape** closes the front window (UiKit, for every mod window).
* **Players window** (*Players...* in the editor, or Settings): one card per player of the match with a picture of their island, a tick box (*All / None / Only me*), and **Edit** / **Copy code**; click a picture to open that island in the editor. Actions on every ticked player at once: *Apply editor island*, *Coast Grow / Shrink / Outline*, *Clear nature under buildings*, *All HP default*, *Copy codes*. Before every change each island is kept as a backup; *Undo (backups)* puts it back (until the match ends). The pictures are redrawn when the window opens, at every turn phase, after a change, and with *Refresh*.

Changing **another player's** island (*Apply to SELECTED*, the players window) is experimental: it only has a chance to reach the other players when you are the host, and it can desynchronise the game.

The whole mod can be switched off in the mod list.

### Settings > Game > Island Tools

| Option | Effect |
|---|---|
| **Windows** | open the editor, the players window or the tutorial |
| **Top bar button** / **outside matches** | show the hammer button (during a match / also in the menu and lobby) |
| **Allow every placement while applying** | while an island is applied (and only then), buildings the game thinks do not fit the current terrain are allowed, so one bad building does not stop the import halfway. The log lists each one |

## Code map

| Folder / file | Job |
|---|---|
| `Plugin.cs`, `Options.cs` | the plugin and its options |
| `Island/Islands.cs` | the players' islands in the match: read one as an island code, apply an island code |
| `Island/ImportGuard.cs` | the placement checks while applying |
| `Island/Buildings.cs` | the building table, read from the game |
| `Island/IslandData.cs`, `Island/BuildingTable.cs` | the island code format and the building table (plain C#) |
| `Island/Clipboard.cs` | the clipboard (Unity's own) |
| `Editor/EditorModel.cs` | every editing tool (plain C#) |
| `Editor/EditorWindow.cs` | the editor window |
| `Editor/MapView.cs` | the map picture: drawing at screen resolution, zoom and pan |
| `Ui/Windows.cs`, `Ui/EditorKeys.cs` | the top bar button, the editing keys |
| `Ui/PlayersWindow.cs`, `Ui/HelpWindow.cs`, `Ui/GameSettings.cs` | the players window, the tutorial, the Settings block |

Nothing runs every frame: the windows redraw after the click or pointer event that changed something, the keys are called by the game's input system, and the button follows the match events.

## Requirements

* Dice Kingdoms with **BepInEx 6 IL2CPP (x64)**: see the wiki's [Mods Installation](https://dicemodders.github.io/DiceKingdoms-Modding-Wiki/guides/mod-installation/) guide. Start the game once so BepInEx creates its folders.
* The DiceModders libraries in `BepInEx\plugins`: [ModCore](https://github.com/DiceModders/DiceKingdoms-ModCore) (`DiceModders.ModCore.dll`), [UiKit](https://github.com/DiceModders/UiKit) (`DiceModders.UiKit.dll`) and [ModBridge](https://github.com/DiceModders/DiceKingdoms-ModBridge) (`DiceModders.ModBridge.dll`, shows ModCore's mod list and lobby rules on the game's screens and the Settings > Input screen for the keys). This mod does not contain copies of them.

## Install (players)

Copy `DiceModders.IslandTools.dll` into `BepInEx\plugins`, next to the libraries. Settings are kept in `BepInEx\config\org.dicemodders.dicekingdoms.islandtools.cfg`.

## Build (developers)

Needs the .NET SDK (6 or newer) and PowerShell. Game files are never committed: the script links `BepInEx\interop` from your game folder and copies the libraries from `BepInEx\plugins` into `libs`.

```powershell
.\scripts\build.ps1 -GameDir "<your Dice-Kingdoms folder>"   # first time: remembers the folder
.\scripts\build.ps1                                           # build + install (Release, console and log OFF)
.\scripts\build.ps1 -Dev -Run                                 # Debug build, console and log ON, then start the game
```

Close the game before building: it locks the DLLs. If PowerShell refuses to run scripts: `powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1`.

## License

MIT, see [LICENSE](LICENSE).
