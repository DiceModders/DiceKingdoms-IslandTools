using DiceModders.UiKit.Custom;
using UnityEngine;

namespace DiceModders.IslandTools;

/// <summary>The paged tutorial window ("Help" in the editor's title bar, or Settings > Game > Island Tools).</summary>
internal static class HelpWindow
{
    private static UiWindow _win;
    private static UiText _title, _body;
    private static UiScroll _scroll;
    private static int _page;

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
        "The Island Editor lets you change an island while you play: paint land (grass, sand, water ...), add or remove trees, rocks and other nature, " +
        "place or delete buildings, and then put the result into the game.\n\n" +
        "It uses the same island codes as the game's own island save / load and the browser island editor.\n\n" +
        "Open it with the hammer button in the game's top bar (shown during a match) or from Settings > Game > Island Tools. The \"Help\" button in its " +
        "title bar opens this tutorial. Drag a window by its title bar; the X or Escape closes it and \"Full screen\" makes it fill the screen.",

        "The top two rows decide where the island comes from and where it goes.\n\n" +
        "Load MY island - reads your current island from the game into the editor.\n" +
        "Paste code - loads an island code from the clipboard (from the browser editor, a friend, or \"Copy code\").\n" +
        "New blank island - a square of water the size of your island in this match (outside a match: the loaded island's size, or 49 x 49).\n" +
        "Applying an island of another size puts it in the middle of the player's grid.\n" +
        "Next player + Load SELECTED - pick another player's island and read it. The Players window does the same with a click on a picture.\n\n" +
        "Loading never changes the game. Nothing you do in the editor touches the game until you press an Apply button.",

        "Pick a layer: Ground (land / water / sand / pier ...), Nature (trees, rocks, scorched earth, lava, flowers, fish) or Buildings.\n\n" +
        "Tools: Point (drag to paint), Fill (flood fill), Line, Rect, Square, Circle, Oval (drag from one corner to the other) and Spray. " +
        "Shapes preview while you drag and are committed when you let go.\n\n" +
        "Brush size, left-right / top-bottom symmetry and Filled shapes (off = outline only) are on the left. " +
        "Right-click a tile to pick up its ground or nature as your brush. Undo / Redo keep 60 steps (Ctrl+Z / Ctrl+Y).\n\n" +
        "Coast (bottom row): Outline paints the shore with your brush, Grow adds one ring of land, Shrink removes one. " +
        "Nature brush \"Erase\" removes nature.",

        "Switch to the Buildings layer. Use < and > to choose the type (id, name, tier, HP and size are shown).\n\n" +
        "Click an empty tile to place the building there. The yellow preview shows exactly which tiles it will use; " +
        "the white dot on the map is its anchor tile. Click any tile of an existing building (or right-click it) to remove it.\n\n" +
        "Rotate (R) turns the building in steps of 90 degrees, Mirrored (T) flips it. HP 0 uses the type's own value " +
        "(Castle 200, House 15 ...); the + / - buttons set your own.\n\n" +
        "Docks, Fishing Ships and Lighthouses may stand on water; everything else needs land.\n\n" +
        "Hills layer: the game's hill pieces (Hill 3x3 and 2x2, Slope 2x2, 1x2 and 1x1). Click land to place the chosen piece (Rotate and Mirrored " +
        "work like for buildings), click a hill to remove it. Hills painted with the Ground brush \"Inner cliff\" are invisible in the game (striped on the " +
        "map) but still block building; \"Make painted hills visible\" covers them with pieces, big ones first. The game picks a random look for each piece " +
        "every time the island is applied.",

        "Press \"Check buildings\" before you apply. It marks problems with red frames on the map and lists them under it:\n\n" +
        "- a building outside the island,\n- nature (a tree, a rock ...) under a building,\n- a land building on water,\n- two buildings overlapping.\n\n" +
        "\"Clear bldg nature\" fixes the nature problem in one click. " +
        "Keep a Castle away from the island's edge - an edge Castle is refused by the game.\n\n" +
        "The game normally refuses invalid placements. While the editor applies an island, Island Tools allows them " +
        "(Settings > Game > Island Tools, \"Allow every placement while applying\") so one bad building does not stop everything - but it can still look wrong in play.",

        "Apply to MY island - replaces your island, right now, with what you see in the editor. Do this early in a match: " +
        "the buildings you had are replaced by the ones in the editor.\n\n" +
        "Apply to SELECTED - the same for the island of the player chosen with \"Next player\". " +
        "EXPERIMENTAL and untested: it is only expected to reach other players when you are the host, and it can desynchronise the game. " +
        "Tell everyone before you use it.\n\n" +
        "Known open problems: applying to your own island can report placement errors and lose buildings, and the turn timer may break afterwards. " +
        "The BepInEx log shows an [import] line with the counts.",

        "Copy code puts the current island code on the clipboard. Send it to a friend, or paste it into the browser island editor for more room and tools.\n\n" +
        "\"Game orientation (Y up)\" makes the map match what you see in the game; turn it off to see raw grid rows.\n\n" +
        "If the game still reacts to clicks on the editor (for example places a building), deselect the building in the game's bottom bar before you paint.\n\n" +
        "Moving around the map: the mouse wheel zooms in and out around the pointer, dragging with the right or middle mouse button moves the view, " +
        "and \"Fit map\" shows the whole island again. A short right-click without moving still picks the brush or removes a building.\n\n" +
        "\"Full screen\" gives the map more room; the - / + buttons in the title bar change the window's size, which is remembered.",

        "\"That player has no island yet\" - you are not in a running match yet. Wait until the island is visible, then try again.\n\n" +
        "Buildings look wrong after Apply - run \"Check buildings\", fix the red frames and apply again. Building names and shapes are read from the game; " +
        "a built-in copy is used until the game has loaded them.\n\n" +
        "Something looks broken? Send the BepInEx log lines starting with [island], [editor] or [import].",
    };

    internal static bool IsOpen => _win != null;

    internal static void Toggle() { if (IsOpen) Close(); else Open(); }

    internal static void Close() => _win?.Close();

    internal static void Open()
    {
        if (IsOpen) { _win.BringToFront(); return; }
        _page = 0;
        _win = Windows.Ui?.Custom.Window(w =>
        {
            w.Title("Island Editor - tutorial").Size(760f, 600f).Position(140f, 90f).Draggable().Closable().Fullscreenable().FullscreenButton();
            w.OnClose(() => { _win = null; _title = _body = null; _scroll = null; });
            _title = w.Body.Text(Titles[0])?.Size(22f).Color(new Color(1f, 0.82f, 0.45f, 1f));
            _scroll = w.Body.Scroll(s => _body = s.Paragraph(Pages[0])?.Size(18f))?.FillWidth().FillHeight() as UiScroll;
            w.Body.Row(r =>
            {
                r.Button("< Previous", () => Go(-1))?.FillWidth();
                r.Button("Open the editor", EditorWindow.Open)?.FillWidth();
                r.Button("Next >", () => Go(1))?.FillWidth();
            })?.Height(36f);
        });
    }

    private static void Go(int d)
    {
        _page = Mathf.Clamp(_page + d, 0, Pages.Length - 1);
        if (_title != null) _title.Value = Titles[_page];
        if (_body != null) _body.Value = Pages[_page];
        _scroll?.ToTop();
    }
}
