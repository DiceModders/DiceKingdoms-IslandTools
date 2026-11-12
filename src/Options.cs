using BepInEx.Configuration;

namespace DiceModders.IslandTools;

/// <summary>The mod's local options, in its config file. The Settings block and in-game config editors both change these; the features listen to them.</summary>
internal static class Options
{
    internal static ConfigEntry<bool> BypassPlacement, ShowButton, ButtonAlways;
    internal static ConfigEntry<int> WindowWidth, WindowHeight;

    internal static void Bind(ConfigFile config)
    {
        BypassPlacement = config.Bind("Island", "BypassPlacementWhileImporting", true,
            "While an island is being applied (and only then), treat every building placement as valid. Without this the game refuses buildings it " +
            "thinks do not fit the CURRENT terrain (typically docks, or an editor-made castle) and stops the import halfway.");
        ShowButton = config.Bind("Editor", "ShowButton", true, "Show the Island Editor button in the game's top bar (during a match).");
        ButtonAlways = config.Bind("Editor", "ButtonAlways", false, "Also show the button in the main menu and the lobby.");
        WindowWidth = config.Bind("Editor", "WindowWidth", 1100,
            new ConfigDescription("Width of the editor window (the - / + buttons in its title bar change it).", new AcceptableValueRange<int>(720, 2400)));
        WindowHeight = config.Bind("Editor", "WindowHeight", 880,
            new ConfigDescription("Height of the editor window.", new AcceptableValueRange<int>(560, 1400)));
    }
}
