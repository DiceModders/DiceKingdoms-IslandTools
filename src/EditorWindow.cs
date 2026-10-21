using System;
using System.Collections.Generic;
using System.Text;
using DiceModders.UiKit;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace DiceModders.IslandTools;

// Inside namespace DiceModders.* the bare name "UiKit" would be the NAMESPACE DiceModders.UiKit; this alias (declared inside
// our namespace, so it wins) makes it mean the static class again.
using UiKit = DiceModders.UiKit.UiKit;

/// <summary>
/// The island editor window, built with UiKit. The island is drawn into a texture that is shown in a UiKit image; the image
/// reports pointer positions as 0..1 coordinates, which are turned into tiles here. All editing logic lives in <see cref="EditorModel"/>.
/// </summary>
internal sealed class EditorWindow
{
    private const int T = 8;                                   // texels per tile in the map texture
    private static readonly string[] ToolNames = { "Point", "Fill", "Line", "Rect", "Square", "Circle", "Oval", "Spray" };
    private static readonly string[] LayerNames = { "Ground", "Nature", "Buildings" };

    private static readonly EditorModel M = new();      // static: the island you are editing survives closing the window / a new match
    private static bool _yUp = true;                    // draw the map like the game (Y up)

    private WindowHandle _win;
    private string _tableSource;
    private string _titleShown;
    private bool _painting;
    private bool _texDirty = true, _uiDirty = true, _viewDirty = true;
    private bool _refreshing;                           // set while widgets are updated from the model, so their callbacks do nothing
    private string _ghostKey = "";
    private (int X, int Y)? _hoverTile;
    private readonly List<string> _checkLines = new();

    // widgets
    private ImageHandle _map;
    private LabelHandle _noData, _status, _hover, _checks, _playerLbl, _bName, _bInfo;
    private ContainerHandle _paintGroup, _groundGroup, _natureGroup, _sprayGroup, _buildGroup;
    private readonly List<ButtonHandle> _layerBtn = new(), _toolBtn = new(), _groundBtn = new(), _natureBtn = new();
    private ButtonHandle _rotateBtn;
    private ToggleHandle _symX, _symY, _fill, _yUpTog, _mirrorTog;
    private ScrollHandle _left;
    private readonly List<Action> _steppers = new();      // refresh the value text of the - / + rows

    // map texture
    private Texture2D _tex;
    private Color32[] _px;        // the island drawn as it is
    private Color32[] _view;      // _px plus the hover preview; this is what is uploaded

    public bool IsOpen => _win != null && _win.IsOpen;

    /// <summary>The island currently in the editor as an island code (for the players dashboard), or null when nothing is loaded.</summary>
    internal static byte[] CurrentCodeBytes() => M.HasData ? IslandCodec.ToCodeBytes(M.Data) : null;
    internal static bool HasIsland => M.HasData;

    public void Close()
    {
        if (_win != null) _win.Close();
        _win = null;
    }

    // ---- building the window --------------------------------------------------------------------------------------------------

