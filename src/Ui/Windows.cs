using DiceModders.UiKit;
using DiceModders.UiKit.Native;

namespace DiceModders.IslandTools;

using Core = DiceModders.ModCore.ModCore;

/// <summary>
/// The mod's top bar button and its three windows (editor, players, tutorial). The button is shown during a match (option: always) and
/// follows the match events and the options; nothing is checked every frame.
/// </summary>
internal static class Windows
{
    internal static UiScope Ui { get; private set; }
    private static TopBarButtonHandle _button;

    internal static void Install(UiScope ui)
    {
        Ui = ui;
        ui?.WhenReady(s => { _button = s.Native.TopBar.AddIconButton("hammer", Tx.T("editor.title"), EditorWindow.Toggle, -100); Refresh(); });
        DiceModders.ModCore.ModCore.Texts.LanguageChanged += () => _button?.SetTooltip(Tx.T("editor.title"));   // the tooltip in the new language
        Core.Match.Started += Refresh;
        Core.Match.Ended += Refresh;
        Options.ShowButton.SettingChanged += (_, _) => Refresh();
        Options.ButtonAlways.SettingChanged += (_, _) => Refresh();
    }

    /// <summary>Shows or hides the top bar button; with the mod switched off the windows are closed too.</summary>
    internal static void Refresh()
    {
        if (!Plugin.On) { EditorWindow.Close(); PlayersWindow.Close(); HelpWindow.Close(); }
        _button?.SetVisible(Plugin.On && Options.ShowButton.Value && (Core.Match.Running || Options.ButtonAlways.Value || EditorWindow.IsOpen));
    }

    /// <summary>Opens the editor (if needed) with this player's island.</summary>
    internal static void EditPlayer(DiceModders.ModCore.MatchPlayer player)
    {
        Islands.Select(player);
        EditorWindow.Open();
        EditorWindow.LoadFrom(player);
    }
}
