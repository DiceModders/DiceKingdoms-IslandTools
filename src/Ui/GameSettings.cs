using BepInEx.Configuration;
using DiceModders.UiKit;
using DiceModders.UiKit.Native;

namespace DiceModders.IslandTools;

/// <summary>The "Island Tools" block in Settings > Game: the windows, and the options. Rows follow the config entries when they change elsewhere.</summary>
internal static class GameSettings
{
    internal static void Install(UiScope ui)
    {
        ui?.Native.Settings.AddBlock(new SettingsBlockSpec
        {
            Id = Plugin.Guid + ".settings",
            Page = SettingsPage.Game,
            Title = "Island Tools",
            Order = 20,
            Build = block =>
            {
                block.AddButtons("Windows", ("Editor", EditorWindow.Toggle), ("Players", PlayersWindow.Toggle), ("Tutorial", HelpWindow.Toggle));
                Toggle(block, "Top bar button", Options.ShowButton);
                Toggle(block, "Top bar button outside matches", Options.ButtonAlways);
                Toggle(block, "Allow every placement while applying", Options.BypassPlacement);
                block.AddInfo("Without it the game stops applying an island at the first building it thinks does not fit.");
            },
        });
    }

    private static void Toggle(SettingsBlockHandle block, string label, ConfigEntry<bool> entry)
    {
        var row = block.AddToggle(label, entry.Value, v => entry.Value = v);
        if (row != null) entry.SettingChanged += (_, _) => row.SetValue(entry.Value);
    }
}
