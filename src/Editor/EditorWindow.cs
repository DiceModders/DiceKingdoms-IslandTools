using System;
using System.Collections.Generic;
using System.Text;
using DiceModders.ModCore;
using DiceModders.UiKit.Custom;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;

namespace DiceModders.IslandTools;

/// <summary>
/// The island editor window. The map picture, zoom and view position live in <see cref="MapView"/>; pointer positions from the UiKit image
/// are turned into tiles there. All editing logic lives in <see cref="EditorModel"/>.
/// The window redraws right after the event that changed something (a click, a pointer move, a new selection): every handler marks what
/// changed and ends with <see cref="Flush"/>. Nothing runs every frame.
/// </summary>
internal static class EditorWindow
{
    private static string[] ToolNames => new[] { Tx.T("tool.0"), Tx.T("tool.1"), Tx.T("tool.2"), Tx.T("tool.3"), Tx.T("tool.4"), Tx.T("tool.5"), Tx.T("tool.6"), Tx.T("tool.7") };
    private static string[] LayerNames => new[] { Tx.T("layer.0"), Tx.T("layer.1"), Tx.T("layer.2"), Tx.T("layer.3") };

    private static readonly EditorModel M = new();      // the island you are editing survives closing the window and a new match
    private static bool _yUp = true;                    // draw the map like the game (Y up)

    private static UiWindow _win;
    private static string _titleShown;
    private static bool _painting;
    private static bool _texDirty, _uiDirty, _viewDirty;
    private static bool _refreshing;                    // set while widgets are updated from the model, so their callbacks do nothing
    private static string _hoverKey = "";
    private static (int X, int Y)? _hoverTile;
    private static readonly List<string> _checkLines = new();

    // widgets
    private static UiImage _map;
    private static UiText _noData, _status, _hover, _checks, _playerLbl, _bName, _bInfo;
    private static UiContainer _paintGroup, _groundGroup, _natureGroup, _sprayGroup, _buildGroup, _hillGroup;
    private static readonly List<UiButton> _layerBtn = new(), _toolBtn = new(), _groundBtn = new(), _natureBtn = new(), _hillBtn = new();
    private static UiButton _rotateBtn, _hillRotateBtn;
    private static UiToggle _symX, _symY, _fill, _yUpTog, _mirrorTog, _hillMirrorTog;
    private static UiScroll _left;
    private static readonly List<Action> _steppers = new();      // refresh the value text of the - / + rows

    // map view
    private static Vector2 _pointerAt;   // last pointer position on the map (canvas units), the centre of wheel zooming
    private static int _dragButton = -1; // 1 / 2 while the right / middle button is held on the map (moves the view)
    private static float _dragMoved;     // how far that button moved: a short right press is a right-click, a longer one moves the view

    internal static bool IsOpen => _win != null;

    /// <summary>The island currently in the editor as an island code (for the players window), or null when nothing is loaded.</summary>
    internal static byte[] CurrentCode() => M.HasData ? IslandCodec.ToCodeBytes(M.Data) : null;

    internal static void Install() => Islands.SelectionChanged += () => { _uiDirty = true; Flush(); };

    internal static void Toggle() { if (IsOpen) Close(); else Open(); }

    // the editing keys (EditorKeys); they only fire while the window is open
    internal static void KeyRotate() => Run(() => { M.BOrient = (M.BOrient + 1) % 4; _hoverKey = ""; _uiDirty = true; });
    internal static void KeyMirror() => Run(() => { M.BMirror = !M.BMirror; _hoverKey = ""; _uiDirty = true; });
    internal static void KeyUndo() => Run(() => { if (M.CanUndo) { M.Undo(); AfterEdit(true); } });
    internal static void KeyRedo() => Run(() => { if (M.CanRedo) { M.Redo(); AfterEdit(true); } });

    internal static void Close() => _win?.Close();

    // ---- building the window --------------------------------------------------------------------------------------------------

