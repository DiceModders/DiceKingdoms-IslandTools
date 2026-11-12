using System;
using System.Collections.Generic;
using DiceModders.UiKit.Custom;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace DiceModders.IslandTools;

/// <summary>
/// The editor's map picture. It is drawn at the screen resolution of the map area (one texel per screen pixel), so nothing is stretched:
/// edges stay sharp and nothing shimmers. Only the part of the island in view is drawn. The mouse wheel zooms around the pointer,
/// dragging with the right or middle button moves the view and "Fit map" shows the whole island again.
/// Drawn only after a change, never every frame.
/// </summary>
internal static class MapView
{
    private const float ZoomStep = 1.25f, MaxTile = 48f;
    private static readonly Color32 Outside = new(22, 28, 38, 255);
    private static readonly Color32 Anchor = new(255, 255, 255, 255);
    private static readonly Color32 Problem = new(255, 40, 40, 255);

    private static Texture2D _tex;
    private static Color32[] _px, _view;     // the picture; the picture plus the hover preview (what is uploaded)
    private static int[] _owner;             // building index per tile, -1 = none
    private static int[] _hill;              // hill piece index per tile, -1 = none
    private static int _w, _h, _side;
    private static float _zoom, _cx, _cy;     // screen pixels per tile; centre of the view in tiles (x right, rows up)
    private static float _ox, _oy;            // pixel position of the picture's bottom-left corner
    private static float _pxPerUnit = 1f;     // screen pixels per canvas unit of the map area
    private static bool _fit = true;

    internal static bool Ready => _px != null;
    internal static float TilePixels => _zoom;

    internal static void Fit() => _fit = true;

    internal static void Release()
    {
        if (_tex != null) UnityEngine.Object.Destroy(_tex);
        _tex = null; _px = _view = null; _w = _h = 0;
    }

    /// <summary>Matches the picture to the map area's size in screen pixels. True when it changed and the map must be drawn again.</summary>
    internal static bool Measure(UiImage map)
    {
        if (map?.Root == null) return false;
        var size = map.Size;
        var canvas = map.Root.GetComponentInParent<Canvas>();
        float scale = canvas != null && canvas.rootCanvas != null ? canvas.rootCanvas.scaleFactor : 1f;
        int w = Mathf.RoundToInt(size.x * scale), h = Mathf.RoundToInt(size.y * scale);
        if (w < 16 || h < 16) return false;
        _pxPerUnit = size.x > 0.01f ? w / size.x : 1f;
        if (w == _w && h == _h && _tex != null) return false;

        _w = w; _h = h;
        if (_tex != null) UnityEngine.Object.Destroy(_tex);
        _tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        _px = new Color32[w * h];
        _view = new Color32[w * h];
        map.SetTexture(_tex);
        return true;
    }

    // ---- view -----------------------------------------------------------------------------------------------------------------

    private static void Place(int side)
    {
        if (side != _side) { _side = side; _fit = true; }
        float fit = Mathf.Min(_w, _h) / (float)side;
        if (_fit) { _zoom = fit; _cx = _cy = side * 0.5f; }
        _zoom = Mathf.Clamp(_zoom, fit, Mathf.Max(fit, MaxTile));
        _cx = Keep(_cx, _w, side);
        _cy = Keep(_cy, _h, side);
        _ox = _w * 0.5f - _cx * _zoom;
        _oy = _h * 0.5f - _cy * _zoom;
    }

    /// <summary>The view centre, kept so the island never scrolls out of the area (centred when it is smaller than the area).</summary>
    private static float Keep(float c, int pixels, int side)
    {
        float half = pixels * 0.5f / _zoom;
        return side <= 2f * half ? side * 0.5f : Mathf.Clamp(c, half, side - half);
    }

    /// <summary>Wheel: zooms in or out around the pointer (position in canvas units inside the map area).</summary>
    internal static void Zoom(float notches, Vector2 at)
    {
        if (_side <= 0 || notches == 0f) return;
        float px = at.x * _pxPerUnit, py = at.y * _pxPerUnit;
        float tx = (px - _ox) / _zoom, ty = (py - _oy) / _zoom;           // the tile point under the pointer stays there
        float fit = Mathf.Min(_w, _h) / (float)_side;
        float zoom = Mathf.Clamp(_zoom * Mathf.Pow(ZoomStep, notches), fit, Mathf.Max(fit, MaxTile));
        if (Mathf.Approximately(zoom, _zoom)) return;
        _fit = false;
        _zoom = zoom;
        _cx = (_w * 0.5f - (px - tx * zoom)) / zoom;
        _cy = (_h * 0.5f - (py - ty * zoom)) / zoom;
    }