    public void Open()
    {
        _tex = NewTexture(49 * T);
        _win = UiKit.Window(w =>
        {
            w.Size(ModPlugin.WindowWidth.Value, ModPlugin.WindowHeight.Value).Draggable().Fullscreenable().Closable().Title("Island Editor");
            w.FullscreenButton();
            w.TitleBar.Button("Help", EditorUi.ToggleHelp);
            w.TitleBar.Button("-", () => Resize(0.9f));
            w.TitleBar.Button("+", () => Resize(1.1f));
            w.Position(60f, 40f);

            var b = w.Body;
            b.Spacing(6f);

            // where the island comes from / goes to
            b.Horizontal(r =>
            {
                r.Button("Load MY island", () => LoadFrom(IslandOps.OwnGrid(out string me), me), 15f);
                r.Button("Apply to MY island", () => ApplyTo(IslandOps.OwnGrid(out string me2), me2), 15f);
                r.Button("New blank island", NewBlank, 15f);
                r.Button("Paste code", PasteCode, 15f);
                r.Button("Copy code", CopyCode, 15f);
            }).FixedH(32f);
            b.Horizontal(r =>
            {
                _playerLbl = r.Label("Other player: (press Next player)", 16f).WeightW(2f);
                r.Button("Players...", EditorUi.TogglePlayers, 15f);
                r.Button("Next player", () => { IslandOps.CyclePlayer(); _uiDirty = true; }, 15f);
                r.Button("Load SELECTED", () => LoadFrom(IslandOps.SelectedGrid(out string sel), sel), 15f);
                r.Button("Apply to SELECTED", () => ApplyTo(IslandOps.SelectedGrid(out string sel2), sel2), 15f);
            }).FixedH(32f);

            // tools on the left, the map on the right
            b.Horizontal(main =>
            {
                main.Spacing(10f);
                _left = main.ScrollView(BuildLeftPanel).FixedW(LeftWidth());
                main.Vertical(col =>
                {
                    _noData = col.Paragraph("No island loaded.\nUse \"Load MY island\" during a match, \"Paste code\", or \"New blank island\".", 18f);
                    _map = col.Image(_tex).Filter(FilterMode.Point);
                    _map.OnPointer(OnPointer);
                    _map.Root.gameObject.SetActive(false);
                }).WeightW(1f);
            }).WeightH(1f);

            // global actions
            b.Horizontal(r =>
            {
                r.Button("Undo", () => { if (M.CanUndo) { M.Undo(); AfterEdit(true); } }, 15f);
                r.Button("Redo", () => { if (M.CanRedo) { M.Redo(); AfterEdit(true); } }, 15f);
                r.Button("Check buildings", RunCheck, 15f);
                r.Button("Clear bldg nature", () => { M.ClearNatureUnderBuildings(); _checkLines.Clear(); SetStatus("Nature under buildings removed."); AfterEdit(true); }, 15f);
                r.Button("All HP default", () => { M.SetAllHpToDefault(); SetStatus("Every building now has its type's default HP."); AfterEdit(false); }, 15f);
            }).FixedH(32f);
            b.Horizontal(r =>
            {
                r.Button("Coast: Outline", () => { M.Morph("outline"); AfterEdit(true); }, 15f);
                r.Button("Coast: Grow", () => { M.Morph("grow"); AfterEdit(true); }, 15f);
                r.Button("Coast: Shrink", () => { M.Morph("shrink"); AfterEdit(true); }, 15f);
                _yUpTog = r.Toggle("Game orientation (Y up)", _yUp, v =>
                {
                    if (_refreshing) return;
                    _yUp = v; _ghostKey = ""; AfterEdit(true);
                });
                _yUpTog.WeightW(2f);
            }).FixedH(32f);

            // status
            _status = b.Label("Load your island, paste a code or start a blank island.", 16f);
            _hover = b.Label("", 15f).SetColor(new Color(0.75f, 0.8f, 0.9f, 1f));
            _checks = b.Paragraph("", 15f).SetColor(new Color(1f, 0.55f, 0.5f, 1f));     // wraps, grows with the problem list (max 4 lines)
        });

        ModPlugin.L.LogInfo("[ui] island editor window built.");
        _uiDirty = true; _texDirty = true; _viewDirty = true;
    }

    private static float LeftWidth() => Mathf.Clamp(ModPlugin.WindowWidth.Value * 0.3f, 240f, 340f);

    /// <summary>Makes the window bigger / smaller (title bar - and + buttons) and remembers the size in the config.</summary>
    private void Resize(float factor)
    {
        if (!IsOpen) return;
        int w = Mathf.Clamp(Mathf.RoundToInt(ModPlugin.WindowWidth.Value * factor), 720, 2400);
        int h = Mathf.Clamp(Mathf.RoundToInt(ModPlugin.WindowHeight.Value * factor), 560, 1400);
        ModPlugin.WindowWidth.Value = w;
        ModPlugin.WindowHeight.Value = h;
        // WindowHandle.Size re-applies the position the window was OPENED at, so a dragged window would jump back: put it where it is now.
        var at = _win.Root.anchoredPosition;
        _win.Size(w, h);
        _win.Position(at.x, -at.y);
        _left.FixedW(LeftWidth());
        SetStatus($"Window size {w} x {h}.");
    }