    internal static void Open()
    {
        if (IsOpen) { _win.BringToFront(); return; }
        Buildings.Refresh();   // buildings a mod added after the first read (the Casino) show up in the list
        _layerBtn.Clear(); _toolBtn.Clear(); _groundBtn.Clear(); _natureBtn.Clear(); _hillBtn.Clear(); _steppers.Clear();
        _win = Windows.Ui?.Custom.Window(w =>
        {
            w.Title(Tx.T("editor.title")).Size(Options.WindowWidth.Value, Options.WindowHeight.Value).Position(60f, 40f)
             .Draggable().Closable().Fullscreenable().FullscreenButton();
            w.OnClose(Closed);
            w.TitleBar?.Button(Tx.T("editor.help"), HelpWindow.Toggle);
            w.TitleBar?.Button("-", () => Resize(0.9f));
            w.TitleBar?.Button("+", () => Resize(1.1f));

            var b = w.Body;
            b.Spacing(6f);

            // where the island comes from / goes to
            b.Row(r =>
            {
                Btn(r, Tx.T("editor.loadMine"), () => LoadFrom(Islands.Local));
                Btn(r, Tx.T("editor.applyMine"), () => ApplyTo(Islands.Local));
                Btn(r, Tx.T("editor.newBlank"), NewBlank);
                Btn(r, Tx.T("editor.paste"), PasteCode);
                Btn(r, Tx.T("editor.copy"), CopyCode);
            })?.Height(34f);
            b.Row(r =>
            {
                _playerLbl = r.Text("")?.WeightW(2f);
                Btn(r, Tx.T("editor.players"), PlayersWindow.Toggle);
                Btn(r, Tx.T("editor.nextPlayer"), Islands.SelectNext);
                Btn(r, Tx.T("editor.loadSelected"), () => LoadFrom(Islands.Selected));
                Btn(r, Tx.T("editor.applySelected"), () => ApplyTo(Islands.Selected));
            })?.Height(34f);

            // tools on the left, the map on the right
            b.Row(main =>
            {
                main.Spacing(10f);
                _left = main.Scroll(BuildLeftPanel)?.FixedW(LeftWidth()).FillHeight();
                main.Column(col =>
                {
                    _noData = col.Paragraph(Tx.T("editor.noData"));
                    _map = col.Image((Texture)null)?.KeepAspect(false).FillWidth().FillHeight();
                    _map?.OnPointer(OnPointer);
                    _map?.OnScroll(n => Run(() => { MapView.Zoom(n, _pointerAt); _texDirty = true; }));
                    _map?.OnResize(_ => Run(() => _texDirty = true));     // drawn at the area's pixel size: redraw after a resize
                })?.FillWidth().FillHeight();
            })?.FillHeight();

            // global actions
            b.Row(r =>
            {
                Btn(r, Tx.T("editor.undo"), () => { if (M.CanUndo) { M.Undo(); AfterEdit(true); } });
                Btn(r, Tx.T("editor.redo"), () => { if (M.CanRedo) { M.Redo(); AfterEdit(true); } });
                Btn(r, Tx.T("editor.check"), RunCheck);
                Btn(r, Tx.T("editor.clearNature"), () => { M.ClearNatureUnderBuildings(); SetStatus(Tx.T("editor.natureCleared")); AfterEdit(true); });
                Btn(r, Tx.T("editor.hpDefault"), () => { M.SetAllHpToDefault(); SetStatus(Tx.T("editor.hpDone")); AfterEdit(false); });
            })?.Height(34f);
            b.Row(r =>
            {
                Btn(r, Tx.T("editor.coastOutline"), () => { M.Morph("outline"); AfterEdit(true); });
                Btn(r, Tx.T("editor.coastGrow"), () => { M.Morph("grow"); AfterEdit(true); });
                Btn(r, Tx.T("editor.coastShrink"), () => { M.Morph("shrink"); AfterEdit(true); });
                Btn(r, Tx.T("editor.fit"), () => { MapView.Fit(); _texDirty = true; });
                _yUpTog = r.Toggle(Tx.T("editor.yUp"), _yUp, v => Changed(() => { _yUp = v; AfterEdit(true); }))?.WeightW(2f);
            })?.Height(34f);

            // status
            _status = b.Text(Tx.T("editor.start"));
            _hover = b.Text("")?.Color(new Color(0.75f, 0.8f, 0.9f, 1f));
            _checks = b.Paragraph("")?.Color(new Color(1f, 0.55f, 0.5f, 1f));     // wraps, grows with the problem list (max 4 lines)
        });
        if (_win == null) return;
        _uiDirty = _texDirty = _viewDirty = true;
        Flush();
        Windows.Refresh();
    }

