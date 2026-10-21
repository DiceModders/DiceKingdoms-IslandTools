using System;
using System.Collections.Generic;

namespace DiceModders.IslandTools;

/// <summary>Objects captured by patches. Everything cross-match is cleared on a new match.</summary>
internal static class State
{
    public const int MaxTrackedPlayers = 16;
    public const int MaxSeenTypes = 512;

    /// <summary>Last IslandGrid that ran Awake. With several players this is NOT guaranteed to be yours.</summary>
    public static object LastIslandGrid;

    /// <summary>PlayerSummary instances (one per player row in the match UI), by native pointer.</summary>
    public static readonly List<IntPtr> PlayerSummaries = new();
    public static int SelectedPlayer;
    public static string SelectedName;

    public static object BuildingTypes;                       // BuildingTypes asset instance
    public static readonly List<IntPtr> SeenBuildingTypes = new(); // BuildingType* seen via BuildData.Unlocked
    public static bool AutoDumped;

    /// <summary>The text of the latest building dump (same format as BepInEx/DiceKingdoms_buildings.txt); used by the in-game editor.</summary>
    public static string BuildingDumpText;

    /// <summary>True only while our own import call runs, so refused placements get logged.</summary>
    public static bool Importing;

    /// <summary>How many CanPlaceBuilding refusals were overridden during the current import.</summary>
    public static int BypassedChecks;

    // import diagnostics (reset at the start of every import)
    public static int ImportChecks;        // CanPlaceBuilding calls
    public static int ImportRefused;       // ... that returned false
    public static int ImportAddsStarted;   // IslandGrid.AddBuilding calls
    public static int ImportAddsDone;      // ... that finished without an exception

    /// <summary>Island codes saved by the players dashboard right before it changed an island (by player name); cleared with every new match.</summary>
    public static readonly Dictionary<string, byte[]> Backups = new();

    public static void ResetForNewMatch()
    {
        Backups.Clear();
        LastIslandGrid = null;
        PlayerSummaries.Clear();
        SelectedPlayer = 0;
        SelectedName = null;
    }
}
