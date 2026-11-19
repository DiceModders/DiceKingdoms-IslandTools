using BepInEx.Configuration;
using DiceModders.UiKit;
using DiceModders.UiKit.Native;

namespace DiceModders.IslandTools;

/// <summary>The "Island Tools" block in Settings > Game: the windows, and the options. Rows follow the config entries when they change elsewhere.</summary>
internal static class GameSettings
{
    internal static void Install(UiScope ui)
    {
        Tx.Block(ui, () => new SettingsBlockSpec
        {
            Id = Plugin.Guid + ".settings",
            Page = SettingsPage.Game,
            Title = Tx.T("settings.title"),
            Order = 20,
            Build = block =>
            {
                block.AddButtons(Tx.T("settings.windows"), (Tx.T("settings.editor"), EditorWindow.Toggle), (Tx.T("settings.players"), PlayersWindow.Toggle), (Tx.T("settings.tutorial"), HelpWindow.Toggle));
                Toggle(block, Tx.T("settings.button"), Options.ShowButton);
                Toggle(block, Tx.T("settings.buttonAlways"), Options.ButtonAlways);
                Toggle(block, Tx.T("settings.bypass"), Options.BypassPlacement);
                block.AddInfo(Tx.T("settings.bypassInfo"));
            },
        });
    }

    private static void Toggle(SettingsBlockHandle block, string label, ConfigEntry<bool> entry)
    {
        var row = block.AddToggle(label, entry.Value, v => entry.Value = v);
        if (row != null) entry.SettingChanged += (_, _) => row.SetValue(entry.Value);
    }
}
