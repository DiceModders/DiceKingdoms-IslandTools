using System;
using System.Collections.Generic;
using DiceModders.ModCore;
using DiceModders.UiKit.Custom;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace DiceModders.IslandTools;

using Core = DiceModders.ModCore.ModCore;

/// <summary>
/// The players window: one card per player of the match with a picture of their island, a tick box, and actions for every ticked player at once
/// (apply the editor's island, grow / shrink the coast, clean up, copy the codes, undo with the automatic backups).
/// The pictures are redrawn when the window opens, when a turn phase is synced, after this mod changed an island, and with "Refresh"; an island
/// whose code did not change is not drawn again.
///
/// All changes go through the same import as the editor: changing OTHER players' islands is experimental and only has a chance to work for the host.
/// </summary>
internal static class PlayersWindow
{
    private const int MaxRows = 16;
    private const int Px = 3;                       // texels per tile in the pictures

    private sealed class Row
    {
        public UiContainer Root;
        public UiToggle Pick;
        public UiImage Picture;
        public UiText Info;
        public Texture2D Tex;
        public Color32[] Pixels = Array.Empty<Color32>();
        public MatchPlayer Player;
        public byte[] LastCode;
    }

    private static UiWindow _win;
    private static readonly Row[] Rows = new Row[MaxRows];
    private static UiText _status, _count;
    private static IReadOnlyList<MatchPlayer> _players = Array.Empty<MatchPlayer>();
    private static readonly HashSet<IntPtr> Ticked = new();
    private static readonly Dictionary<IntPtr, byte[]> Backups = new();   // the island code before this window changed it, per player, this match
    private static readonly EditorModel Calc = new();                      // renders footprints and runs the bulk edits
    private static bool _refreshing;

    internal static bool IsOpen => _win != null;

    internal static void Install()
    {
        Core.Match.PhaseSynced += _ => Redraw();
        Core.Match.Started += Redraw;
        Core.Match.Ended += () => { Backups.Clear(); Ticked.Clear(); Redraw(); };
        Islands.Changed += Redraw;
    }

    internal static void Toggle() { if (IsOpen) Close(); else Open(); }

    internal static void Close() => _win?.Close();

    // ---- window --------------------------------------------------------------------------------------------------------------------

    internal static void Open()
    {
        if (IsOpen) { _win.BringToFront(); return; }
        _win = Windows.Ui?.Custom.Window(w =>
        {
            w.Title("Players").Size(780f, 820f).Position(120f, 60f).Draggable().Closable().Fullscreenable().FullscreenButton();
            w.OnClose(Closed);
            var b = w.Body;
            b.Spacing(6f);

            b.Row(r =>
            {
                _count = r.Text("")?.WeightW(1.4f);
                r.Button("All", () => Pick(_ => true))?.FillWidth();
                r.Button("None", () => Pick(_ => false))?.FillWidth();
                r.Button("Only me", () => Pick(Islands.IsLocal))?.FillWidth();
                r.Button("Refresh", Redraw)?.FillWidth();
            })?.Height(34f);

            b.Scroll(list =>
            {
                list.Spacing(6f);
                for (int i = 0; i < MaxRows; i++) Rows[i] = BuildRow(list, i);
            })?.FillWidth().FillHeight();

            b.Text("Actions on the ticked players (a backup of each island is kept; \"Undo\" puts it back):");
            b.Row(r =>
            {
                r.Button("Apply editor island", ApplyEditorIsland)?.FillWidth();
                r.Button("Copy codes", CopyCodes)?.FillWidth();
                r.Button("Undo (backups)", RestoreBackups)?.FillWidth();
            })?.Height(34f);
            b.Row(r =>
            {
                r.Button("Coast: Grow", () => Bulk("Coast grown", m => m.Morph("grow")))?.FillWidth();
                r.Button("Coast: Shrink", () => Bulk("Coast shrunk", m => m.Morph("shrink")))?.FillWidth();
                r.Button("Coast: Outline", () => Bulk("Coast outlined", m => m.Morph("outline")))?.FillWidth();
            })?.Height(34f);
            b.Row(r =>
            {
                r.Button("Clear nature under buildings", () => Bulk("Nature under buildings removed", m => m.ClearNatureUnderBuildings()))?.FillWidth();
                r.Button("All HP default", () => Bulk("Building HP set to default", m => m.SetAllHpToDefault()))?.FillWidth();
            })?.Height(34f);
            _status = b.Paragraph("Tick players, then use an action. Click a picture or \"Edit\" to open that island in the editor.");
        });
        Redraw();
    }