    private static void Closed()
    {
        _win = null;
        _map = null; _left = null;
        _noData = _status = _hover = _checks = _playerLbl = _bName = _bInfo = null;
        _painting = false; _hoverTile = null; _hoverKey = ""; _titleShown = null;
        _dragButton = -1;
        MapView.Release();
        Windows.Refresh();
    }

    /// <summary>A button whose click is followed by the redraw.</summary>
    private static UiButton Btn(UiContainer row, string text, Action click) => row.Button(text, () => Run(click))?.FillWidth();

    /// <summary>A toggle callback: ignored while the window itself sets the toggles, otherwise runs and redraws.</summary>
    private static void Changed(Action change) { if (!_refreshing) Run(change); }

    private static void Run(Action action)
    {
        try { action(); }
        catch (Exception e) { Plugin.L.LogError("[editor] " + e); }
        Flush();
    }

    private static float LeftWidth() => Mathf.Clamp(Options.WindowWidth.Value * 0.3f, 240f, 340f);

    /// <summary>Makes the window bigger / smaller (title bar - and + buttons) and remembers the size in the config.</summary>
    private static void Resize(float factor)
    {
        if (!IsOpen) return;
        int w = Mathf.Clamp(Mathf.RoundToInt(Options.WindowWidth.Value * factor), 720, 2400);
        int h = Mathf.Clamp(Mathf.RoundToInt(Options.WindowHeight.Value * factor), 560, 1400);
        Options.WindowWidth.Value = w;
        Options.WindowHeight.Value = h;
        var at = _win.Root.anchoredPosition;     // Size re-applies the opening position: keep the window where it was dragged to
        _win.Size(w, h);
        _win.Position(at.x, -at.y);
        _left?.FixedW(LeftWidth());
        SetStatus(Tx.T("editor.windowSize", w, h));
    }

    /// <summary>A row "text  [-] value [+]".</summary>
    private static void Stepper(UiContainer parent, string text, Func<int> get, Action<int> set, int min, int max, int step)
    {
        UiText value = null;
        parent.Row(r =>
        {
            r.Spacing(4f);
            r.Text(text)?.FillWidth();
            r.Button("-", () => Run(() => { set(Mathf.Max(min, get() - step)); _uiDirty = true; }))?.FixedW(38f);
            value = r.Text("")?.Align(TextAlignmentOptions.Center).FixedW(48f);
            r.Button("+", () => Run(() => { set(Mathf.Min(max, get() + step)); _uiDirty = true; }))?.FixedW(38f);
        })?.Height(32f);
        _steppers.Add(() => { if (value != null) value.Value = get().ToString(); });
    }

