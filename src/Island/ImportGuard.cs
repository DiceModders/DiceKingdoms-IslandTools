using System;
using DiceModders.ModCore.Tools;

namespace DiceModders.IslandTools;

using GameBuilding = DiceKingdoms.Game.Building;
using GameGrid = DiceKingdoms.Game.IslandGrid;

/// <summary>
/// The game patch of the mod, active only while this mod applies an island: every refused building placement is logged in readable form
/// and (option) allowed.
/// </summary>
internal static class ImportGuard
{
    private static bool _active;
    private static int _checks, _refused, _allowed;
    private static long _started;
    // CanPlaceBuilding is one of the game's busiest methods (the AI and the start placement call it thousands of times), so the patch is only in
    // the game while an island is being applied
    private static PatchSwitch _patch;

    internal static void Install(Patcher patcher)
    {
        _patch = patcher.Switch(typeof(GameGrid), nameof(GameGrid.CanPlaceBuilding), typeof(ImportGuard), postfix: nameof(CanPlacePostfix));
    }

    internal static void Begin()
    {
        _checks = _refused = _allowed = 0;
        _started = Environment.TickCount64;
        _active = true;
        if (_patch != null) _patch.On = true;
    }

    internal static void End()
    {
        _active = false;
        if (_patch != null) _patch.On = false;
        Plugin.L.LogInfo($"[import] {_checks} placement check(s), {_refused} refused, {_allowed} allowed anyway; " +
                         $"{Environment.TickCount64 - _started} ms");
    }

    internal static string Note() => _allowed > 0 ? " (" + Tx.T("apply.allowed", _allowed) + ")" : "";

    internal static void CanPlacePostfix(GameBuilding __0, ref bool __result)
    {
        if (!_active) return;
        _checks++;
        if (__result || __0 == null) return;
        _refused++;
        // Building: the type at +0x10, then x, y and rotation as ints
        IntPtr b = __0.Pointer;
        string line = $"[import] placement refused: {__0.type?.DisplayName ?? "?"} at x={Mem.ReadInt(b, 0x18)} y={Mem.ReadInt(b, 0x1C)} rot={Mem.ReadInt(b, 0x20)}";
        if (Options.BypassPlacement.Value) { __result = true; _allowed++; line += " -> allowed"; }
        Plugin.L.LogWarning(line);
    }
}