    private static void Closed()
    {
        _win = null;
        _status = _count = null;
        for (int i = 0; i < MaxRows; i++)
        {
            if (Rows[i]?.Tex != null) UnityEngine.Object.Destroy(Rows[i].Tex);
            Rows[i] = null;
        }
    }

    private static Row BuildRow(UiContainer list, int index)
    {
        var row = new Row();
        row.Root = list.Row(r =>
        {
            r.Spacing(10f);
            row.Picture = r.Image()?.Filter(FilterMode.Bilinear).Fixed(116f);
            row.Picture?.OnPointer(e => { if (e.Kind == UiPointerKind.Click && e.Button == 0) Edit(index); });
            r.Column(col =>
            {
                col.Spacing(4f);
                row.Pick = col.Toggle("", false, v =>
                {
                    if (_refreshing || row.Player == null) return;
                    if (v) Ticked.Add(row.Player.Game.Pointer); else Ticked.Remove(row.Player.Game.Pointer);
                });
                row.Info = col.Paragraph("");
                col.Row(bb =>
                {
                    bb.Button("Edit", () => Edit(index))?.FillWidth();
                    bb.Button("Copy code", () => CopyOne(index))?.FillWidth();
                })?.Height(30f);
            })?.FillWidth();
        })?.Height(124f);
        if (row.Root?.Root != null) row.Root.Root.gameObject.SetActive(false);
        return row;
    }

    // ---- pictures ------------------------------------------------------------------------------------------------------------------

    /// <summary>Reads the current players and their islands and updates the cards.</summary>
    private static void Redraw()
    {
        if (!IsOpen) return;
        try
        {
            _players = Islands.Players;
            int count = Math.Min(_players.Count, MaxRows);
            if (_count != null) _count.Value = count == 0 ? "No players yet - start or join a match" : count + " players";
            Calc.Table = Buildings.Table;
            _refreshing = true;
            try
            {
                for (int i = 0; i < MaxRows; i++)
                {
                    var row = Rows[i];
                    if (row?.Root?.Root == null) continue;
                    bool shown = i < count;
                    row.Root.Root.gameObject.SetActive(shown);
                    if (!shown) { row.Player = null; row.LastCode = null; continue; }
                    var p = _players[i];
                    if (!Islands.Same(row.Player, p)) { row.Player = p; row.LastCode = null; }
                    row.Pick?.Text(p.Name + (p.IsLocal ? "  (you)" : "")).Set(Ticked.Contains(p.Game.Pointer));
                    Snapshot(row);
                }
            }
            finally { _refreshing = false; }
        }
        catch (Exception e) { Plugin.L.LogError("[players] redraw: " + e); }
    }

    /// <summary>Reads one player's island and redraws the picture when the island changed.</summary>
    private static void Snapshot(Row row)
    {
        byte[] code = Islands.Export(row.Player);
        if (code == null || code.Length == 0) { SetInfo(row, "No island yet."); return; }
        if (row.LastCode != null && row.LastCode.AsSpan().SequenceEqual(code)) return;
        row.LastCode = code;

        var d = IslandCodec.FromCodeBytes(code);
        if (!d.SideIsExact) { SetInfo(row, "Unexpected island layout."); return; }
        Calc.Load(d);
        Draw(row, d);
        int land = 0, meteor = 0;
        foreach (int g in d.Ground) { if (g >= 2) land++; if (g == 5) meteor++; }
        SetInfo(row, $"Land {land} tiles, {d.Buildings.Count} buildings" + (meteor > 0 ? $", {meteor} meteorite tiles" : "") + ".");
    }

    private static void SetInfo(Row row, string text) { if (row.Info != null) row.Info.Value = text; }

    private static void Draw(Row row, IslandData d)
    {
        int side = d.Side, n = side * Px;
        if (row.Pixels.Length != n * n)
        {
            row.Pixels = new Color32[n * n];
            if (row.Tex != null) UnityEngine.Object.Destroy(row.Tex);
            row.Tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            row.Picture?.SetTexture(row.Tex);
        }
        var px = row.Pixels;
        for (int gy = 0; gy < side; gy++)
            for (int gx = 0; gx < side; gx++)
            {
                int g = d.Ground[gy * side + gx];
                var col = g >= 0 && g < EditorWindow.GroundColors.Length ? EditorWindow.GroundColors[g] : new Color32(255, 0, 255, 255);
                int nat = d.Nature[gy * side + gx];
                if (nat >= 0 && nat < EditorWindow.NatureColors.Length) col = Color32.Lerp(col, EditorWindow.NatureColors[nat], 0.7f);
                Fill(px, n, gx, gy, col);
            }
        foreach (var b in d.Buildings)
        {
            var col = EditorWindow.BuildingColor(b.Type);
            foreach (var (fx, fy) in Calc.Footprint(b))
                if (Calc.InBounds(fx, fy)) Fill(px, n, fx, fy, col);
        }
        row.Tex.SetPixels32(new Il2CppStructArray<Color32>(px));
        row.Picture?.Refresh();
    }