    private static void BuildLeftPanel(UiContainer c)
    {
        c.Spacing(4f);

        c.Row(r =>
        {
            for (int i = 0; i < LayerNames.Length; i++)
            {
                int idx = i;
                _layerBtn.Add(Btn(r, LayerNames[i], () => { M.Layer = (EditLayer)idx; _hoverKey = ""; _uiDirty = true; }));
            }
        })?.Height(32f);

        // ---- ground / nature painting
        _paintGroup = c.Column(p =>
        {
            p.Spacing(4f);
            Pairs(p, ToolNames, _toolBtn, i => { M.Tool = (EditTool)i; _uiDirty = true; });

            p.Text(Tx.T("editor.brush"));
            _groundGroup = p.Column(g => { g.Spacing(4f); Pairs(g, EditorModel.GroundNames, _groundBtn, i => { M.GroundBrush = i; _uiDirty = true; }); });
            _natureGroup = p.Column(g =>
            {
                g.Spacing(4f);
                var names = new List<string> { Tx.T("editor.erase") };
                names.AddRange(EditorModel.NatureNames);
                Pairs(g, names.ToArray(), _natureBtn, i => { M.NatureBrush = i - 1; _uiDirty = true; });
            });

            Stepper(p, Tx.T("editor.brushSize"), () => M.BrushSize, v => M.BrushSize = v, 1, 9, 1);

            _symX = p.Toggle(Tx.T("editor.symX"), M.SymX, v => Changed(() => M.SymX = v));
            _symY = p.Toggle(Tx.T("editor.symY"), M.SymY, v => Changed(() => M.SymY = v));
            _fill = p.Toggle(Tx.T("editor.filled"), M.FillShapes, v => Changed(() => M.FillShapes = v));

            _sprayGroup = p.Column(s =>
            {
                s.Spacing(4f);
                Stepper(s, Tx.T("editor.sprayRadius"), () => M.SprayRadius, v => M.SprayRadius = v, 1, 15, 1);
                Stepper(s, Tx.T("editor.sprayDensity"), () => M.SprayDensity, v => M.SprayDensity = v, 5, 100, 5);
            });
        });

        // ---- buildings
        _buildGroup = c.Column(g =>
        {
            g.Spacing(4f);
            g.Paragraph(Tx.T("editor.bldgHelp"));
            g.Row(r =>
            {
                r.Button("<", () => Run(() => CycleBuilding(-1)))?.FixedW(44f);
                _bName = r.Text("")?.Align(TextAlignmentOptions.Center).FillWidth();
                r.Button(">", () => Run(() => CycleBuilding(1)))?.FixedW(44f);
            })?.Height(32f);
            _bInfo = g.Text("");
            g.Row(r =>
            {
                _rotateBtn = Btn(r, Tx.T("editor.rotate"), () => { M.BOrient = (M.BOrient + 1) % 4; _hoverKey = ""; _uiDirty = true; });
                _mirrorTog = r.Toggle(Tx.T("editor.mirrored"), M.BMirror, v => Changed(() => { M.BMirror = v; _hoverKey = ""; _uiDirty = true; }))?.FillWidth();
            })?.Height(32f);
            Stepper(g, Tx.T("editor.hp"), () => M.BHealth, v => M.BHealth = v, 0, 1000, 5);
            g.Paragraph(Tx.T("editor.bldgPreview"));
        });

        // ---- hills (the game's inner cliffs)
        _hillGroup = c.Column(g =>
        {
            g.Spacing(4f);
            g.Paragraph(Tx.T("editor.hillHelp"));
            var names = new string[EditorModel.HillShapes.Length];
            for (int i = 0; i < names.Length; i++) names[i] = EditorModel.HillShapes[i].Name;
            Pairs(g, names, _hillBtn, i => { M.HillShape = i; _hoverKey = ""; _uiDirty = true; });
            g.Row(r =>
            {
                _hillRotateBtn = Btn(r, Tx.T("editor.rotate"), () => { M.BOrient = (M.BOrient + 1) % 4; _hoverKey = ""; _uiDirty = true; });
                _hillMirrorTog = r.Toggle(Tx.T("editor.mirrored"), M.BMirror, v => Changed(() => { M.BMirror = v; _hoverKey = ""; _uiDirty = true; }))?.FillWidth();
            })?.Height(32f);
            g.Row(r => Btn(r, Tx.T("editor.hillsVisible"), MakeHillsVisible))?.Height(32f);
            g.Paragraph(Tx.T("editor.hillsInfo") +
                        "");
        });
    }

    private static void MakeHillsVisible()
    {
        int added = M.MakeHillsVisible();
        SetStatus(added > 0 ? Tx.T("editor.hillsAdded", added) : Tx.T("editor.hillsNone"));
        AfterEdit(true);
    }

    /// <summary>Buttons in rows of two (a single last one keeps half the width).</summary>
    private static void Pairs(UiContainer parent, string[] names, List<UiButton> store, Action<int> click)
    {
        for (int i = 0; i < names.Length; i += 2)
        {
            int a = i, b = i + 1;
            parent.Row(r =>
            {
                r.Spacing(4f);
                store.Add(Btn(r, names[a], () => click(a)));
                if (b < names.Length) store.Add(Btn(r, names[b], () => click(b)));
                else r.Text("")?.FillWidth();
            })?.Height(32f);
        }
    }

