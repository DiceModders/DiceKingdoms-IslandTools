using System;
using System.Collections.Generic;
using System.Linq;
using DiceModders.ModCore;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace DiceModders.IslandTools;

using Core = DiceModders.ModCore.ModCore;
using GameGrid = DiceKingdoms.Game.IslandGrid;

/// <summary>
/// The players' islands in the running match: reading one as an island code (<c>IslandGrid.ExportIsland</c>) and replacing one with an island
/// code (<c>IslandGrid.ImportIsland</c>, guarded by <see cref="ImportGuard"/>). Also the "selected player" the editor's second row works on.
/// </summary>
internal static class Islands
{
    /// <summary>Raised after an island was replaced by this mod (the players window redraws its pictures).</summary>
    internal static event Action Changed;
    internal static event Action SelectionChanged;

    private static IntPtr _selected;   // the selected player's Player object; it lives as long as the match

    internal static IReadOnlyList<MatchPlayer> Players => Core.Players.All;
    internal static MatchPlayer Local => Core.Players.Local;

    internal static void Install()
    {
        // Ended is not raised when the match was left before it started (islands still spawning): Reset covers that
        Core.Match.Ended += ClearSelection;
        Core.Match.Reset += ClearSelection;
    }

    private static void ClearSelection()
    {
        if (_selected == IntPtr.Zero) return;
        _selected = IntPtr.Zero;
        SelectionChanged?.Invoke();
    }

    internal static bool IsLocal(MatchPlayer p) => p != null && p.IsLocal;
    internal static bool Same(MatchPlayer a, MatchPlayer b) => a != null && b != null && a.Game.Pointer == b.Game.Pointer;

    internal static MatchPlayer Selected => Players.FirstOrDefault(p => p.Game.Pointer == _selected);

    internal static void Select(MatchPlayer p) { _selected = p?.Game.Pointer ?? IntPtr.Zero; SelectionChanged?.Invoke(); }

    /// <summary>Selects the next player of the match (after the selected one).</summary>
    internal static void SelectNext()
    {
        var all = Players;
        if (all.Count == 0) return;
        int at = -1;
        for (int i = 0; i < all.Count; i++) if (all[i].Game.Pointer == _selected) at = i;
        Select(all[(at + 1) % all.Count]);
    }

    internal static GameGrid GridOf(MatchPlayer p)
    {
        try { return p?.Island?.grid; }
        catch (Exception) { return null; }
    }

    /// <summary>Tiles per side of the player's island grid (49 in a normal match, bigger with the lobby rule), or 0.</summary>
    internal static int SideOf(MatchPlayer p)
    {
        var grid = GridOf(p);
        try { return grid == null ? 0 : grid.size.x; }
        catch (Exception) { return 0; }
    }

    /// <summary>The player's island as island-code bytes (raw DEFLATE of the island stream), or null.</summary>
    internal static byte[] Export(MatchPlayer p)
    {
        var grid = GridOf(p);
        if (grid == null) return null;
        try { return grid.ExportIsland()?.ToArray(); }
        catch (Exception e) { Plugin.L.LogError("[island] export: " + e.Message); return null; }
    }

    /// <summary>Replaces the player's island with island-code bytes. The message is the result for the window's status line.</summary>
    internal static bool Import(MatchPlayer p, byte[] code, out string message)
    {
        var grid = GridOf(p);
        if (grid == null) { message = Tx.T("apply.noIsland"); return false; }
        string fitted = Fit(ref code, SideOf(p));
        var backup = Export(p);   // the game changes the island while it reads the code: put this back if it stops halfway
        ImportGuard.Begin();
        try
        {
            RemoveHills(grid);
            grid.ImportIsland(new Il2CppStructArray<byte>(code));
            message = Tx.T("apply.done", p.Name) + fitted + ImportGuard.Note() + ".";
            return true;
        }
        catch (Exception e)
        {
            string first = (e.Message ?? "").Split('\n')[0].Trim();
            Plugin.L.LogError("[island] import: " + e.GetType().Name + ": " + first);
            bool restored = Restore(grid, backup);
            string after = " " + (restored ? Tx.T("apply.restored") : Tx.T("apply.half"));
            message = (e.Message ?? "").Contains("CanPlaceBuilding")
                ? Tx.T("apply.refused") + after +
                  " " + Tx.T("apply.fixHint")
                : Tx.T("apply.rejected", first) + after;
            return false;
        }
        finally
        {
            ImportGuard.End();
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// <c>IslandGrid.RemoveInnerCliff</c> turns every tile under the removed hill into grass. The game removes the old hills while it applies the
    /// code, after the new ground, so a hill that stood where the new island has water left a grass islet there. Removing them first lets the new
    /// ground overwrite that grass.
    /// </summary>
    private static void RemoveHills(GameGrid grid)
    {
        int count;
        try { count = grid.innerCliffs?.Count ?? 0; }
        catch (Exception e) { Plugin.L.LogWarning("[island] reading the old hills: " + e.Message); return; }
        for (int i = count - 1; i >= 0; i--)   // from the end: the game removes with swap-back
        {
            try { grid.RemoveInnerCliff(i); }
            catch (Exception e) { Plugin.L.LogWarning("[island] removing old hill " + i + ": " + e.Message); }   // one bad piece must not keep the rest
        }
    }

    /// <summary>After a failed import: the island as it was before (its own export). False when there is no backup or that fails too.</summary>
    private static bool Restore(GameGrid grid, byte[] backup)
    {
        if (backup == null || backup.Length == 0) return false;
        try
        {
            RemoveHills(grid);
            grid.ImportIsland(new Il2CppStructArray<byte>(backup));
            Plugin.L.LogInfo("[island] failed import: the island was put back from its backup");
            return true;
        }
        catch (Exception e) { Plugin.L.LogError("[island] putting the island back failed: " + e.Message); return false; }
    }

    /// <summary>
    /// The game reads an island code into the grid it already has and fails on any other size (IndexOutOfRange). An island of another size is
    /// put in the middle of the player's grid first. Returns the note for the status line ("" when nothing changed).
    /// </summary>
    private static string Fit(ref byte[] code, int side)
    {
        if (side <= 0) return "";
        IslandData data;
        try { data = IslandCodec.FromCodeBytes(code); }
        catch (Exception) { return ""; }                    // not readable here: the game gives its own answer
        if (!data.SideIsExact) return "";

        // a hill piece the game has no model for stops ImportIsland halfway (KeyNotFound in DefaultPrefabs.InnerCliff): leave such pieces out
        int unknown = data.Cliffs.RemoveAll(c => !KnownHill(c));
        if (unknown > 0) Plugin.L.LogWarning($"[island] {unknown} hill piece(s) of a kind the game does not have were left out");
        string note = unknown > 0 ? Tx.T("fit.unknownHills", unknown) : "";
        if (data.Side == side)
        {
            if (unknown > 0) code = IslandCodec.ToCodeBytes(data);
            return note;
        }
        code = IslandCodec.ToCodeBytes(data.Fitted(side, out int dropped));
        return note + Tx.T("fit.placed", data.Side, side) +
               (dropped > 0 ? Tx.T("fit.dropped", dropped) : "") + ")";
    }

    /// <summary>The hill pieces the game has models for (InnerCliff.Key: kind, width, height).</summary>
    private static bool KnownHill(IslandCliff c)
    {
        foreach (var s in EditorModel.HillShapes) if (s.Kind == c.Kind && s.W == c.W && s.H == c.H) return true;
        return false;
    }
}
