using System;
using System.Collections.Generic;

namespace DiceModders.IslandTools;

// Pure C#: the island editor's tools and undo history (a port of tools/island-editor.html).
// The UI (EditorWindow) only translates mouse positions to tiles and calls these methods.

internal enum EditLayer { Ground, Nature, Buildings, Hills }
internal enum EditTool { Point, Fill, Line, Rect, Square, Circle, Oval, Spray }

internal sealed class EditorModel
{
    public static string[] GroundNames => new[] { Tx.T("ground.0"), Tx.T("ground.1"), Tx.T("ground.2"), Tx.T("ground.3"), Tx.T("ground.4"), Tx.T("ground.5"), Tx.T("ground.6") };
    public static string[] NatureNames => new[] { Tx.T("nature.0"), Tx.T("nature.1"), Tx.T("nature.2"), Tx.T("nature.3"), Tx.T("nature.4"), Tx.T("nature.5") };

    /// <summary>The game's hill pieces (InnerCliff.Key: size and kind 0 = MAIN, 1 = EXTENSION); each has its own 3D models.</summary>
    public static (int W, int H, int Kind, string Name)[] HillShapes => new (int W, int H, int Kind, string Name)[]
    {
        (3, 3, 0, Tx.T("hill.h33")), (2, 2, 0, Tx.T("hill.h22")), (2, 2, 1, Tx.T("hill.s22")), (1, 2, 1, Tx.T("hill.s12")), (1, 1, 1, Tx.T("hill.s11")),
    };
    public int HillShape;
    /// <summary>Why the last hill click did nothing (null when it worked).</summary>
    public string HillMessage;

    public IslandData Data;
    public BuildingTable Table = BuildingTable.Parse(BuildingTable.DefaultDump);

    public EditLayer Layer = EditLayer.Ground;
    public EditTool Tool = EditTool.Point;
    public int GroundBrush = 2;      // Grass
    public int NatureBrush = 0;      // Tree; -1 = erase nature
    public int BrushSize = 1;
    public bool SymX, SymY;
    public bool FillShapes = true;
    public int SprayRadius = 3, SprayDensity = 35;

    // building brush
    public int BType = 0, BOrient = 0;
    public bool BMirror;
    public int BHealth = 0;          // 0 = type default

    public bool Dirty;               // edited since the last load/apply
    public List<(int X, int Y)> Problems = new();

    private readonly Random _rng = new();
    private readonly List<Snapshot> _undo = new(), _redo = new();
    private (int X, int Y)? _dragStart;
    private int[] _baseLayer;

    private sealed class Snapshot
    {
        public int[] Ground, Nature;
        public List<IslandBuilding> Buildings;
        public List<IslandCliff> Cliffs;
    }

    public bool HasData => Data != null && Data.SideIsExact && Data.Side > 0;
    public int Side => Data?.Side ?? 0;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public int Brush
    {
        get => Layer == EditLayer.Nature ? NatureBrush : GroundBrush;
        set { if (Layer == EditLayer.Nature) NatureBrush = value; else GroundBrush = value; }
    }

    private int[] LayerArray => Layer == EditLayer.Nature ? Data.Nature : Data.Ground;
    public int Cell(int x, int y) => y * Side + x;
    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Side && y < Side;

    public void Load(IslandData d)
    {
        Data = d; _undo.Clear(); _redo.Clear(); _dragStart = null; _baseLayer = null;
        Problems.Clear(); Dirty = false;
    }

    public void NewBlank(int side) { Load(IslandData.Blank(side)); }

    // ---- undo / redo ---------------------------------------------------------------------------------------------

    private Snapshot Snap() => new Snapshot
    {
        Ground = (int[])Data.Ground.Clone(), Nature = (int[])Data.Nature.Clone(),
        Buildings = Data.Buildings.ConvertAll(b => b.Clone()),
        Cliffs = Data.Cliffs.ConvertAll(c => c.Clone()),
    };

    public void PushUndo()
    {
        _undo.Add(Snap());
        if (_undo.Count > 60) _undo.RemoveAt(0);
        _redo.Clear();
    }

    private void Apply(Snapshot s) { Data.Ground = s.Ground; Data.Nature = s.Nature; Data.Buildings = s.Buildings; Data.Cliffs = s.Cliffs; Dirty = true; Problems.Clear(); }