    // ---- redraw ---------------------------------------------------------------------------------------------------------------

    /// <summary>Applies whatever the last event changed: widgets, the map picture, the hover preview, the GPU upload.</summary>
    private static void Flush()
    {
        if (!IsOpen) return;
        try
        {
            if (_uiDirty) RefreshUi();
            if (_texDirty) RenderTexture();
            UpdatePreview();
            if (_viewDirty) Upload();
        }
        catch (Exception e) { Plugin.L.LogError("[editor] redraw: " + e); }
    }

    private static void SetStatus(string s) { if (_status != null) _status.Value = s; }

    private static void AfterEdit(bool redraw)
    {
        _uiDirty = true;
        if (redraw) _texDirty = true;
        _checkLines.Clear();
    }

    private static void Show(UiElement e, bool on)
    {
        var go = e?.Root != null ? e.Root.gameObject : null;
        if (go != null && go.activeSelf != on) go.SetActive(on);
    }

    private static void RefreshUi()
    {
        _uiDirty = false;
        _refreshing = true;
        try
        {
            var table = Buildings.Table;
            if (!ReferenceEquals(M.Table, table))
            {
                M.Table = table;
                if (M.Table.Get(M.BType) == null) foreach (var k in M.Table.ById.Keys) { M.BType = k; break; }
            }
            string title = Tx.T("editor.title") + (M.Dirty ? " *" : "");
            if (title != _titleShown) { _titleShown = title; _win.Title(title); }
            if (_playerLbl != null) _playerLbl.Value = Tx.T("editor.selected", Islands.Selected?.Name ?? Tx.T("editor.pressNext"));

            bool paint = M.Layer == EditLayer.Ground || M.Layer == EditLayer.Nature;
            Show(_paintGroup, paint);
            Show(_buildGroup, M.Layer == EditLayer.Buildings);
            Show(_hillGroup, M.Layer == EditLayer.Hills);
            for (int i = 0; i < _hillBtn.Count; i++) _hillBtn[i]?.Selected(M.HillShape == i);
            Show(_groundGroup, M.Layer == EditLayer.Ground);
            Show(_natureGroup, M.Layer == EditLayer.Nature);
            Show(_sprayGroup, paint && M.Tool == EditTool.Spray);
            for (int i = 0; i < _layerBtn.Count; i++) _layerBtn[i]?.Selected((int)M.Layer == i);
            for (int i = 0; i < _toolBtn.Count; i++) _toolBtn[i]?.Selected((int)M.Tool == i);
            for (int i = 0; i < _groundBtn.Count; i++) _groundBtn[i]?.Selected(M.GroundBrush == i);
            for (int i = 0; i < _natureBtn.Count; i++) _natureBtn[i]?.Selected(M.NatureBrush == i - 1);

            foreach (var refresh in _steppers) refresh();
            _symX?.Set(M.SymX);
            _symY?.Set(M.SymY);
            _fill?.Set(M.FillShapes);
            _yUpTog?.Set(_yUp);

            var info = M.Table.Get(M.BType);
            if (_bName != null) _bName.Value = M.BType + ": " + M.Table.NameOf(M.BType);
            if (_bInfo != null) _bInfo.Value = info == null ? Tx.T("editor.unknownType") : Tx.T("editor.bInfo", info.Tier, info.Hp, info.Shape.Length);
            _rotateBtn?.Text(Tx.T("editor.rotateN", ((M.BOrient % 4) + 4) % 4));
            _mirrorTog?.Set(M.BMirror);
            _hillRotateBtn?.Text(Tx.T("editor.rotateN", ((M.BOrient % 4) + 4) % 4));
            _hillMirrorTog?.Set(M.BMirror);

            if (_checks != null) _checks.Value = string.Join("\n", _checkLines);
            Show(_noData, !M.HasData);
            Show(_map, M.HasData);
            if (_hoverTile == null && _hover != null) _hover.Value = M.HasData ? M.Summary() : "";
        }
        finally { _refreshing = false; }
    }