    /// <summary>Drag with the right or middle button: moves the view with the pointer (delta in canvas units).</summary>
    internal static void Pan(Vector2 delta)
    {
        if (_zoom <= 0f) return;
        _fit = false;
        _cx -= delta.x * _pxPerUnit / _zoom;
        _cy -= delta.y * _pxPerUnit / _zoom;
    }

    /// <summary>The tile under a position in canvas units inside the map area. Rows count from the bottom of the picture.</summary>
    internal static bool TileAt(Vector2 at, bool yUp, bool clamp, out int x, out int y)
    {
        x = y = -1;
        if (_side <= 0 || _zoom <= 0f) return false;
        x = Mathf.FloorToInt((at.x * _pxPerUnit - _ox) / _zoom);
        int row = Mathf.FloorToInt((at.y * _pxPerUnit - _oy) / _zoom);
        if (clamp) { x = Mathf.Clamp(x, 0, _side - 1); row = Mathf.Clamp(row, 0, _side - 1); }
        y = yUp ? row : _side - 1 - row;
        return x >= 0 && x < _side && row >= 0 && row < _side;
    }

    // ---- drawing --------------------------------------------------------------------------------------------------------------

    private static int X(int tile) => Mathf.Clamp(Mathf.FloorToInt(_ox + tile * _zoom), 0, _w);
    private static int Y(int row) => Mathf.Clamp(Mathf.FloorToInt(_oy + row * _zoom), 0, _h);

    /// <summary>Draws the island part in view into the picture.</summary>
    internal static void Render(EditorModel m, bool yUp)
    {
        if (_px == null) return;
        Array.Fill(_px, Outside);
        if (!m.HasData) return;

        var d = m.Data;
        int side = m.Side;
        Place(side);

        if (_owner == null || _owner.Length != side * side) _owner = new int[side * side];
        Array.Fill(_owner, -1);
        for (int i = 0; i < d.Buildings.Count; i++)
            foreach (var (fx, fy) in m.Footprint(d.Buildings[i]))
                if (m.InBounds(fx, fy)) _owner[fy * side + fx] = i;
        if (_hill == null || _hill.Length != side * side) _hill = new int[side * side];
        Array.Fill(_hill, -1);
        for (int i = 0; i < d.Cliffs.Count; i++)
            foreach (var (fx, fy) in m.CliffFootprint(d.Cliffs[i]))
                if (m.InBounds(fx, fy)) _hill[fy * side + fx] = i;

        int gx0 = Math.Max(0, Mathf.FloorToInt(-_ox / _zoom)), gx1 = Math.Min(side, Mathf.CeilToInt((_w - _ox) / _zoom));
        int r0 = Math.Max(0, Mathf.FloorToInt(-_oy / _zoom)), r1 = Math.Min(side, Mathf.CeilToInt((_h - _oy) / _zoom));

        for (int row = r0; row < r1; row++)
        {
            int y0 = Y(row), y1 = Y(row + 1);
            if (y1 <= y0) continue;
            int gy = yUp ? row : side - 1 - row;
            for (int gx = gx0; gx < gx1; gx++)
            {
                int x0 = X(gx), x1 = X(gx + 1);
                if (x1 <= x0) continue;
                int t = Math.Min(x1 - x0, y1 - y0);
                int cell = gy * side + gx;

                int g = d.Ground[cell];
                var ground = g >= 0 && g < EditorWindow.GroundColors.Length ? EditorWindow.GroundColors[g] : new Color32(255, 0, 255, 255);
                Fill(_px, x0, y0, x1, y1, ground);
                if (t >= 6)     // a fine grid: darker left and bottom line (fainter on water)
                {
                    var line = Shade(ground, g == 0 ? 0.93f : 0.84f);
                    Fill(_px, x0, y0, x0 + 1, y1, line);
                    Fill(_px, x0, y0, x1, y0 + 1, line);
                }

                if (g == 4) DrawHillTile(cell, gx, row, x0, y0, x1, y1, t, side, yUp, ground);

                int b = _owner[cell];
                if (b >= 0)
                {
                    DrawBuildingTile(d.Buildings[b], b, gx, row, x0, y0, x1, y1, t, side, yUp);
                    continue;
                }

                int n = d.Nature[cell];
                if (n == -1) continue;
                var col = n >= 0 && n < EditorWindow.NatureColors.Length ? EditorWindow.NatureColors[n] : new Color32(255, 0, 255, 255);
                if (t >= 5) Disc(_px, x0, y0, x1, y1, col, t >= 10 ? Shade(col, 0.7f) : col);
                else Fill(_px, x0 + t / 4, y0 + t / 4, x1 - t / 4, y1 - t / 4, col);
            }
        }

        foreach (var (px, py) in m.Problems)
        {
            if (!m.InBounds(px, py)) continue;
            int row = yUp ? py : side - 1 - py;
            int x0 = X(px), x1 = X(px + 1), y0 = Y(row), y1 = Y(row + 1);
            if (x1 <= x0 || y1 <= y0) continue;
            Frame(_px, x0, y0, x1, y1, Math.Max(1, (x1 - x0) / 8), Problem);
        }
    }