    /// <summary>A row "text  [-] value [+]". Plain buttons work at every width, unlike the game's wide slider.</summary>
    private void Stepper(ContainerHandle parent, string text, Func<int> get, Action<int> set, int min, int max, int step)
    {
        LabelHandle value = null;
        parent.Horizontal(r =>
        {
            r.Spacing(4f);
            r.Label(text, 15f).WeightW(1f);
            r.Button("-", () => { set(Mathf.Max(min, get() - step)); _uiDirty = true; }, 18f).FixedW(38f);
            value = r.Label("", 15f).Center().FixedW(48f);
            r.Button("+", () => { set(Mathf.Min(max, get() + step)); _uiDirty = true; }, 18f).FixedW(38f);
        }).FixedH(32f);
        _steppers.Add(() => value.SetText(get().ToString()));
    }

    private void BuildLeftPanel(ContainerHandle c)
    {
        c.Spacing(4f);
        c.Padding(0, 26, 0, 0);                 // keeps the buttons clear of the scroll bar on the right

        c.Horizontal(r =>
        {
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                _layerBtn.Add(r.Button(LayerNames[i], () => { M.Layer = (EditLayer)idx; _ghostKey = ""; _uiDirty = true; }, 16f));
            }
        }).FixedH(32f);

        // ---- ground / nature painting
        _paintGroup = c.Vertical(p =>
        {
            p.Spacing(4f);
            Pairs(p, ToolNames, _toolBtn, i => { M.Tool = (EditTool)i; _uiDirty = true; });

            p.Label("Brush:", 16f);
            _groundGroup = p.Vertical(g =>
            {
                g.Spacing(4f);
                Pairs(g, EditorModel.GroundNames, _groundBtn, i => { M.GroundBrush = i; _uiDirty = true; });
            });
            _natureGroup = p.Vertical(g =>
            {
                g.Spacing(4f);
                var names = new List<string> { "Erase" };
                names.AddRange(EditorModel.NatureNames);
                Pairs(g, names.ToArray(), _natureBtn, i => { M.NatureBrush = i - 1; _uiDirty = true; });
            });

            Stepper(p, "Brush size", () => M.BrushSize, v => M.BrushSize = v, 1, 9, 1);

            _symX = p.Toggle("Symmetry left-right", M.SymX, v => { if (_refreshing) return; M.SymX = v; });
            _symY = p.Toggle("Symmetry top-bottom", M.SymY, v => { if (_refreshing) return; M.SymY = v; });
            _fill = p.Toggle("Filled shapes (off = outline)", M.FillShapes, v => { if (_refreshing) return; M.FillShapes = v; });

            _sprayGroup = p.Vertical(s =>
            {
                s.Spacing(4f);
                Stepper(s, "Spray radius", () => M.SprayRadius, v => M.SprayRadius = v, 1, 15, 1);
                Stepper(s, "Spray density %", () => M.SprayDensity, v => M.SprayDensity = v, 5, 100, 5);
            });
        });