    private static void CycleBuilding(int dir)
    {
        var ids = new List<int>(M.Table.ById.Keys);
        if (ids.Count == 0) return;
        int pos = Math.Max(0, ids.IndexOf(M.BType));
        M.BType = ids[(pos + dir + ids.Count) % ids.Count];
        _hoverKey = ""; _uiDirty = true;
    }

    // ---- map texture ----------------------------------------------------------------------------------------------------------

    internal static readonly Color32[] GroundColors =
    {
        C(0x1f6f8b), C(0x5b6b73), C(0x4a8f3c), C(0xe8d5a8), C(0x8a7a63), C(0x9a9a9a), C(0xb98a4d),
    };
    internal static readonly Color32[] NatureColors = { C(0x1c5e2a), C(0x6b6b6b), C(0x5a3d1f), C(0xc1572b), C(0xd43f7a), C(0x2ea3c7) };

    private static Color32 C(int rgb) => new((byte)((rgb >> 16) & 255), (byte)((rgb >> 8) & 255), (byte)(rgb & 255), 255);

    internal static Color32 BuildingColor(int type)
    {
        float h = (type * 47f) % 360f / 60f, s = 0.7f, l = 0.4f;
        float c = (1f - Mathf.Abs(2f * l - 1f)) * s, x = c * (1f - Mathf.Abs(h % 2f - 1f)), r = 0f, g = 0f, b = 0f;
        if (h < 1f) { r = c; g = x; } else if (h < 2f) { r = x; g = c; } else if (h < 3f) { g = c; b = x; }
        else if (h < 4f) { g = x; b = c; } else if (h < 5f) { r = x; b = c; } else { r = c; b = x; }
        float m = l - c / 2f;
        return new Color32((byte)((r + m) * 255f), (byte)((g + m) * 255f), (byte)((b + m) * 255f), 255);
    }

    private static void RenderTexture()
    {
        _texDirty = false;
        _viewDirty = true;
        _hoverKey = "";                                   // the preview must be drawn again on top of the new picture
        if (_map != null) MapView.Measure(_map);
        MapView.Render(M, _yUp);
    }

    /// <summary>The picture plus the hover preview, uploaded to the GPU.</summary>
    private static void Upload()
    {
        _viewDirty = false;
        if (!MapView.Ready) return;
        IEnumerable<(int X, int Y)> tiles = null;
        Color32 tint = new(255, 255, 255, 255);
        float blend = 0.35f;
        if (M.HasData && _hoverTile is var (x, y))
        {
            if (M.Layer == EditLayer.Buildings)
            {
                var g = M.CurrentBuilding(x, y);
                tiles = M.Footprint(g.Type, x, y, g.Orient, g.Mirror);
                tint = new Color32(255, 255, 0, 255);
                blend = 0.5f;
            }
            else if (M.Layer == EditLayer.Hills)
            {
                int hit = M.HitCliff(x, y);
                tiles = hit >= 0 ? M.CliffFootprint(M.Data.Cliffs[hit]) : M.CliffFootprint(M.CurrentHill(x, y));
                tint = hit >= 0 ? new Color32(255, 80, 60, 255) : new Color32(255, 200, 120, 255);
                blend = 0.5f;
            }
            else tiles = new[] { (x, y) };
        }
        MapView.Upload(tiles, tint, blend, M.HasData ? M.Side : 0, _yUp, _map);
    }

    // ---- pointer on the map ---------------------------------------------------------------------------------------------------

    private static (int X, int Y) TileAt(Vector2 at, bool clamp)
    {
        MapView.TileAt(at, _yUp, clamp, out int x, out int y);
        return (x, y);
    }