    /// <summary>A building is one solid shape over its whole footprint, outlined where its neighbour tile is not part of it.</summary>
    private static void DrawBuildingTile(IslandBuilding bd, int index, int gx, int row, int x0, int y0, int x1, int y1, int t, int side, bool yUp)
    {
        var col = EditorWindow.BuildingColor(bd.Type);
        Fill(_px, x0, y0, x1, y1, col);
        var edge = Shade(col, 0.55f);
        int bw = Math.Max(1, t / 8);
        if (OwnerAt(gx - 1, row, side, yUp) != index) Fill(_px, x0, y0, x0 + bw, y1, edge);
        if (OwnerAt(gx + 1, row, side, yUp) != index) Fill(_px, x1 - bw, y0, x1, y1, edge);
        if (OwnerAt(gx, row - 1, side, yUp) != index) Fill(_px, x0, y0, x1, y0 + bw, edge);
        if (OwnerAt(gx, row + 1, side, yUp) != index) Fill(_px, x0, y1 - bw, x1, y1, edge);

        int gy = yUp ? row : side - 1 - row;
        if (bd.X == gx && bd.Y == gy)
        {
            int dot = Math.Max(2, t / 4), cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
            Fill(_px, cx - dot / 2, cy - dot / 2, cx - dot / 2 + dot, cy - dot / 2 + dot, Anchor);
        }
    }

    /// <summary>
    /// A hill tile. With a piece (shown in the game): one raised shape per piece, a light top edge and a dark outline where the piece ends.
    /// Without one (painted ground only: invisible in the game, still blocks building): diagonal stripes.
    /// </summary>
    private static void DrawHillTile(int cell, int gx, int row, int x0, int y0, int x1, int y1, int t, int side, bool yUp, Color32 ground)
    {
        int piece = _hill[cell];
        if (piece < 0)
        {
            if (t < 4) return;
            var stripe = Shade(ground, 0.72f);
            for (int y = Math.Max(y0, 0); y < Math.Min(y1, _h); y++)
                for (int x = Math.Max(x0, 0); x < Math.Min(x1, _w); x++)
                    if (((x + y) % 6) < 2) _px[y * _w + x] = stripe;
            return;
        }
        Fill(_px, x0, y0, x1, y1, Shade(ground, 1.12f));
        var edge = Shade(ground, 0.5f);
        int bw = Math.Max(1, t / 8);
        if (HillAt(gx - 1, row, side, yUp) != piece) Fill(_px, x0, y0, x0 + bw, y1, edge);
        if (HillAt(gx + 1, row, side, yUp) != piece) Fill(_px, x1 - bw, y0, x1, y1, edge);
        if (HillAt(gx, row - 1, side, yUp) != piece) Fill(_px, x0, y0, x1, y0 + bw, edge);
        if (HillAt(gx, row + 1, side, yUp) != piece) Fill(_px, x0, y1 - bw, x1, y1, edge);
    }