        // ---- buildings
        _buildGroup = c.Vertical(g =>
        {
            g.Spacing(4f);
            g.Paragraph("Click an empty tile to place, click a building tile to remove it.", 15f);
            g.Horizontal(r =>
            {
                r.Button("<", () => CycleBuilding(-1), 18f).FixedW(44f);
                _bName = r.Label("", 16f).Center().WeightW(1f);
                r.Button(">", () => CycleBuilding(1), 18f).FixedW(44f);
            }).FixedH(32f);
            _bInfo = g.Label("", 15f);
            g.Horizontal(r =>
            {
                _rotateBtn = r.Button("Rotate", () => { M.BOrient = (M.BOrient + 1) % 4; _ghostKey = ""; _uiDirty = true; }, 16f);
                _mirrorTog = r.Toggle("Mirrored", M.BMirror, v => { if (_refreshing) return; M.BMirror = v; _ghostKey = ""; _uiDirty = true; });
            }).FixedH(32f);
            Stepper(g, "HP (0 = default)", () => M.BHealth, v => M.BHealth = v, 0, 1000, 5);
            g.Paragraph("Yellow preview = where it goes. Right-click removes. The white dot is the anchor tile.", 15f);
        });
    }

    /// <summary>Buttons in rows of two (a single last one is stretched).</summary>
    private static void Pairs(ContainerHandle parent, string[] names, List<ButtonHandle> store, Action<int> click)
    {
        for (int i = 0; i < names.Length; i += 2)
        {
            int a = i, b = i + 1;
            parent.Horizontal(r =>
            {
                r.Spacing(4f);
                store.Add(r.Button(names[a], () => click(a), 16f));
                if (b < names.Length) store.Add(r.Button(names[b], () => click(b), 16f));
                else r.Label("", 16f).WeightW(1f);                    // keeps the single button at half width
            }).FixedH(32f);
        }
    }

    private static Texture2D NewTexture(int n)
    {
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        t.filterMode = FilterMode.Point;
        t.wrapMode = TextureWrapMode.Clamp;
        return t;
    }

    // ---- per-frame work ------------------------------------------------------------------------------------------------------

    /// <summary>Every frame while the editor exists: widget refresh, map redraw, hover preview.</summary>
    public void Tick()
    {
        if (!IsOpen) return;
        try
        {
            string who = State.SelectedName ?? "(press Next player)";
            string text = "Other player: " + who;
            if (_playerLbl != null && _playerLbl.GetText() != text) _playerLbl.SetText(text);

            if (_uiDirty) RefreshUi();
            if (_texDirty) RenderTexture();
            UpdatePreview();
            if (_viewDirty) Upload();
        }
        catch (Exception e) { ModPlugin.L.LogError("[editor] tick: " + e); }
    }

    // keyboard shortcuts (called by EditorUi while the editor is open)
    public void KeyUndo() { if (M.CanUndo) { M.Undo(); AfterEdit(true); } }
    public void KeyRedo() { if (M.CanRedo) { M.Redo(); AfterEdit(true); } }
    public void KeyRotate() { M.BOrient = (M.BOrient + 1) % 4; _ghostKey = ""; _uiDirty = true; }
    public void KeyMirror() { M.BMirror = !M.BMirror; _ghostKey = ""; _uiDirty = true; }

    private void SetStatus(string s) { if (_status != null) _status.SetText(s); }

    private void AfterEdit(bool redraw)
    {
        _uiDirty = true;
        if (redraw) _texDirty = true;
        _checkLines.Clear();
    }

    private static void Show(ContainerHandle g, bool on)
    {
        if (g != null && g.Root.gameObject.activeSelf != on) g.Root.gameObject.SetActive(on);
    }

    private void RefreshUi()
    {
        _uiDirty = false;
        _refreshing = true;
        try
        {
            if (!ReferenceEquals(_tableSource, State.BuildingDumpText))
            {
                _tableSource = State.BuildingDumpText;
                M.Table = BuildingTable.FromDumpOrDefault(_tableSource);
                if (M.Table.Get(M.BType) == null && M.Table.ById.Count > 0) { foreach (var k in M.Table.ById.Keys) { M.BType = k; break; } }
            }
            string title = "Island Editor" + (M.Dirty ? " *" : "");
            if (title != _titleShown) { _titleShown = title; _win.Title(title); }

            bool paint = M.Layer != EditLayer.Buildings;
            Show(_paintGroup, paint);
            Show(_buildGroup, !paint);
            Show(_groundGroup, M.Layer == EditLayer.Ground);
            Show(_natureGroup, M.Layer == EditLayer.Nature);
            Show(_sprayGroup, paint && M.Tool == EditTool.Spray);
            for (int i = 0; i < _layerBtn.Count; i++) _layerBtn[i].SetSelected((int)M.Layer == i);
            for (int i = 0; i < _toolBtn.Count; i++) _toolBtn[i].SetSelected((int)M.Tool == i);
            for (int i = 0; i < _groundBtn.Count; i++) _groundBtn[i].SetSelected(M.GroundBrush == i);
            for (int i = 0; i < _natureBtn.Count; i++) _natureBtn[i].SetSelected(M.NatureBrush == i - 1);

            foreach (var refresh in _steppers) refresh();
            _symX.SetValue(M.SymX);
            _symY.SetValue(M.SymY);
            _fill.SetValue(M.FillShapes);
            _yUpTog.SetValue(_yUp);

            var info = M.Table.Get(M.BType);
            _bName.SetText(M.BType + ": " + M.Table.NameOf(M.BType));
            _bInfo.SetText(info == null ? "(unknown type)" : $"tier {info.Tier}, HP {info.Hp}, {info.Shape.Length} tiles");
            _rotateBtn.SetText("Rotate (" + (((M.BOrient % 4) + 4) % 4) + ")");
            _mirrorTog.SetValue(M.BMirror);

            _checks.SetText(string.Join("\n", _checkLines));
            _noData.Root.gameObject.SetActive(!M.HasData);
            _map.Root.gameObject.SetActive(M.HasData);
            if (!_ghostKey.Contains("|")) _hover.SetText(M.HasData ? M.Summary() : "");
        }
        finally { _refreshing = false; }
    }

    private void CycleBuilding(int dir)
    {
        var ids = new List<int>(M.Table.ById.Keys);
        if (ids.Count == 0) return;
        int pos = Math.Max(0, ids.IndexOf(M.BType));
        M.BType = ids[(pos + dir + ids.Count) % ids.Count];
        _ghostKey = ""; _uiDirty = true;
    }

    // ---- map texture ----------------------------------------------------------------------------------------------------------

    internal static readonly Color32[] GroundColors =
    {
        C(0x1f6f8b), C(0x5b6b73), C(0x4a8f3c), C(0xe8d5a8), C(0x8a7a63), C(0x9a9a9a), C(0xb98a4d),
    };
    internal static readonly Color32[] NatureColors = { C(0x1c5e2a), C(0x6b6b6b), C(0x5a3d1f), C(0xc1572b), C(0xd43f7a), C(0x2ea3c7) };

    private static Color32 C(int rgb) => new Color32((byte)((rgb >> 16) & 255), (byte)((rgb >> 8) & 255), (byte)(rgb & 255), 255);

    internal static Color32 BuildingColor(int type)
    {
        float h = (type * 47f) % 360f / 60f, s = 0.7f, l = 0.4f;
        float c = (1f - Mathf.Abs(2f * l - 1f)) * s, x = c * (1f - Mathf.Abs(h % 2f - 1f)), r = 0f, g = 0f, b = 0f;
        if (h < 1f) { r = c; g = x; } else if (h < 2f) { r = x; g = c; } else if (h < 3f) { g = c; b = x; }
        else if (h < 4f) { g = x; b = c; } else if (h < 5f) { r = x; b = c; } else { r = c; b = x; }
        float m = l - c / 2f;
        return new Color32((byte)((r + m) * 255f), (byte)((g + m) * 255f), (byte)((b + m) * 255f), 255);
    }

    /// <summary>Paints a w x h block of texels inside tile (gx, gy) at offset (ox, oy); blend 1 = solid, less = mixed with what is there.</summary>
    private void Block(Color32[] target, int side, int gx, int gy, int ox, int oy, int w, int h, Color32 col, float blend = 1f)
    {
        int n = side * T;
        int row0 = (_yUp ? gy : side - 1 - gy) * T + oy;     // texture row 0 is the bottom of the picture
        int col0 = gx * T + ox;
        for (int yy = 0; yy < h; yy++)
        {
            int rr = row0 + yy;
            if (rr < 0 || rr >= n) continue;
            for (int xx = 0; xx < w; xx++)
            {
                int cc = col0 + xx;
                if (cc < 0 || cc >= n) continue;
                if (blend >= 1f) { target[rr * n + cc] = col; continue; }
                var old = target[rr * n + cc];
                target[rr * n + cc] = new Color32(
                    (byte)(old.r + (col.r - old.r) * blend),
                    (byte)(old.g + (col.g - old.g) * blend),
                    (byte)(old.b + (col.b - old.b) * blend), 255);
            }
        }
    }

    private void RenderTexture()
    {
        _texDirty = false;
        _viewDirty = true;
        _ghostKey = "";                                   // the preview must be drawn again on top of the new picture
        if (!M.HasData) return;
        var d = M.Data;
        int side = M.Side, n = side * T;

        if (_px == null || _px.Length != n * n)
        {
            _px = new Color32[n * n];
            _view = new Color32[n * n];
            if (_tex != null) UnityEngine.Object.Destroy(_tex);
            _tex = NewTexture(n);
            _map.SetTexture(_tex);
        }

        // ground (with a darker bottom/right edge on land tiles so the tile grid is visible)
        for (int gy = 0; gy < side; gy++)
            for (int gx = 0; gx < side; gx++)
            {
                int g = d.Ground[gy * side + gx];
                var col = g >= 0 && g < GroundColors.Length ? GroundColors[g] : new Color32(255, 0, 255, 255);
                Block(_px, side, gx, gy, 0, 0, T, T, col);
                if (g != 0)
                {
                    var dark = new Color32((byte)(col.r * 0.8f), (byte)(col.g * 0.8f), (byte)(col.b * 0.8f), 255);
                    Block(_px, side, gx, gy, T - 1, 0, 1, T, dark);
                    Block(_px, side, gx, gy, 0, 0, T, 1, dark);
                }
            }
        // nature
        for (int gy = 0; gy < side; gy++)
            for (int gx = 0; gx < side; gx++)
            {
                int nat = d.Nature[gy * side + gx];
                if (nat == -1) continue;
                Block(_px, side, gx, gy, 2, 2, T - 4, T - 4, nat >= 0 && nat < NatureColors.Length ? NatureColors[nat] : new Color32(255, 0, 255, 255));
            }
        // buildings
        foreach (var b in d.Buildings)
        {
            var col = BuildingColor(b.Type);
            foreach (var (fx, fy) in M.Footprint(b))
                if (M.InBounds(fx, fy)) Block(_px, side, fx, fy, 1, 1, T - 2, T - 2, col);
            if (M.InBounds(b.X, b.Y)) Block(_px, side, b.X, b.Y, T / 2 - 1, T / 2 - 1, 2, 2, new Color32(255, 255, 255, 255));
        }
        // problems from "Check buildings": red frame
        var red = new Color32(255, 40, 40, 255);
        foreach (var (px, py) in M.Problems)
        {
            if (!M.InBounds(px, py)) continue;
            Block(_px, side, px, py, 0, 0, T, 1, red); Block(_px, side, px, py, 0, T - 1, T, 1, red);
            Block(_px, side, px, py, 0, 0, 1, T, red); Block(_px, side, px, py, T - 1, 0, 1, T, red);
        }
    }

    /// <summary>Copies the picture, adds the hover preview and uploads it to the GPU.</summary>
    private void Upload()
    {
        _viewDirty = false;
        if (!M.HasData || _px == null) return;
        Array.Copy(_px, _view, _px.Length);

        var tile = _hoverTile;
        if (tile != null)
        {
            var (x, y) = tile.Value;
            int side = M.Side;
            if (M.Layer == EditLayer.Buildings)
            {
                var g = M.CurrentBuilding(x, y);
                foreach (var (fx, fy) in M.Footprint(g.Type, x, y, g.Orient, g.Mirror))
                    if (M.InBounds(fx, fy)) Block(_view, side, fx, fy, 0, 0, T, T, new Color32(255, 255, 0, 255), 0.5f);
            }
            else if (M.InBounds(x, y)) Block(_view, side, x, y, 0, 0, T, T, new Color32(255, 255, 255, 255), 0.35f);
        }

        _tex.SetPixels32(new Il2CppStructArray<Color32>(_view));
        _map.Refresh();
    }

    // ---- pointer on the map ---------------------------------------------------------------------------------------------------

    private (int X, int Y) ToTile(Vector2 uv, bool clamp)
    {
        int side = M.Side;
        int x = Mathf.FloorToInt(uv.x * side);
        int row = Mathf.FloorToInt(uv.y * side);              // counted from the bottom of the picture
        int y = _yUp ? row : side - 1 - row;
        if (clamp) { x = Mathf.Clamp(x, 0, side - 1); y = Mathf.Clamp(y, 0, side - 1); }
        return (x, y);
    }

    private void OnPointer(PointerEvent e)
    {
        if (!M.HasData) return;
        switch (e.Kind)
        {
            case PointerKind.Down:
            {
                var t = ToTile(e.Uv, false);
                if (!M.InBounds(t.X, t.Y)) return;
                if (e.Button == 0) { M.PointerDown(t.X, t.Y); _painting = true; _checkLines.Clear(); }
                else if (e.Button == 1) M.RightClick(t.X, t.Y);
                else return;
                _texDirty = true; _uiDirty = true;
                break;
            }
            case PointerKind.Drag:
                if (_painting)
                {
                    var t = ToTile(e.Uv, true);
                    M.PointerDrag(t.X, t.Y);
                    _texDirty = true;
                }
                SetHover(ToTile(e.Uv, true));
                break;
            case PointerKind.Up:
                if (_painting)
                {
                    M.PointerUp();
                    _painting = false;
                    _uiDirty = true;
                }
                break;
            case PointerKind.Move:
            case PointerKind.Enter:
                SetHover(ToTile(e.Uv, false));
                break;
            case PointerKind.Exit:
                if (!_painting) SetHover(null);
                break;
        }
    }

    private void SetHover((int X, int Y)? tile)
    {
        if (tile != null && !M.InBounds(tile.Value.X, tile.Value.Y)) tile = null;
        _hoverTile = tile;
    }

    /// <summary>Redraws the preview when the hovered tile, or anything the preview depends on, changed.</summary>
    private void UpdatePreview()
    {
        var tile = _hoverTile;
        string key = tile == null ? "" : $"{tile.Value.X},{tile.Value.Y}|{M.Layer}|{M.BType}|{M.BOrient}|{M.BMirror}|{_yUp}|{(M.HasData ? M.Data.Buildings.Count : 0)}";
        if (key == _ghostKey) return;
        _ghostKey = key;
        _viewDirty = true;
        if (tile == null) { _hover.SetText(M.HasData ? M.Summary() : ""); return; }
        _hover.SetText(DescribeTile(tile.Value.X, tile.Value.Y));
    }

    private string DescribeTile(int x, int y)
    {
        if (!M.InBounds(x, y)) return "";
        int g = M.Data.Ground[M.Cell(x, y)], n = M.Data.Nature[M.Cell(x, y)];
        var sb = new StringBuilder($"tile {x},{y}: {(g >= 0 && g < EditorModel.GroundNames.Length ? EditorModel.GroundNames[g] : "ground " + g)}");
        if (n != -1) sb.Append(", ").Append(n >= 0 && n < EditorModel.NatureNames.Length ? EditorModel.NatureNames[n] : "nature " + n);
        int bi = M.HitBuilding(x, y);
        if (bi >= 0)
        {
            var b = M.Data.Buildings[bi];
            sb.Append($", {M.Table.NameOf(b.Type)} (HP {b.Health}, orient {b.Orient}, mirror {b.Mirror})");
        }
        return sb.ToString();
    }

    // ---- actions --------------------------------------------------------------------------------------------------------------

    private void RunCheck()
    {
        var msgs = M.Check();
        _checkLines.Clear();
        if (msgs.Count == 0) _checkLines.Add("No problems found (bounds / water / nature / overlap).");
        else
        {
            var seen = new HashSet<string>();
            foreach (var s in msgs) if (seen.Add(s)) _checkLines.Add(s);
            if (_checkLines.Count > 3) { int more = _checkLines.Count - 3; _checkLines.RemoveRange(3, more); _checkLines.Add("... and " + more + " more (red frames on the map)"); }
        }
        SetStatus(msgs.Count == 0 ? "Check done: all good." : "Check done: " + msgs.Count + " problem(s) - see the red frames.");
        _texDirty = true; _uiDirty = true;
    }

    private void NewBlank()
    {
        M.NewBlank(M.HasData ? M.Side : 49);
        _checkLines.Clear();
        SetStatus("Blank island (all water). Paint some land first.");
        _texDirty = true; _uiDirty = true;
    }

    internal void LoadFrom(object grid, string who)
    {
        try
        {
            if (grid == null) { SetStatus("Cannot find that island - join/start a match first (see the BepInEx log)."); return; }
            byte[] bytes = IslandOps.ExportBytes(grid);
            if (bytes == null || bytes.Length == 0) { SetStatus("The game returned no island data."); return; }
            var d = IslandCodec.FromCodeBytes(bytes);
            if (!d.SideIsExact) { SetStatus("The island has an unexpected layout (tile arrays do not form a square) - not loaded."); return; }
            M.Load(d);
            _checkLines.Clear();
            SetStatus($"Loaded {who}'s island: {M.Summary()}" + (d.TrailingBytes > 0 ? $" (note: {d.TrailingBytes} unread byte(s))" : "") + ".");
            _texDirty = true; _uiDirty = true;
        }
        catch (Exception e) { SetStatus("Load failed: " + e.Message); ModPlugin.L.LogError("[editor] load: " + e); }
    }

    private void ApplyTo(object grid, string who)
    {
        try
        {
            if (!M.HasData) { SetStatus("Nothing to apply - load or create an island first."); return; }
            if (grid == null) { SetStatus("Cannot find that island - join/start a match first (see the BepInEx log)."); return; }
            int problems = M.Check().Count;
            byte[] bytes = IslandCodec.ToCodeBytes(M.Data);
            State.BypassedChecks = 0;
            bool ok = IslandOps.ImportBytes(grid, bytes, out string error);
            if (ok)
            {
                M.Dirty = false;
                SetStatus($"Applied to {who}'s island" + IslandOps.ImportNote() + (problems > 0 ? $" ({problems} placement problem(s) were present)" : "") + ".");
            }
            else SetStatus("Apply failed: " + error);
            _uiDirty = true;
        }
        catch (Exception e) { SetStatus("Apply failed: " + e.Message); ModPlugin.L.LogError("[editor] apply: " + e); }
    }

    private void PasteCode()
    {
        try
        {
            if (!Clipboard.TryGetText(out string text)) { SetStatus("The clipboard has no text."); return; }
            if (!Clipboard.TryDecodeBase64(text, out byte[] bytes)) { SetStatus("The clipboard text is not an island code."); return; }
            var d = IslandCodec.FromCodeBytes(bytes);
            if (!d.SideIsExact) { SetStatus("That code has an unexpected layout - not loaded."); return; }
            M.Load(d); M.Dirty = true; _checkLines.Clear();
            SetStatus("Loaded the code from the clipboard: " + M.Summary() + ".");
            _texDirty = true; _uiDirty = true;
        }
        catch (Exception e) { SetStatus("That is not a valid island code: " + e.Message); }
    }

    private void CopyCode()
    {
        try
        {
            if (!M.HasData) { SetStatus("Nothing to copy - load or create an island first."); return; }
            string code = Convert.ToBase64String(IslandCodec.ToCodeBytes(M.Data));
            SetStatus(Clipboard.SetText(code) ? $"Island code copied ({code.Length} characters)." : "Could not write to the clipboard.");
        }
        catch (Exception e) { SetStatus("Copy failed: " + e.Message); }
    }
}