    /// <summary>
    /// Left button paints / places. Right or middle button dragged moves the view; a short right press without moving is the right-click
    /// (pick the brush / remove a building). The wheel zooms (see Open).
    /// </summary>
    private static void OnPointer(UiPointerEvent e)
    {
        if (!M.HasData) return;
        _pointerAt = e.Position;
        switch (e.Kind)
        {
            case UiPointerKind.Down:
            {
                if (e.Button == 1 || e.Button == 2) { _dragButton = e.Button; _dragMoved = 0f; return; }
                if (e.Button != 0) return;
                var t = TileAt(e.Position, false);
                if (!M.InBounds(t.X, t.Y)) return;
                M.PointerDown(t.X, t.Y); _painting = M.Layer != EditLayer.Hills; _checkLines.Clear();
                if (M.Layer == EditLayer.Hills && M.HillMessage != null) SetStatus(M.HillMessage);
                _texDirty = true; _uiDirty = true;
                break;
            }
            case UiPointerKind.Drag:
                if (_dragButton >= 0 && e.Button == _dragButton)
                {
                    _dragMoved += e.Delta.magnitude;
                    if (_dragMoved < 4f) return;
                    MapView.Pan(e.Delta);
                    _texDirty = true;
                    break;
                }
                if (_painting) { var t = TileAt(e.Position, true); M.PointerDrag(t.X, t.Y); _texDirty = true; }
                SetHover(TileAt(e.Position, true));
                break;
            case UiPointerKind.Up:
                if (_dragButton >= 0 && e.Button == _dragButton)
                {
                    bool click = _dragButton == 1 && _dragMoved < 4f;
                    _dragButton = -1;
                    if (!click) return;
                    var t = TileAt(e.Position, false);
                    if (!M.InBounds(t.X, t.Y)) return;
                    M.RightClick(t.X, t.Y);
                    _texDirty = true; _uiDirty = true;
                    break;
                }
                if (_painting) { M.PointerUp(); _painting = false; _uiDirty = true; }
                break;
            case UiPointerKind.Move:
            case UiPointerKind.Enter:
                SetHover(TileAt(e.Position, false));
                break;
            case UiPointerKind.Exit:
                if (!_painting) SetHover(null);
                break;
            default:
                return;
        }
        Flush();
    }

    private static void SetHover((int X, int Y)? tile)
    {
        if (tile != null && !M.InBounds(tile.Value.X, tile.Value.Y)) tile = null;
        _hoverTile = tile;
    }

    /// <summary>Redraws the preview when the hovered tile, or anything the preview depends on, changed.</summary>
    private static void UpdatePreview()
    {
        var tile = _hoverTile;
        string key = tile == null ? "" : $"{tile.Value.X},{tile.Value.Y}|{M.Layer}|{M.BType}|{M.HillShape}|{M.BOrient}|{M.BMirror}|{_yUp}|{(M.HasData ? M.Data.Buildings.Count : 0)}";
        if (key == _hoverKey) return;
        _hoverKey = key;
        _viewDirty = true;
        if (_hover != null) _hover.Value = tile == null ? (M.HasData ? M.Summary() : "") : DescribeTile(tile.Value.X, tile.Value.Y);
    }

    private static string DescribeTile(int x, int y)
    {
        if (!M.InBounds(x, y)) return "";
        int g = M.Data.Ground[M.Cell(x, y)], n = M.Data.Nature[M.Cell(x, y)];
        var sb = new StringBuilder(Tx.T("tile.info", x, y, (g >= 0 && g < EditorModel.GroundNames.Length ? EditorModel.GroundNames[g] : "#" + g)));
        if (n != -1) sb.Append(", ").Append(n >= 0 && n < EditorModel.NatureNames.Length ? EditorModel.NatureNames[n] : "#" + n);
        if (g == 4)
        {
            int hi = M.HitCliff(x, y);
            if (hi < 0) sb.Append(" (" + Tx.T("tile.invisible") + ")");
            else
            {
                var h = M.Data.Cliffs[hi];
                string shape = Tx.T("tile.hillPiece");
                foreach (var s in EditorModel.HillShapes) if (s.W == h.W && s.H == h.H && s.Kind == h.Kind) shape = s.Name;
                sb.Append(Tx.T("tile.hill", shape, h.X, h.Y, h.Orient, h.Mirror != 0 ? Tx.T("tile.mirrored") : ""));
            }
        }
        int bi = M.HitBuilding(x, y);
        if (bi >= 0)
        {
            var b = M.Data.Buildings[bi];
            sb.Append($", {M.Table.NameOf(b.Type)} (HP {b.Health}, orient {b.Orient}, mirror {b.Mirror})");
        }
        return sb.ToString();
    }

