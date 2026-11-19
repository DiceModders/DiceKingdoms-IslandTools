using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using DiceModders.ModCore.Tools;

namespace DiceModders.IslandTools;

using Core = DiceModders.ModCore.ModCore;   // inside DiceModders.* the bare name would be the namespace
using Ui = DiceModders.UiKit.UiKit;

/// <summary>
/// DiceKingdoms Island Tools: an in-game island editor, a players window (every island of the match at a glance, bulk changes)
/// and island codes through the clipboard.
/// Everything reacts to button clicks, pointer events the editing keys and the match events; nothing runs every frame.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
[BepInDependency("DiceModders.ModCore")]
[BepInDependency("DiceModders.UiKit")]
[BepInProcess("Dice-Kingdoms.exe")]
public sealed class Plugin : BasePlugin
{
    public const string Guid = "org.dicemodders.dicekingdoms.islandtools";
    public const string Name = "DiceKingdoms Island Tools";
    public const string Version = "3.0.0";

    internal static ManualLogSource L;
    /// <summary>The mod's own switch ([General] Enabled); while off the game behaves as without the mod.</summary>
    internal static bool On;

    public override void Load()
    {
        Tx.Load();                                   // texts.json in the game's language (ModCore)
        L = Log;
        Options.Bind(Config);

        var patcher = new Patcher(Guid, Log);
        ImportGuard.Install(patcher);
        patcher.Summary();
        Islands.Install();

        var ui = Ui.CreateScope(Guid);
        Windows.Install(ui);
        EditorWindow.Install();
        PlayersWindow.Install();
        GameSettings.Install(ui);
        EditorKeys.Install(Config);

        Core.Mods.Switch(Config, description: "Turns the island editor and the players window on or off.")
            .WhenOn(() => { On = true; Windows.Refresh(); }, () => { On = false; Windows.Refresh(); });
    }
}
