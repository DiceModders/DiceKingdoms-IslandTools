using System;
using System.Collections.Generic;
using DiceModders.Shared;
using DiceModders.UiKit;
using UnityEngine;

namespace DiceModders.IslandTools;

// Inside namespace DiceModders.* the bare name "UiKit" would be the NAMESPACE DiceModders.UiKit; this alias (declared inside
// our namespace, so it wins) makes it mean the static class again.
using UiKit = DiceModders.UiKit.UiKit;

/// <summary>Owns the editor and tutorial windows and the top-bar button; called from the plugin's Update.</summary>
internal static class EditorUi
{
    private static EditorWindow _editor;
    private static HelpWindow _help;
    private static PlayersWindow _players;
    private static TopBarButton _button;

    public static bool EditorOpen => _editor != null && _editor.IsOpen;
    public static bool HelpOpen => _help != null && _help.IsOpen;

    public static void Init()
    {
        try
        {
            _button = UiKit.TopBar.Add("islandtools.editor", "hammer", ToggleEditor, "Island Editor", ModPlugin.LauncherOrder.Value);
            _button.SetVisible(false);
        }
        catch (Exception e) { ModPlugin.L.LogWarning("[ui] could not add the top bar button: " + e.Message); }
    }

    public static void ToggleEditor()
    {
        try
        {
            if (EditorOpen) { _editor.Close(); _editor = null; return; }
            _editor = new EditorWindow();
            _editor.Open();
        }
        catch (Exception e)
        {
            _editor = null;
            ModPlugin.L.LogError("[ui] editor window: " + e);
        }
    }

    public static bool PlayersOpen => _players != null && _players.IsOpen;

    public static void TogglePlayers()
    {
        try
        {
            if (PlayersOpen) { _players.Close(); _players = null; return; }
            _players = new PlayersWindow();
            _players.Open();
        }
        catch (Exception e)
        {
            _players = null;
            ModPlugin.L.LogError("[ui] players window: " + e);
        }
    }

    /// <summary>Opens the editor (if needed) and loads tracked player <paramref name="index"/>'s island into it.</summary>
    public static void EditPlayer(int index)
    {
        if (!EditorOpen) ToggleEditor();
        if (!EditorOpen) return;
        State.SelectedPlayer = index;
        string name = IslandOps.PlayerName(index);
        State.SelectedName = name;
        _editor.LoadFrom(IslandOps.PlayerGrid(index), name);
    }

    public static void ToggleHelp()
    {
        try
        {
            if (HelpOpen) { _help.Close(); _help = null; return; }
            _help = new HelpWindow();
            _help.Open();
        }
        catch (Exception e)
        {
            _help = null;
            ModPlugin.L.LogError("[ui] help window: " + e);
        }
    }

    // ---- single-key shortcuts that work like the game's own keys (no Right Ctrl needed) --------------------------------

    private static readonly Dictionary<int, bool> KeyPrev = new();
    private static bool _keysBroken;

    /// <summary>One press (edge) of a key while the game window has focus.</summary>
    private static bool Edge(int vk)
    {
        bool down = vk != 0 && Hotkeys.KeyIsDown(vk);
        KeyPrev.TryGetValue(vk, out bool prev);
        KeyPrev[vk] = down;
        return down && !prev;
    }

    private static void PollKeys()
    {
        if (_keysBroken || !ModPlugin.EditorShortcuts.Value) return;
        try
        {
            bool open = ModPlugin.EditorOpenKey.Value != HotkeyKey.None && Edge((int)ModPlugin.EditorOpenKey.Value);
            bool esc = Edge(0x1B), r = Edge(0x52), t = Edge(0x54), z = Edge(0x5A), y = Edge(0x59);
            bool ctrl = Hotkeys.KeyIsDown(0x11);
            if (open) ToggleEditor();
            if (HelpOpen) { if (esc) ToggleHelp(); return; }
            if (!EditorOpen) return;
            if (esc) ToggleEditor();
            else if (r) _editor.KeyRotate();
            else if (t) _editor.KeyMirror();
            else if (ctrl && z) _editor.KeyUndo();
            else if (ctrl && y) _editor.KeyRedo();
        }
        catch (DllNotFoundException) { _keysBroken = true; }        // not Windows (tests)
        catch (EntryPointNotFoundException) { _keysBroken = true; }
        catch (Exception e) { ModPlugin.L.LogError("[ui] keys: " + e); }
    }

    /// <summary>Called every frame (plugin Update): keys, the top-bar button and the editor's own refresh.</summary>
    public static void Tick()
    {
        PollKeys();
        try
        {
            bool want = ModPlugin.ShowLauncher.Value && (State.PlayerSummaries.Count > 0 || ModPlugin.LauncherAlways.Value || EditorOpen);
            _button?.SetVisible(want);
            if (_editor != null && !_editor.IsOpen) _editor = null;     // closed with its X button
            if (_help != null && !_help.IsOpen) _help = null;
            if (_players != null && !_players.IsOpen) _players = null;
            _editor?.Tick();
            _players?.Tick();
        }
        catch (Exception e) { ModPlugin.L.LogError("[ui] tick: " + e); }
    }
}
