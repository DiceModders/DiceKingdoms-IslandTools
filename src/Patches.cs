using System;
using DiceModders.Shared;

namespace DiceModders.IslandTools;

internal static class Patches
{
    // IslandGrid.generationParameters is an embedded struct at +0x1E8; HardLimit.radius is its
    // first field. This is plain serialized data that Awake never writes, and setting it is
    // idempotent (an absolute SET, not a multiply), so it stays correct if Awake re-runs
    // during an import. See the wiki's "Island Grid" page for why we do NOT resize arrays.
    internal static void IslandGridAwakePrefix(object __instance)
    {
        IntPtr p = Mem.PtrOf(__instance);
        int radius = ModPlugin.IslandRadius.Value;
        if (p != IntPtr.Zero && radius > 0) Mem.WriteInt(p, 0x1E8, radius);
        State.LastIslandGrid = __instance;
    }

    // ChatInterface.Awake fires once per fresh match: drop pointers into the previous match.
    internal static void NewMatchPrefix()
    {
        State.ResetForNewMatch();
        ModPlugin.L.LogInfo("[capture] new match - cleared cross-match state");
    }

    internal static void SummaryPostfix(object __instance)
    {
        IntPtr p = Mem.PtrOf(__instance);
        if (p == IntPtr.Zero || State.PlayerSummaries.Contains(p)) return;
        if (State.PlayerSummaries.Count >= State.MaxTrackedPlayers) return;
        State.PlayerSummaries.Add(p);
        ModPlugin.L.LogInfo($"[players] tracked PlayerSummary #{State.PlayerSummaries.Count - 1} = 0x{p.ToInt64():X}");
    }

    // BuildData.Unlocked(BuildingType) is called repeatedly by the bottom-bar panels, so it is a
    // reliable place to learn live BuildingType pointers (used by the dump and the import diagnostic).
    internal static void UnlockedPrefix(object[] __args)
    {
        if (__args == null || __args.Length < 1) return;
        IntPtr type = Mem.PtrOf(__args[0]);
        if (type == IntPtr.Zero || State.SeenBuildingTypes.Count >= State.MaxSeenTypes) return;
        if (!State.SeenBuildingTypes.Contains(type)) State.SeenBuildingTypes.Add(type);
    }

    // RulesBuilding holds the shared BuildingTypes asset at +0x18.
    internal static void RulesBuildingPostfix(object __instance)
    {
        IntPtr p = Mem.PtrOf(__instance);
        IntPtr bt = Mem.ReadPtr(p, 0x18);
        if (bt != IntPtr.Zero) State.BuildingTypes = Reflect.Wrap(GameTypes.Find("BuildingTypes"), bt);
    }

    internal static void GetTypesPostfix(object __instance)
    {
        if (__instance != null) State.BuildingTypes = __instance;
    }

    // While WE import an island: count every placement check, log every refused building in readable form, and (unless
    // disabled in the config) override the refusal so the import can continue.
    internal static void CanPlacePostfix(object[] __args, ref bool __result)
    {
        if (!State.Importing) return;
        State.ImportChecks++;
        if (__result || __args == null || __args.Length < 1) return;
        IntPtr building = Mem.PtrOf(__args[0]);
        if (building == IntPtr.Zero) return;
        State.ImportRefused++;

        // Layout seen in the logs: BuildingType pointer at +0x10, then x, y, rotation as ints.
        IntPtr typePtr = Mem.ReadPtr(building, 0x10);
        string type = typePtr != IntPtr.Zero && State.SeenBuildingTypes.Contains(typePtr) ? BuildingDump.NameOf(typePtr) : "?";
        var sb = new System.Text.StringBuilder(
            $"[import] placement check REFUSED: {type} at x={Mem.ReadInt(building, 0x18)} y={Mem.ReadInt(building, 0x1C)} rot={Mem.ReadInt(building, 0x20)}  (raw ints:");
        for (int i = 0; i < 12; i++) sb.Append(' ').Append(Mem.ReadInt(building, 0x10 + i * 4));
        sb.Append(')');

        if (ModPlugin.BypassPlacementWhileImporting.Value)
        {
            __result = true;                 // let the import continue; the imported tiles define the final terrain
            State.BypassedChecks++;
            sb.Append(" -> overridden");
        }
        ModPlugin.L.LogWarning(sb.ToString());
    }

    // IslandGrid.AddBuilding: started vs. completed tells us whether buildings are silently lost during an import.
    internal static void AddBuildingPrefix() { if (State.Importing) State.ImportAddsStarted++; }
    internal static void AddBuildingPostfix() { if (State.Importing) State.ImportAddsDone++; }
}
