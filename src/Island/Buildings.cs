using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DiceModders.IslandTools;

using GameBuildingTypes = DiceKingdoms.Game.BuildingTypes;
using GameBuildingTypeName = DiceKingdoms.Game.BuildingTypeName;

/// <summary>
/// The building table of the editor (id -> name, tier, HP, footprint), read from the game's <c>BuildingTypes</c> asset the first time it is
/// needed after the game has loaded it. Until then the built-in table of <see cref="BuildingTable"/> is used.
/// </summary>
internal static class Buildings
{
    private static BuildingTable _live;
    private static int _liveCount = -1;   // length of the game's list when it was read (-1: the built-in table is in use)

    internal static BuildingTable Table => _live ??= ReadLive() ?? BuildingTable.FromDumpOrDefault(null);

    /// <summary>
    /// Reads the game's list again when it is longer or shorter than when it was read: a mod adds its buildings to it once the game data is loaded
    /// (the Casino), which can be after the first read. Called when the editor opens and before an island code is applied, not per frame.
    /// </summary>
    internal static void Refresh()
    {
        try
        {
            int n = GameTypes()?.Length ?? 0;
            if (n > 0 && n != _liveCount && ReadLive() is BuildingTable table) _live = table;
        }
        catch (Exception e) { Plugin.L.LogWarning("[buildings] re-reading the game's buildings: " + e.Message); }
    }

    private static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<DiceKingdoms.Game.BuildingType> GameTypes()
    {
        var all = Resources.FindObjectsOfTypeAll<GameBuildingTypes>();
        return all.Length > 0 ? all[0].Types : null;
    }

    /// <summary>The game's table in the text format <see cref="BuildingTable.Parse"/> reads, or null while the asset is not loaded.</summary>
    private static BuildingTable ReadLive()
    {
        var types = GameTypes();
        if (types == null || types.Length == 0) return null;
        var text = new StringBuilder();
        var fromMods = new List<int>();
        for (int i = 0; i < types.Length; i++)
        {
            var t = types[i];
            if (t == null) continue;
            if (!Enum.IsDefined(typeof(GameBuildingTypeName), t.name)) fromMods.Add(i);   // a mod's building (the game's list ends with its own)
            text.Append(i).Append(": ").Append(t.DisplayName).Append(" | tier=").Append(t.tier).Append(" hp=").Append(t.health).Append(" | [");
            var cells = t.localPositions;
            for (int j = 0; cells != null && j < cells.Length; j++)
                text.Append(j > 0 ? ",[" : "[").Append(cells[j].x).Append(',').Append(cells[j].y).Append(']');
            text.Append("]\n");
        }
        var table = BuildingTable.Parse(text.ToString());
        if (table.ById.Count == 0) return null;
        foreach (int id in fromMods) if (table.Get(id) is BuildingInfo info) info.FromMod = true;
        _liveCount = types.Length;
        Plugin.L.LogInfo($"[buildings] {table.ById.Count} building types read from the game" + (fromMods.Count > 0 ? $" ({fromMods.Count} from mods)." : "."));
        return table;
    }
}
