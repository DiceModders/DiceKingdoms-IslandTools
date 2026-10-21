using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using DiceModders.Shared;
using UnityEngine;

namespace DiceModders.IslandTools;

/// <summary>
/// Island-focused tools: the in-game island editor, save/load an island through the real Windows clipboard, pick another
/// player's island as a target (host experiment), read building footprints for the editor, and set the island's land radius.
/// The editor's interface is built with the UiKit library (a separate plugin that has to be installed next to this one).
/// </summary>
[BepInPlugin(Guid, Name, Version)]
[BepInDependency("DiceModders.UiKit")]
[BepInProcess("Dice-Kingdoms.exe")]
public sealed class ModPlugin : BasePlugin
{
    public const string Guid = "org.dicemodders.dicekingdoms.islandtools";
    public const string Name = "DiceKingdoms Island Tools";
    public const string Version = "2.1.0";

    internal static ManualLogSource L;
    internal static Hotkeys Keys;

    internal static ConfigEntry<int> IslandRadius;
    internal static ConfigEntry<bool> AutoDumpBuildings;
    internal static ConfigEntry<string> LocalPlayerName;
    internal static ConfigEntry<bool> BypassPlacementWhileImporting;

    internal static ConfigEntry<bool> ShowLauncher, LauncherAlways, EditorShortcuts;
    internal static ConfigEntry<int> LauncherOrder, WindowWidth, WindowHeight;
    internal static ConfigEntry<HotkeyKey> EditorOpenKey;

    public override void Load()
    {
        L = Log;

        IslandRadius = Config.Bind("Island", "HardLimitRadius", 24,
            new ConfigDescription(
                "Land radius (HardLimit.radius) applied when an island is generated. Vanilla is 22; 0 = leave the game's value alone. " +
                "EVERY player in the lobby must use the same value, and values above ~24-26 approach the edge of the " +
                "allocated tile arrays (danger zone) - raise it in small steps.",
                new AcceptableValueRange<int>(0, 40)));
        LocalPlayerName = Config.Bind("Island", "LocalPlayerName", "",
            "Your player name as shown in the match (e.g. your Steam name). Leave empty to detect it automatically; " +
            "set it if 'Save/Load my island' reports that it cannot tell which player is you.");
        BypassPlacementWhileImporting = Config.Bind("Island", "BypassPlacementWhileImporting", true,
            "While an island code is being imported (and only then), treat every building placement as valid. " +
            "Without this the game asserts on buildings it thinks do not fit the CURRENT terrain (typically docks, or an " +
            "editor-made castle) and aborts the import halfway.");
        AutoDumpBuildings = Config.Bind("Buildings", "AutoDump", true,
            "Read the building types (names, sizes, HP) from the game automatically the first time they are seen in a match, for the island editor.");

        ShowLauncher = Config.Bind("Editor", "ShowLauncher", true,
            "Show the Island Editor (hammer) button in the game's top-right button bar. It only appears during a match unless LauncherAlways is on.");
        LauncherAlways = Config.Bind("Editor", "LauncherAlways", false, "Also show the hammer button in the main menu / lobby.");
        LauncherOrder = Config.Bind("Editor", "LauncherOrder", -100,
            new ConfigDescription("Position of the hammer button among the mod buttons of the top bar (lower = further left).", new AcceptableValueRange<int>(-1000, 1000)));
        WindowWidth = Config.Bind("Editor", "WindowWidth", 1100,
            new ConfigDescription("Width of the editor window in UI units (the - / + buttons in its title bar change it; Full screen fills the screen).", new AcceptableValueRange<int>(640, 2400)));
        WindowHeight = Config.Bind("Editor", "WindowHeight", 880,
            new ConfigDescription("Height of the editor window in UI units.", new AcceptableValueRange<int>(480, 1400)));
        EditorShortcuts = Config.Bind("Editor", "KeyboardShortcuts", true,
            "While the editor is open: Esc closes it (tutorial first), R rotates / T mirrors the building brush (same keys as the game's build mode), Ctrl+Z / Ctrl+Y undo / redo.");
        EditorOpenKey = Config.Bind("Editor", "OpenKey", HotkeyKey.F9,
            "A single key (no Right Ctrl needed) that opens / closes the editor. None = off. F1, F2, F3 and F10 are used by the game.");

        Keys = new Hotkeys(Config, L, Name);
        Keys.Bind(Config, "OpenEditor", HotkeyKey.E, "Open / close the in-game island editor", EditorUi.ToggleEditor, "Island editor");
        Keys.Bind(Config, "OpenPlayers", HotkeyKey.None, "Open / close the players dashboard (select several players, live island previews, bulk actions)", EditorUi.TogglePlayers, "Players dashboard");
        Keys.Bind(Config, "EditorHelp", HotkeyKey.None, "Open / close the island editor tutorial", EditorUi.ToggleHelp, "Island editor tutorial");
        Keys.Bind(Config, "SaveIsland", HotkeyKey.K, "Save YOUR island to the clipboard (experimental)", IslandOps.SaveOwn, "Save my island to clipboard");
        Keys.Bind(Config, "LoadIsland", HotkeyKey.L, "Load an island from the clipboard onto your island (experimental)", IslandOps.LoadOwn, "Load island from clipboard");
        Keys.Bind(Config, "CyclePlayer", HotkeyKey.None, "Cycle which tracked player is selected", IslandOps.CyclePlayer, "Select next player");
        Keys.Bind(Config, "SaveSelectedPlayer", HotkeyKey.None, "Copy the SELECTED player's island code to the clipboard",
                  IslandOps.SaveSelected, "Copy selected player's island code");
        Keys.Bind(Config, "LoadOnSelectedPlayer", HotkeyKey.None, "Load the clipboard onto the SELECTED player's island (host only, RISKY / untested)", IslandOps.LoadOnSelected, "Load clipboard onto selected player (RISKY)");

        var patcher = new Patcher(Guid, L);
        patcher.Patch("IslandGrid", "Awake", typeof(Patches), prefix: nameof(Patches.IslandGridAwakePrefix));
        patcher.Patch("ChatInterface", "Awake", typeof(Patches), prefix: nameof(Patches.NewMatchPrefix));
        patcher.Patch("PlayerSummary", "Update", typeof(Patches), postfix: nameof(Patches.SummaryPostfix));
        patcher.Patch("PlayerSummary", "OnSyncPhase", typeof(Patches), postfix: nameof(Patches.SummaryPostfix));
        patcher.Patch("BuildData", "Unlocked", typeof(Patches), prefix: nameof(Patches.UnlockedPrefix));
        patcher.Patch("RulesBuilding", "OnEnable", typeof(Patches), postfix: nameof(Patches.RulesBuildingPostfix));
        patcher.Patch("BuildingTypes", "get_Types", typeof(Patches), postfix: nameof(Patches.GetTypesPostfix));
        patcher.Patch("IslandGrid", "CanPlaceBuilding", typeof(Patches), postfix: nameof(Patches.CanPlacePostfix));
        patcher.Patch("IslandGrid", "AddBuilding", typeof(Patches), prefix: nameof(Patches.AddBuildingPrefix), postfix: nameof(Patches.AddBuildingPostfix));
        patcher.Summary();

        EditorUi.Init();                  // the hammer button in the top bar
        Keys.LogBindings();
        AddComponent<IslandToolsTicker>();
    }
}

public sealed class IslandToolsTicker : MonoBehaviour
{
    public IslandToolsTicker(IntPtr ptr) : base(ptr) { }

    private void Update()
    {
        ModPlugin.Keys?.Poll();
        BuildingDump.AutoTick();
        EditorUi.Tick();
    }
}
