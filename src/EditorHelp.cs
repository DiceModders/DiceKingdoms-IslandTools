using System;
using DiceModders.UiKit;
using UnityEngine;

namespace DiceModders.IslandTools;

// Inside namespace DiceModders.* the bare name "UiKit" would be the NAMESPACE DiceModders.UiKit; this alias (declared inside
// our namespace, so it wins) makes it mean the static class again.
using UiKit = DiceModders.UiKit.UiKit;

/// <summary>The paged tutorial window ("Help" in the editor's title bar).</summary>
internal sealed class HelpWindow
{
    private WindowHandle _win;
    private int _page;
    private LabelHandle _pageTitle, _body;
    private ScrollHandle _scroll;

    private static readonly string[] Titles =
    {
        "1/8  What is this?",
        "2/8  Loading an island",
        "3/8  Painting land and nature",
        "4/8  Placing buildings",
        "5/8  Check before you apply",
        "6/8  Applying to the game",
        "7/8  Sharing codes, tips",
        "8/8  Troubleshooting",
    };

    private static readonly string[] Pages =
    {
        "The Island Editor lets you change an island while you play: paint land (grass, sand, water...), add or remove trees, rocks and other nature, " +
        "place or delete buildings, and then put the result into the game.\n\n" +
        "It is the in-game version of the browser Island Editor (tools/island-editor.html) and uses the same island codes.\n\n" +
        "Open it with the hammer button in the game's top-right button bar, with F9, or with Right Ctrl + E. The \"Help\" button in its title bar opens this tutorial. " +
        "Drag a window by its title bar; the X closes it and \"Full screen\" makes it fill the screen.\n\n" +
        "The hammer button only shows during a match. (Config: [Editor] LauncherAlways, ShowLauncher.)",

        "The top two rows decide where the island comes from and where it goes.\n\n" +
        "Load MY island - reads your current island from the game into the editor.\n" +
        "Paste code - loads an island code from the clipboard (from the web editor, a friend, or \"Copy code\").\n" +
        "New blank island - a square of water the same size as the loaded island (49 x 49 if nothing is loaded).\n" +
        "Next player + Load SELECTED - pick another player's island and read it. Loading never changes the game.\n\n" +
        "Nothing you do in the editor touches the game until you press an Apply button.",

        "Pick a layer: Ground (land / water / sand / pier ...), Nature (trees, rocks, scorched earth, lava, flowers, fish) or Buildings.\n\n" +
        "Tools: Point (drag to paint), Fill (flood fill), Line, Rect, Square, Circle, Oval (drag from one corner to the other) and Spray. " +
        "Shapes preview while you drag and are committed when you let go.\n\n" +
        "Brush size, left-right / top-bottom symmetry and Filled shapes (off = outline only) are on the left. " +
        "Right-click a tile to pick up its ground or nature as your brush. Undo / Redo keep 60 steps.\n\n" +
        "Coast (bottom row): Outline paints the shore with your brush, Grow adds one ring of land, Shrink removes one. " +
        "Nature brush \"Erase\" removes nature.",

        "Switch to the Buildings layer. Use < and > to choose the type (id, name, tier, HP and size are shown).\n\n" +
        "Click an empty tile to place the building there. The yellow preview shows exactly which tiles it will use; " +
        "the white dot on the map is its anchor tile. Click any tile of an existing building (or right-click it) to remove it.\n\n" +
        "Rotate turns the building in steps of 90 degrees, Mirrored flips it. HP \"type default\" uses the game's value " +
        "(Castle 200, House 15 ...); the + / - buttons set your own.\n\n" +
        "Docks, Fishing Ships and Lighthouses may stand on water; everything else needs land.",

        "Press \"Check buildings\" before you apply. It marks problems with red frames on the map and lists them under it:\n\n" +
        "- a building outside the island,\n- nature (a tree, a rock ...) under a building,\n- a land building on water,\n- two buildings overlapping.\n\n" +
        "\"Clear bldg nature\" fixes the nature problem in one click. " +
        "Keep a Castle away from the island's edge - an edge Castle is refused by the game.\n\n" +
        "The game normally refuses invalid placements; while the editor applies an island, IslandTools overrides those refusals " +
        "(config [Island] BypassPlacementWhileImporting) so one bad building does not stop everything - but it can still look wrong in play.",

        "Apply to MY island - replaces your island, right now, with what you see in the editor. Do this early in a match: " +
        "units and buildings you had are replaced by the ones in the editor.\n\n" +
        "Apply to SELECTED - the same for the island of the player chosen with \"Next player\". " +
        "EXPERIMENTAL and untested: it is only expected to reach other players when you are the host, and it can desynchronise the game. " +
        "Tell everyone before you use it.\n\n" +
        "Known open problems (they also affect the Right Ctrl + L hotkey): loading onto your own island can report placement errors " +
        "and lose buildings, and the turn timer may break afterwards. The BepInEx log shows an [import] line with the counts.",

        "Copy code puts the current island code on the clipboard. Send it to a friend, or paste it into the browser editor " +
        "(tools/island-editor.html) for more room and tools. The game's own Right Ctrl + K / L hotkeys use the same codes.\n\n" +
        "\"Game orientation (Y up)\" makes the map match what you see in the game; turn it off to see raw grid rows.\n\n" +
        "The editor is part of the game's own UI, so clicks on it should not reach the game underneath. If the game still reacts " +
        "(for example places a building), deselect the building in the game's bottom bar before you paint.\n\n" +
        "Drag the window by its title bar; \"Full screen\" gives the map more room. [Editor] WindowWidth and WindowHeight in the config set the window's size.",

        "\"Cannot find that island\" - you are not in a running match yet (the mod learns the players from the match screen). " +
        "Wait until the island is visible, then try again.\n\n" +
        "\"Could not tell which player is YOU\" (in the log) - set [Island] LocalPlayerName in BepInEx/config to your in-game name.\n\n" +
        "Buildings look wrong after Apply - run \"Check buildings\", fix the red frames and apply again. Building names / shapes are read from the game " +
        "as soon as a match shows them; a built-in copy of v1.0.4 is used until then.\n\n" +
        "Something looks broken? Send the BepInEx log lines starting with [ui], [editor] or [import] (build with scripts\\build-dev.ps1 to switch the log on).",
    };

    public bool IsOpen => _win != null && _win.IsOpen;

    public void Close()
    {
        if (_win != null) _win.Close();
        _win = null;
    }

    public void Open()
    {
        _win = UiKit.Window(w =>
        {
            w.Size(760f, 600f).Draggable().Fullscreenable().Closable().Title("Island Editor - tutorial");
            w.FullscreenButton();
            w.Position(140f, 90f);

            _pageTitle = w.Body.Label(Titles[0], 22f).SetColor(new Color(1f, 0.82f, 0.45f, 1f));
            _scroll = w.Body.ScrollView(sv => { _body = sv.Paragraph(Pages[0], 18f); });
            _scroll.WeightH(1f);
            w.Body.Horizontal(r =>
            {
                r.Button("< Previous", () => Go(-1));
                r.Button("Open the editor", () => { if (!EditorUi.EditorOpen) EditorUi.ToggleEditor(); });
                r.Button("Next >", () => Go(1));
            }).FixedH(32f);
        });
        _page = 0;
    }

    private void Go(int d)
    {
        _page = Mathf.Clamp(_page + d, 0, Pages.Length - 1);
        _pageTitle.SetText(Titles[_page]);
        _body.SetText(Pages[_page]);
        _scroll.ScrollToTop();
    }
}
