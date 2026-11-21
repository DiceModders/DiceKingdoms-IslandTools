using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DiceModders.IslandTools;

// Pure C#: building id -> name, tier, HP and footprint, parsed from text
// ("id: Name | tier=T hp=H | [[dx,dy],...]"). A built-in copy of the current game build is used until the game has loaded its own (see Buildings).

internal sealed class BuildingInfo
{
    public int Id;
    public string RawName;
    /// <summary>The game's own name in the current language (building.&lt;TYPE&gt;.name), else the listed English name.</summary>
    public string Name => DiceModders.IslandTools.GameNames.Building(RawName);
    public int Tier, Hp;
    public int[][] Shape;      // list of {dx, dy}
    /// <summary>Added to the game's list by a mod (not one of the game's own BuildingTypeName values): a game without that mod cannot read it.</summary>
    public bool FromMod;
}

internal sealed class BuildingTable
{
    public readonly SortedDictionary<int, BuildingInfo> ById = new();

    private static readonly Regex Full = new(@"^\s*(\d+)\s*:\s*(.+?)\s*\|\s*tier\s*=\s*(-?\d+)\s+hp\s*=\s*(-?\d+)\s*\|\s*(\[.*\])\s*$");
    private static readonly Regex Plain = new(@"^\s*(\d+)\s*:\s*(.+?)\s*(\[\s*\[.*\])\s*$");
    private static readonly Regex Pair = new(@"\[\s*(-?\d+)\s*,\s*(-?\d+)\s*\]");

    public BuildingInfo Get(int id) => ById.TryGetValue(id, out var b) ? b : null;
    public string NameOf(int id) => Get(id)?.Name ?? ("type " + id);

    public static BuildingTable Parse(string text)
    {
        var t = new BuildingTable();
        if (string.IsNullOrEmpty(text)) return t;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            int tier = 0, hp = 0;
            Match m = Full.Match(line);
            string shapeText, name;
            int id;
            if (m.Success)
            {
                id = int.Parse(m.Groups[1].Value); name = m.Groups[2].Value;
                tier = int.Parse(m.Groups[3].Value); hp = int.Parse(m.Groups[4].Value);
                shapeText = m.Groups[5].Value;
            }
            else
            {
                m = Plain.Match(line);
                if (!m.Success) continue;
                id = int.Parse(m.Groups[1].Value); name = m.Groups[2].Value.TrimEnd(' ', '|');
                shapeText = m.Groups[3].Value;
            }
            var cells = new List<int[]>();
            foreach (Match p in Pair.Matches(shapeText))
                cells.Add(new[] { int.Parse(p.Groups[1].Value), int.Parse(p.Groups[2].Value) });
            if (cells.Count == 0) continue;
            t.ById[id] = new BuildingInfo { Id = id, RawName = name, Tier = tier, Hp = hp, Shape = cells.ToArray() };
        }
        return t;
    }

    /// <summary>The live dump if it parses, else the built-in table.</summary>
    public static BuildingTable FromDumpOrDefault(string dumpText)
    {
        var live = Parse(dumpText);
        return live.ById.Count > 0 ? live : Parse(DefaultDump);
    }

    // Dice Kingdoms v1.0.4 (24 building types), taken from the game's own F11 dump.
    public const string DefaultDump = @"0: Archery Range | tier=2 hp=60 | [[0,0],[-1,0],[0,-1],[-1,-1],[-1,-2]]
1: Barracks | tier=1 hp=60 | [[-1,1],[0,1],[0,0],[0,-1],[1,0],[1,-1]]
2: Castle | tier=1 hp=200 | [[-1,-1],[-1,0],[-1,1],[-1,2],[0,-1],[0,0],[0,1],[0,2],[1,-1],[1,0],[1,1],[1,2],[2,-1],[2,0],[2,1],[2,2]]
3: Cathedral | tier=2 hp=40 | [[1,0],[0,0],[-1,0],[-2,0],[0,1],[0,-1]]
4: Church | tier=1 hp=15 | [[0,0],[-1,0],[-2,0]]
5: Cottage | tier=1 hp=10 | [[0,0],[0,1]]
6: Dock | tier=1 hp=50 | [[0,0],[1,0],[2,0],[2,1],[3,0],[3,1],[4,-1],[4,0],[4,1],[5,-1],[5,0],[5,1]]
7: Farm | tier=1 hp=15 | [[0,1],[0,0],[0,-1],[0,-2],[1,1],[1,0],[1,-1],[1,-2]]
8: Guard Tower | tier=2 hp=40 | [[0,0],[-1,0],[0,-1],[-1,-1]]
9: Guard Tower Up | tier=2 hp=40 | [[0,0],[-1,0],[0,-1],[-1,-1]]
10: Hospital | tier=2 hp=40 | [[0,0],[-1,0],[1,0],[0,-1],[0,1]]
11: House | tier=1 hp=15 | [[0,0],[-1,0],[0,-1]]
12: Lumber Camp | tier=3 hp=20 | [[0,0],[-1,0],[-1,1],[-2,1]]
13: Market | tier=3 hp=10 | [[0,0],[1,0],[1,1],[0,1]]
14: Sorcerer Tower | tier=2 hp=20 | [[0,0],[1,0],[0,1]]
15: Stone Quarry | tier=3 hp=20 | [[0,0],[0,1],[1,0]]
16: Storage | tier=2 hp=20 | [[0,0],[1,0],[0,-1],[1,-1],[0,1],[1,1]]
17: University | tier=1 hp=50 | [[0,1],[0,0],[0,-1],[1,1],[1,-1],[2,1],[2,-1]]
18: Wall | tier=1 hp=50 | [[0,0]]
19: Windmill | tier=2 hp=20 | [[0,0],[-1,0],[-1,1],[0,1]]
20: Wonder | tier=3 hp=150 | [[3,-1],[3,0],[3,1],[2,-1],[2,0],[2,1],[1,-1],[1,0],[1,1],[0,-1],[0,0],[0,1],[-1,-1],[-1,0],[-1,1]]
21: Fishing Ship | tier=2 hp=30 | [[0,0],[0,1]]
22: Lighthouse | tier=3 hp=50 | [[0,0],[-1,0],[0,-1],[-1,-1]]
23: Land Camp | tier=3 hp=20 | [[0,0],[1,1],[0,1],[-1,1]]";
}
