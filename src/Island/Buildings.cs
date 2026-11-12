using System.Text;
using UnityEngine;

namespace DiceModders.IslandTools;

using GameBuildingTypes = DiceKingdoms.Game.BuildingTypes;

/// <summary>
/// The building table of the editor (id -> name, tier, HP, footprint), read from the game's <c>BuildingTypes</c> asset the first time it is
/// needed after the game has loaded it. Until then the built-in table of <see cref="BuildingTable"/> is used.
/// </summary>
internal static class Buildings
{
    private static BuildingTable _live;

    internal static BuildingTable Table => _live ??= ReadLive() ?? BuildingTable.FromDumpOrDefault(null);

    /// <summary>The game's table in the text format <see cref="BuildingTable.Parse"/> reads, or null while the asset is not loaded.</summary>
    private static BuildingTable ReadLive()
    {
        var all = Resources.FindObjectsOfTypeAll<GameBuildingTypes>();
        var types = all.Length > 0 ? all[0].Types : null;
        if (types == null || types.Length == 0) return null;
        var text = new StringBuilder();
        for (int i = 0; i < types.Length; i++)
        {
            var t = types[i];
            if (t == null) continue;
            text.Append(i).Append(": ").Append(t.DisplayName).Append(" | tier=").Append(t.tier).Append(" hp=").Append(t.health).Append(" | [");
            var cells = t.localPositions;
            for (int j = 0; cells != null && j < cells.Length; j++)
                text.Append(j > 0 ? ",[" : "[").Append(cells[j].x).Append(',').Append(cells[j].y).Append(']');
            text.Append("]\n");
        }
        var table = BuildingTable.Parse(text.ToString());
        if (table.ById.Count == 0) return null;
        Plugin.L.LogInfo($"[buildings] {table.ById.Count} building types read from the game.");
        return table;
    }
}