    // ---- actions --------------------------------------------------------------------------------------------------------------

    private static void RunCheck()
    {
        var msgs = M.Check();
        _checkLines.Clear();
        if (msgs.Count == 0) _checkLines.Add(Tx.T("editor.checkOk"));
        else
        {
            var seen = new HashSet<string>();
            foreach (var s in msgs) if (seen.Add(s)) _checkLines.Add(s);
            if (_checkLines.Count > 3) { int more = _checkLines.Count - 3; _checkLines.RemoveRange(3, more); _checkLines.Add(Tx.T("check.more", more)); }
        }
        int problems = msgs.Count - M.Notes;   // notes (a building of a mod the others may lack) are not problems
        SetStatus(problems <= 0 ? Tx.T("editor.checkGood") : Tx.T("editor.checkBad", problems));
        _texDirty = true; _uiDirty = true;
    }

    private static void NewBlank()
    {
        int mine = Islands.SideOf(Islands.Local);
        M.NewBlank(mine > 0 ? mine : M.HasData ? M.Side : 49);
        _checkLines.Clear();
        SetStatus(Tx.T("editor.blank"));
        _texDirty = true; _uiDirty = true;
    }

    /// <summary>Reads the player's island into the editor.</summary>
    internal static void LoadFrom(MatchPlayer player)
    {
        if (player == null) { SetStatus(Tx.T("editor.noPlayer")); Flush(); return; }
        byte[] bytes = Islands.Export(player);
        if (bytes == null || bytes.Length == 0) { SetStatus(Tx.T("editor.noIsland")); Flush(); return; }
        Load(bytes, false, Tx.T("editor.loaded", player.Name));
        Flush();
    }

    private static bool Load(byte[] code, bool dirty, string what)
    {
        try
        {
            var d = IslandCodec.FromCodeBytes(code);
            if (!d.SideIsExact) { SetStatus(Tx.T("editor.badLayout")); return false; }
            M.Load(d);
            M.Dirty = dirty;
            if (d.Cliffs.Count > 0)
            {
                var kinds = new SortedDictionary<string, int>();
                foreach (var c in d.Cliffs) { string k = $"{(c.Kind == 0 ? "main" : "extension")} {c.W}x{c.H}"; kinds[k] = kinds.TryGetValue(k, out int v) ? v + 1 : 1; }
                Plugin.L.LogInfo("[editor] hill pieces: " + string.Join(", ", System.Linq.Enumerable.Select(kinds, p => $"{p.Key} x{p.Value}")));
            }
            _checkLines.Clear();
            SetStatus($"{what}: {M.Summary()}" + (d.TrailingBytes > 0 ? Tx.T("summary.unread", d.TrailingBytes) : "") + ".");
            _texDirty = true; _uiDirty = true;
            return true;
        }
        catch (Exception e) { SetStatus(Tx.T("editor.badCode", e.Message)); return false; }
    }

    private static void ApplyTo(MatchPlayer player)
    {
        if (!M.HasData) { SetStatus(Tx.T("editor.nothingToApply")); return; }
        if (player == null) { SetStatus(Tx.T("editor.noPlayer")); return; }
        int problems = M.Check().Count - M.Notes;
        bool ok = Islands.Import(player, IslandCodec.ToCodeBytes(M.Data), out string message);
        if (ok) M.Dirty = false;
        SetStatus(message + (ok && problems > 0 ? $" {problems} placement problem(s) were present." : ""));
        _uiDirty = true;
    }

    private static void PasteCode()
    {
        var bytes = Clipboard.ReadIslandCode(out string error);
        if (bytes == null) { SetStatus(error); return; }
        Load(bytes, true, Tx.T("editor.pasted"));
    }

    private static void CopyCode()
    {
        if (!M.HasData) { SetStatus(Tx.T("editor.nothingToCopy")); return; }
        string code = Convert.ToBase64String(IslandCodec.ToCodeBytes(M.Data));
        SetStatus(Clipboard.SetText(code) ? Tx.T("editor.copied", code.Length) : Tx.T("clipboard.failed"));
    }
}
