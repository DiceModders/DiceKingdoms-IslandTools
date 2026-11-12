using BepInEx.Configuration;

namespace DiceModders.IslandTools;

using Core = DiceModders.ModCore.ModCore;

/// <summary>
/// The editing keys, on the game's own input system through ModCore (rebindable in Settings > Input > Mod keys, stored in [Hotkeys]).
/// Each one only works while its window is open; Unity calls back on the press, nothing is checked every frame.
/// R / T are the game's own rotate / mirror keys of build mode. Escape (closing the front window) is UiKit's, for every mod window.
/// </summary>
internal static class EditorKeys
{
    internal static void Install(ConfigFile config)
    {
        Core.Hotkeys.Add(config, "editorRotate", "Editor: rotate", "R", EditorWindow.KeyRotate, () => EditorWindow.IsOpen);
        Core.Hotkeys.Add(config, "editorMirror", "Editor: mirror", "T", EditorWindow.KeyMirror, () => EditorWindow.IsOpen);
        Core.Hotkeys.Add(config, "editorUndo", "Editor: undo", "Ctrl+Z", EditorWindow.KeyUndo, () => EditorWindow.IsOpen);
        Core.Hotkeys.Add(config, "editorRedo", "Editor: redo", "Ctrl+Y", EditorWindow.KeyRedo, () => EditorWindow.IsOpen);
    }
}