    private static int HillAt(int gx, int row, int side, bool yUp)
    {
        if (gx < 0 || gx >= side || row < 0 || row >= side) return -1;
        int gy = yUp ? row : side - 1 - row;
        return _hill[gy * side + gx];
    }

    private static int OwnerAt(int gx, int row, int side, bool yUp)
    {
        if (gx < 0 || gx >= side || row < 0 || row >= side) return -1;
        int gy = yUp ? row : side - 1 - row;
        return _owner[gy * side + gx];
    }

    /// <summary>Copies the picture, adds the hover preview (tiles blended with a colour) and uploads it to the GPU.</summary>
    internal static void Upload(IEnumerable<(int X, int Y)> hover, Color32 tint, float blend, int side, bool yUp, UiImage map)
    {
        if (_px == null || _tex == null) return;
        Array.Copy(_px, _view, _px.Length);
        if (hover != null)
            foreach (var (hx, hy) in hover)
            {
                if (hx < 0 || hy < 0 || hx >= side || hy >= side) continue;
                int row = yUp ? hy : side - 1 - hy;
                int x0 = X(hx), x1 = X(hx + 1), y0 = Y(row), y1 = Y(row + 1);
                Blend(_view, x0, y0, x1, y1, tint, blend);
            }

        try
        {
            unsafe { fixed (Color32* p = _view) _tex.LoadRawTextureData((IntPtr)p, _view.Length * 4); }
            _tex.Apply(false);
        }
        catch
        {
            _tex.SetPixels32(new Il2CppStructArray<Color32>(_view));
            map?.Refresh();
        }
    }

    // ---- pixel helpers --------------------------------------------------------------------------------------------------------

    private static Color32 Shade(Color32 c, float k) => new((byte)Math.Min(255f, c.r * k), (byte)Math.Min(255f, c.g * k), (byte)Math.Min(255f, c.b * k), 255);

    private static void Fill(Color32[] target, int x0, int y0, int x1, int y1, Color32 col)
    {
        x0 = Math.Max(x0, 0); y0 = Math.Max(y0, 0); x1 = Math.Min(x1, _w); y1 = Math.Min(y1, _h);
        for (int y = y0; y < y1; y++)
            Array.Fill(target, col, y * _w + x0, Math.Max(0, x1 - x0));
    }

    private static void Frame(Color32[] target, int x0, int y0, int x1, int y1, int bw, Color32 col)
    {
        Fill(target, x0, y0, x1, y0 + bw, col);
        Fill(target, x0, y1 - bw, x1, y1, col);
        Fill(target, x0, y0, x0 + bw, y1, col);
        Fill(target, x1 - bw, y0, x1, y1, col);
    }

    private static void Blend(Color32[] target, int x0, int y0, int x1, int y1, Color32 col, float k)
    {
        x0 = Math.Max(x0, 0); y0 = Math.Max(y0, 0); x1 = Math.Min(x1, _w); y1 = Math.Min(y1, _h);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int i = y * _w + x;
                var o = target[i];
                target[i] = new Color32((byte)(o.r + (col.r - o.r) * k), (byte)(o.g + (col.g - o.g) * k), (byte)(o.b + (col.b - o.b) * k), 255);
            }
    }

    /// <summary>A round blob (trees, rocks, ...) inside a tile, with a darker rim on big tiles.</summary>
    private static void Disc(Color32[] target, int x0, int y0, int x1, int y1, Color32 col, Color32 rim)
    {
        float cx = (x0 + x1) * 0.5f, cy = (y0 + y1) * 0.5f, r = Math.Min(x1 - x0, y1 - y0) * 0.36f;
        float r2 = r * r, inner = (r - 1.2f) * (r - 1.2f);
        int ya = Math.Max(0, Mathf.FloorToInt(cy - r)), yb = Math.Min(_h, Mathf.CeilToInt(cy + r));
        int xa = Math.Max(0, Mathf.FloorToInt(cx - r)), xb = Math.Min(_w, Mathf.CeilToInt(cx + r));
        for (int y = ya; y < yb; y++)
        {
            float dy = y + 0.5f - cy;
            for (int x = xa; x < xb; x++)
            {
                float dx = x + 0.5f - cx, q = dx * dx + dy * dy;
                if (q <= r2) target[y * _w + x] = q > inner ? rim : col;
            }
        }
    }
}