    public void Undo()
    {
        if (!HasData || _undo.Count == 0) return;
        _redo.Add(Snap());
        var s = _undo[_undo.Count - 1]; _undo.RemoveAt(_undo.Count - 1);
        Apply(s);
    }

    public void Redo()
    {
        if (!HasData || _redo.Count == 0) return;
        _undo.Add(Snap());
        var s = _redo[_redo.Count - 1]; _redo.RemoveAt(_redo.Count - 1);
        Apply(s);
    }

    // ---- painting primitives -------------------------------------------------------------------------------------

    private void Put(int x, int y)
    {
        int s = Side;
        var arr = LayerArray;
        int b = Brush;
        void Set(int px, int py) { if (px >= 0 && py >= 0 && px < s && py < s) arr[py * s + px] = b; }
        Set(x, y);
        if (SymX) Set(s - 1 - x, y);
        if (SymY) Set(x, s - 1 - y);
        if (SymX && SymY) Set(s - 1 - x, s - 1 - y);
    }

    private void SetPixel(int x, int y)
    {
        int r = BrushSize - 1;
        if (r <= 0) { Put(x, y); return; }
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
                if (dx * dx + dy * dy <= r * r + r * 0.5) Put(x + dx, y + dy);
    }

    private void FloodFill(int x0, int y0)
    {
        var arr = LayerArray;
        int target = arr[Cell(x0, y0)];
        if (target == Brush) return;
        var stack = new Stack<(int, int)>();
        var seen = new bool[arr.Length];
        stack.Push((x0, y0));
        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            if (!InBounds(x, y)) continue;
            int idx = Cell(x, y);
            if (seen[idx] || arr[idx] != target) continue;
            seen[idx] = true; arr[idx] = Brush;
            stack.Push((x + 1, y)); stack.Push((x - 1, y)); stack.Push((x, y + 1)); stack.Push((x, y - 1));
        }
    }

    private void DrawLine(int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1, dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, err = dx + dy;
        while (true)
        {
            SetPixel(x0, y0);
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private void DrawRect(int x0, int y0, int x1, int y1)
    {
        int xa = Math.Min(x0, x1), xb = Math.Max(x0, x1), ya = Math.Min(y0, y1), yb = Math.Max(y0, y1);
        if (FillShapes) { for (int y = ya; y <= yb; y++) for (int x = xa; x <= xb; x++) Put(x, y); return; }
        DrawLine(xa, ya, xb, ya); DrawLine(xb, ya, xb, yb); DrawLine(xb, yb, xa, yb); DrawLine(xa, yb, xa, ya);
    }

    private void DrawSquare(int x0, int y0, int x1, int y1)
    {
        int s = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
        DrawRect(x0, y0, x0 + (x1 >= x0 ? s : -s), y0 + (y1 >= y0 ? s : -s));
    }

    // ellipse inscribed in the tile box (integer cells only)
    private void DrawBoxEllipse(int xa, int ya, int xb, int yb)
    {
        if (xa > xb) (xa, xb) = (xb, xa);
        if (ya > yb) (ya, yb) = (yb, ya);
        double cx = (xa + xb) / 2.0, cy = (ya + yb) / 2.0, rx = (xb - xa + 1) / 2.0, ry = (yb - ya + 1) / 2.0;
        bool Inside(int x, int y) => Math.Pow((x - cx) / rx, 2) + Math.Pow((y - cy) / ry, 2) <= 1;
        // only the tiles on the grid (a circle dragged far out can be much bigger than the island)
        for (int y = Math.Max(ya, 0); y <= Math.Min(yb, Side - 1); y++)
            for (int x = Math.Max(xa, 0); x <= Math.Min(xb, Side - 1); x++)
            {
                if (!Inside(x, y)) continue;
                if (FillShapes) { Put(x, y); continue; }
                bool edge = false;
                for (int k = 1; k <= BrushSize && !edge; k++)
                    edge = !Inside(x + k, y) || !Inside(x - k, y) || !Inside(x, y + k) || !Inside(x, y - k);
                if (edge) Put(x, y);
            }
    }

    private void DrawCircle(int cx, int cy, int x1, int y1)
    {
        int r = (int)Math.Round(Math.Sqrt((double)(x1 - cx) * (x1 - cx) + (double)(y1 - cy) * (y1 - cy)));
        DrawBoxEllipse(cx - r, cy - r, cx + r, cy + r);
    }

    private void Spray(int cx, int cy)
    {
        int r = SprayRadius;
        double density = SprayDensity / 100.0;
        for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
                if (x * x + y * y <= r * r && _rng.NextDouble() < density) SetPixel(cx + x, cy + y);
    }

    // ---- ground-layer morphology -----------------------------------------------------------------------------------

    private static bool IsLand(int g) => g != 0 && g != 1;

    private bool Neigh4(int x, int y, Func<int, bool> f)
    {
        foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            int a = x + dx, b = y + dy;
            if (InBounds(a, b) && f(Data.Ground[Cell(a, b)])) return true;
        }
        return false;
    }

    /// <summary>mode: "outline" (draw the coast), "grow" or "shrink" the land by one tile.</summary>
    public void Morph(string mode)
    {
        if (!HasData) return;
        PushUndo();
        var g = Data.Ground;
        var o = (int[])g.Clone();
        bool ground = Layer == EditLayer.Ground;
        for (int y = 0; y < Side; y++)
            for (int x = 0; x < Side; x++)
            {
                bool land = IsLand(g[Cell(x, y)]);
                if (mode == "outline" && land && Neigh4(x, y, v => !IsLand(v))) o[Cell(x, y)] = ground ? GroundBrush : 3;
                if (mode == "grow" && !land && Neigh4(x, y, IsLand)) o[Cell(x, y)] = ground ? GroundBrush : 2;
                if (mode == "shrink" && land && Neigh4(x, y, v => !IsLand(v))) o[Cell(x, y)] = 0;
            }
        Data.Ground = o; Dirty = true;
    }

    // ---- pointer handling (tile coordinates, y = grid y) ---------------------------------------------------------------

    public bool StrokeActive => _dragStart != null || _strokeOpen;
    private bool _strokeOpen;

    /// <summary>Left mouse button pressed on tile (x,y).</summary>
    public void PointerDown(int x, int y)
    {
        if (!HasData || !InBounds(x, y)) return;
        if (Layer == EditLayer.Hills) { ClickHill(x, y); return; }
        PushUndo();
        Dirty = true;
        _strokeOpen = true;
        if (Layer == EditLayer.Buildings)
        {
            int idx = HitBuilding(x, y);
            if (idx >= 0) Data.Buildings.RemoveAt(idx);
            else
            {
                var g = CurrentBuilding(x, y);
                Data.Buildings.Add(new IslandBuilding { Type = g.Type, X = x, Y = y, Orient = g.Orient, Mirror = g.Mirror, Health = g.Health, Turn = 0 });
            }
            _strokeOpen = false;
            return;
        }
        switch (Tool)
        {
            case EditTool.Point: SetPixel(x, y); break;
            case EditTool.Fill: FloodFill(x, y); _strokeOpen = false; break;
            case EditTool.Spray: Spray(x, y); break;
            default: _dragStart = (x, y); _baseLayer = (int[])LayerArray.Clone(); break;
        }
    }

    /// <summary>Mouse moved with the left button held.</summary>
    public void PointerDrag(int x, int y)
    {
        if (!HasData || !InBounds(x, y) || Layer == EditLayer.Buildings || Layer == EditLayer.Hills) return;
        if (Tool == EditTool.Point && _strokeOpen) { SetPixel(x, y); return; }
        if (Tool == EditTool.Spray && _strokeOpen) { Spray(x, y); return; }
        if (_dragStart == null) return;
        Array.Copy(_baseLayer, LayerArray, _baseLayer.Length);
        var (sx, sy) = _dragStart.Value;
        switch (Tool)
        {
            case EditTool.Line: DrawLine(sx, sy, x, y); break;
            case EditTool.Rect: DrawRect(sx, sy, x, y); break;
            case EditTool.Square: DrawSquare(sx, sy, x, y); break;
            case EditTool.Circle: DrawCircle(sx, sy, x, y); break;
            case EditTool.Oval: DrawBoxEllipse(sx, sy, x, y); break;
        }
    }

    public void PointerUp() { _dragStart = null; _baseLayer = null; _strokeOpen = false; }

    /// <summary>Right mouse button: remove the building on the tile, or pick the tile's brush.</summary>
    public void RightClick(int x, int y)
    {
        if (!HasData || !InBounds(x, y)) return;
        if (Layer == EditLayer.Buildings)
        {
            int i = HitBuilding(x, y);
            if (i >= 0) { PushUndo(); Data.Buildings.RemoveAt(i); Dirty = true; }
        }
        else if (Layer == EditLayer.Hills)
        {
            int i = HitCliff(x, y);
            if (i >= 0) { PushUndo(); RemoveCliff(i); }
        }
        else Brush = LayerArray[Cell(x, y)];
    }

    // ---- hills (inner cliffs) -------------------------------------------------------------------------------------------

    /// <summary>
    /// The tiles of a hill piece: the box between its corner tiles (0,0) and (W-1,H-1) after the piece's rotation and mirroring - the same as the
    /// game's InnerCliff.Positions. The game draws the 3D hill from the cliff entry; the tiles below it must be ground INNER_CLIFF (4).
    /// </summary>
    public IEnumerable<(int X, int Y)> CliffFootprint(int w, int h, int x, int y, int orient, int mirror)
    {
        var (c, s) = CosSin(orient);
        (int X, int Y) T(int dx, int dy) { int nx = dx * c - dy * s, ny = dx * s + dy * c; if (mirror != 0) nx = -nx; return (x + nx, y + ny); }
        var a = T(0, 0);
        var b = T(w - 1, h - 1);
        for (int yy = Math.Min(a.Y, b.Y); yy <= Math.Max(a.Y, b.Y); yy++)
            for (int xx = Math.Min(a.X, b.X); xx <= Math.Max(a.X, b.X); xx++)
                yield return (xx, yy);
    }

    public IEnumerable<(int X, int Y)> CliffFootprint(IslandCliff c) => CliffFootprint(c.W, c.H, c.X, c.Y, c.Orient, c.Mirror);

    /// <summary>The hill piece being placed at (x, y) with the current shape, rotation and mirroring.</summary>
    public IslandCliff CurrentHill(int x, int y)
    {
        var shape = HillShapes[Math.Clamp(HillShape, 0, HillShapes.Length - 1)];
        return new IslandCliff { Kind = shape.Kind, W = shape.W, H = shape.H, X = x, Y = y, Orient = ((BOrient % 4) + 4) % 4, Mirror = BMirror ? 1 : 0 };
    }

    public int HitCliff(int x, int y)
    {
        for (int i = 0; i < Data.Cliffs.Count; i++)
            foreach (var (fx, fy) in CliffFootprint(Data.Cliffs[i]))
                if (fx == x && fy == y) return i;
        return -1;
    }

    /// <summary>A click with the Hills layer: removes the hill piece under the pointer, or places the current one when all its tiles are free land.</summary>
    private void ClickHill(int x, int y)
    {
        HillMessage = null;
        int hit = HitCliff(x, y);
        if (hit >= 0) { PushUndo(); RemoveCliff(hit); return; }

        var hill = CurrentHill(x, y);
        foreach (var (fx, fy) in CliffFootprint(hill))
        {
            if (!InBounds(fx, fy)) { HillMessage = Tx.T("hill.noFit"); return; }
            int g = Data.Ground[Cell(fx, fy)];
            if (g == 0 || g == 1 || g == 6) { HillMessage = Tx.T("hill.needLand", fx, fy, GroundNames[g].ToLowerInvariant()); return; }
            if (HitBuilding(fx, fy) >= 0) { HillMessage = Tx.T("hill.building", fx, fy); return; }
            // the game checks nothing: two pieces on one tile would stand inside each other
            if (HitCliff(fx, fy) >= 0) { HillMessage = Tx.T("hill.covered", fx, fy); return; }
        }
        PushUndo();
        Data.Cliffs.Add(hill);
        foreach (var (fx, fy) in CliffFootprint(hill))
        {
            Data.Ground[Cell(fx, fy)] = 4;     // INNER_CLIFF: blocks building like the game's own hills
            Data.Nature[Cell(fx, fy)] = -1;
        }
        Dirty = true;
    }

    /// <summary>
    /// Covers every hill tile without a piece (ground INNER_CLIFF painted with the brush: an invisible hill) with the game's pieces, the way the
    /// game builds its own hills: a big 3x3 where it fits, then 2x2, then 1x2 either way, single tiles last. Returns the number of pieces added.
    /// </summary>
    public int MakeHillsVisible()
    {
        if (!HasData) return 0;
        int side = Side;
        var open = UncoveredHillTiles(out int count);
        if (count == 0) return 0;

        // (shape, orientation); the anchor is moved so the piece's lowest corner lands on the tile being filled
        var options = new List<(int Shape, int Orient, int Dx, int Dy)>();
        foreach (var (shape, orient) in new[] { (0, 0), (1, 0), (3, 0), (3, 1), (4, 0) })
        {
            var s = HillShapes[shape];
            int minX = int.MaxValue, minY = int.MaxValue;
            foreach (var (fx, fy) in CliffFootprint(s.W, s.H, 0, 0, orient, 0)) { minX = Math.Min(minX, fx); minY = Math.Min(minY, fy); }
            options.Add((shape, orient, -minX, -minY));
        }

        PushUndo();
        int added = 0;
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                if (!open[Cell(x, y)]) continue;
                foreach (var (shape, orient, dx, dy) in options)
                {
                    var s = HillShapes[shape];
                    var piece = new IslandCliff { Kind = s.Kind, W = s.W, H = s.H, X = x + dx, Y = y + dy, Orient = orient, Mirror = 0 };
                    bool fits = true;
                    foreach (var (fx, fy) in CliffFootprint(piece))
                        if (!InBounds(fx, fy) || !open[Cell(fx, fy)]) { fits = false; break; }
                    if (!fits) continue;
                    foreach (var (fx, fy) in CliffFootprint(piece)) { open[Cell(fx, fy)] = false; Data.Nature[Cell(fx, fy)] = -1; }
                    Data.Cliffs.Add(piece);
                    added++;
                    break;
                }
            }
        Dirty = true;
        return added;
    }

    /// <summary>Hill tiles (ground INNER_CLIFF) that no piece covers: invisible in the game, but they still block building.</summary>
    public int InvisibleHillTiles()
    {
        if (!HasData) return 0;
        UncoveredHillTiles(out int n);
        return n;
    }

    /// <summary>Per tile: hill ground that no piece covers. One pass over the pieces, then one over the tiles (not every tile against every piece).</summary>
    private bool[] UncoveredHillTiles(out int count)
    {
        var open = new bool[Side * Side];
        for (int i = 0; i < open.Length; i++) open[i] = Data.Ground[i] == 4;
        foreach (var c in Data.Cliffs)
            foreach (var (fx, fy) in CliffFootprint(c))
                if (InBounds(fx, fy)) open[Cell(fx, fy)] = false;
        count = 0;
        for (int i = 0; i < open.Length; i++) if (open[i]) count++;
        return open;
    }

    /// <summary>
    /// Removes a hill piece; its tiles become grass again unless another piece still covers them. (The game's RemoveInnerCliff turns every tile of
    /// the piece to grass; here a tile another piece still covers stays hill ground, which is what the island should look like.)
    /// </summary>
    private void RemoveCliff(int index)
    {
        var gone = Data.Cliffs[index];
        Data.Cliffs.RemoveAt(index);
        foreach (var (fx, fy) in CliffFootprint(gone))
            if (InBounds(fx, fy) && Data.Ground[Cell(fx, fy)] == 4 && HitCliff(fx, fy) < 0) Data.Ground[Cell(fx, fy)] = 2;
        Dirty = true;
    }

    // ---- buildings --------------------------------------------------------------------------------------------------

    public (int Type, int Orient, int Mirror, int Health) CurrentBuilding(int x, int y)
    {
        int hp = BHealth > 0 ? BHealth : (Table.Get(BType)?.Hp is int h && h > 0 ? h : 100);
        return (BType, ((BOrient % 4) + 4) % 4, BMirror ? 1 : 0, hp);
    }

    private static (int C, int S) CosSin(int orient)
    {
        switch (((orient % 4) + 4) % 4) { case 0: return (1, 0); case 1: return (0, 1); case 2: return (-1, 0); default: return (0, -1); }
    }

    public IEnumerable<(int X, int Y)> Footprint(int type, int x, int y, int orient, int mirror)
    {
        var shape = Table.Get(type)?.Shape;
        if (shape == null || shape.Length == 0) { yield return (x, y); yield break; }
        var (c, s) = CosSin(orient);
        foreach (var d in shape)
        {
            int nx = d[0] * c - d[1] * s, ny = d[0] * s + d[1] * c;
            if (mirror != 0) nx = -nx;
            yield return (x + nx, y + ny);
        }
    }

    public IEnumerable<(int X, int Y)> Footprint(IslandBuilding b) => Footprint(b.Type, b.X, b.Y, b.Orient, b.Mirror);

    public int HitBuilding(int x, int y)
    {
        for (int i = 0; i < Data.Buildings.Count; i++)
            foreach (var (fx, fy) in Footprint(Data.Buildings[i]))
                if (fx == x && fy == y) return i;
        return -1;
    }

    /// <summary>Water / nature / overlap / bounds check (same rules as the web editor). Fills Problems; returns messages.</summary>
    public List<string> Check()
    {
        Problems.Clear();
        var msgs = new List<string>();
        if (!HasData) return msgs;
        var occ = new Dictionary<(int, int), int>();
        for (int i = 0; i < Data.Buildings.Count; i++)
        {
            var b = Data.Buildings[i];
            string nm = Table.NameOf(b.Type);
            foreach (var (x, y) in Footprint(b))
            {
                if (!InBounds(x, y)) { Problems.Add((x, y)); msgs.Add(Tx.T("check.outside", nm, b.X, b.Y)); continue; }
                if (Data.Nature[Cell(x, y)] != -1) { Problems.Add((x, y)); msgs.Add(Tx.T("check.nature", nm, b.X, b.Y, x, y)); }
                int g = Data.Ground[Cell(x, y)];
                if ((g == 0 || g == 1) && b.Type != 6 && b.Type != 21 && b.Type != 22) { Problems.Add((x, y)); msgs.Add(Tx.T("check.water", nm, b.X, b.Y, x, y)); }
                if (g == 4) { Problems.Add((x, y)); msgs.Add(Tx.T("check.onHill", nm, b.X, b.Y, x, y)); }
                if (occ.TryGetValue((x, y), out int other) && other != i)
                { Problems.Add((x, y)); msgs.Add(Tx.T("check.overlapsBuilding", nm, b.X, b.Y, Table.NameOf(Data.Buildings[other].Type), x, y)); }
                occ[(x, y)] = i;
            }
        }
        // hill pieces: the game draws them wherever they are, so these show up as hills on water, on grass, or inside each other
        var hillAt = new Dictionary<(int, int), int>();
        for (int i = 0; i < Data.Cliffs.Count; i++)
        {
            var c = Data.Cliffs[i];
            string nm = Tx.T("check.hill", c.W, c.H);
            foreach (var (x, y) in CliffFootprint(c))
            {
                if (!InBounds(x, y)) { Problems.Add((x, y)); msgs.Add(Tx.T("check.outside", nm, c.X, c.Y)); continue; }
                int hg = Data.Ground[Cell(x, y)];
                if (hg != 4) { Problems.Add((x, y)); msgs.Add(Tx.T("check.notHillGround", nm, c.X, c.Y, x, y, (hg >= 0 && hg < GroundNames.Length ? GroundNames[hg].ToLowerInvariant() : "#" + hg))); }
                if (hillAt.TryGetValue((x, y), out int other) && other != i)
                { Problems.Add((x, y)); msgs.Add(Tx.T("check.overlapHill", nm, c.X, c.Y, x, y)); }
                hillAt[(x, y)] = i;
            }
        }
        int invisible = InvisibleHillTiles();
        if (invisible > 0) msgs.Add(Tx.T("check.invisible", invisible));
        return msgs;
    }

    public void ClearNatureUnderBuildings()
    {
        if (!HasData) return;
        PushUndo();
        foreach (var b in Data.Buildings)
            foreach (var (x, y) in Footprint(b))
                if (InBounds(x, y)) Data.Nature[Cell(x, y)] = -1;
        Problems.Clear(); Dirty = true;
    }

    public void SetAllHpToDefault()
    {
        if (!HasData) return;
        PushUndo();
        foreach (var b in Data.Buildings)
            if (Table.Get(b.Type) is BuildingInfo info && info.Hp > 0) b.Health = info.Hp;
        Dirty = true;
    }

    /// <summary>Counts for the status line.</summary>
    public string Summary()
    {
        if (!HasData) return Tx.T("summary.none");
        int land = 0;
        foreach (int g in Data.Ground) if (g != 0 && g != 1) land++;
        return Tx.T("summary", Side, land, Data.Buildings.Count, Data.Cliffs.Count);
    }
}