    // Y up like the game: row 0 of the island is the bottom of the picture (Texture2D rows also start at the bottom).
    private static void Fill(Color32[] px, int n, int gx, int gy, Color32 col)
    {
        for (int dy = 0; dy < Px; dy++)
            for (int dx = 0; dx < Px; dx++)
                px[(gy * Px + dy) * n + gx * Px + dx] = col;
    }

    // ---- selection -------------------------------------------------------------------------------------------------------------------

    private static void Pick(Func<MatchPlayer, bool> which)
    {
        Ticked.Clear();
        foreach (var p in _players) if (which(p)) Ticked.Add(p.Game.Pointer);
        Redraw();
    }

    private static List<MatchPlayer> TickedPlayers()
    {
        var list = new List<MatchPlayer>();
        foreach (var p in _players) if (Ticked.Contains(p.Game.Pointer)) list.Add(p);
        return list;
    }

    private static MatchPlayer At(int index) => index < _players.Count ? _players[index] : null;

    private static void Edit(int index) { var p = At(index); if (p != null) Windows.EditPlayer(p); }

    private static void SetStatus(string s) { if (_status != null) _status.Value = s; }

    // ---- actions ---------------------------------------------------------------------------------------------------------------------

    private static void CopyOne(int index)
    {
        var p = At(index);
        var code = Islands.Export(p);
        if (code == null) { SetStatus("That island cannot be read yet."); return; }
        SetStatus(Clipboard.SetText(Convert.ToBase64String(code)) ? $"Copied the island code of {p.Name}." : "Could not write to the clipboard.");
    }

    private static void CopyCodes()
    {
        var ticked = TickedPlayers();
        if (ticked.Count == 0) { SetStatus("Tick at least one player first."); return; }
        var lines = new List<string>();
        foreach (var p in ticked)
        {
            var code = Islands.Export(p);
            if (code != null) lines.Add(p.Name + "\t" + Convert.ToBase64String(code));
        }
        SetStatus(Clipboard.SetText(string.Join("\n", lines)) ? $"Copied {lines.Count} island code(s), one per line (name, tab, code)." : "Could not write to the clipboard.");
    }

    private static void ApplyEditorIsland()
    {
        byte[] code = EditorWindow.CurrentCode();
        if (code == null) { SetStatus("The editor has no island yet - open the editor and load or create one."); return; }
        Run("Editor island applied", _ => code);
    }

    private static void Bulk(string done, Action<EditorModel> edit)
    {
        Calc.Table = Buildings.Table;
        Run(done, current =>
        {
            var d = IslandCodec.FromCodeBytes(current);
            if (!d.SideIsExact) return null;
            Calc.Load(d);
            edit(Calc);
            return IslandCodec.ToCodeBytes(Calc.Data);
        });
    }

    /// <summary>For every ticked player: current island code -> new island code (null = skip). Keeps a backup first.</summary>
    private static void Run(string done, Func<byte[], byte[]> make)
    {
        var ticked = TickedPlayers();
        if (ticked.Count == 0) { SetStatus("Tick at least one player first."); return; }
        int ok = 0, skipped = 0, failed = 0;
        string lastError = null;
        foreach (var p in ticked)
        {
            try
            {
                byte[] current = Islands.Export(p);
                byte[] next = current == null ? null : make(current);
                if (next == null) { skipped++; continue; }
                Backups[p.Game.Pointer] = current;
                if (Islands.Import(p, next, out string message)) ok++;
                else { failed++; lastError = message; }
            }
            catch (Exception e) { failed++; lastError = e.Message; Plugin.L.LogError("[players] " + e); }
        }
        SetStatus($"{done}: {ok} island(s)" + (skipped > 0 ? $", {skipped} skipped" : "") + (failed > 0 ? $", {failed} FAILED ({lastError})" : "") + ".");
    }

    private static void RestoreBackups()
    {
        var ticked = TickedPlayers();
        if (ticked.Count == 0) { SetStatus("Tick at least one player first."); return; }
        int ok = 0, none = 0, failed = 0;
        foreach (var p in ticked)
        {
            if (!Backups.TryGetValue(p.Game.Pointer, out var code)) { none++; continue; }
            if (Islands.Import(p, code, out _)) ok++; else failed++;
        }
        SetStatus($"Restored {ok} island(s)" + (none > 0 ? $", {none} had no backup" : "") + (failed > 0 ? $", {failed} FAILED" : "") + ".");
    }
}
