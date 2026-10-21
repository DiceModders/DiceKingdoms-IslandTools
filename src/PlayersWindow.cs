using System;
using System.Collections.Generic;
using System.Linq;
using DiceModders.Shared;
using DiceModders.UiKit;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace DiceModders.IslandTools;

// Inside namespace DiceModders.* the bare name "UiKit" would be the NAMESPACE DiceModders.UiKit; this alias makes it the static class again.
using UiKit = DiceModders.UiKit.UiKit;

/// <summary>
/// The players dashboard: one card per player of the match with a live picture of their island, a tick box, and bulk actions for every ticked
/// player at once (apply the editor's island, grow / shrink the coast, clean up, copy the codes, undo with the automatic backups).
/// Pictures are refreshed one player at a time (about two per second in total), so a big lobby does not cause a hitch.
///
/// All changes go through the same ImportIsland path as the editor, so the notes of the README apply: changing OTHER players' islands is
/// experimental and only has a chance to work for the host.
/// </summary>
internal sealed class PlayersWindow
{
    private const int Rows = State.MaxTrackedPlayers;
    private const int Px = 3;                       // texels per tile in the pictures
    private const long RefreshEveryMs = 500;       // one player's picture per step

    private sealed class Row
    {
        public ContainerHandle Root;
        public ToggleHandle Pick;
        public ImageHandle Picture;
        public LabelHandle Info;
        public Texture2D Tex;
        public Color32[] Pixels;
        public string Name = "";
        public byte[] LastBytes;
        public string InfoText = "";
        public bool PictureDirty;
    }

    private WindowHandle _win;
    private readonly Row[] _rows = new Row[Rows];
    private LabelHandle _status, _count;
    private ToggleHandle _live;
    private int _next;
    private long _nextStep;
    private int _shownCount = -1;
    private bool _refreshing;
    private readonly EditorModel _calc = new();     // used to render footprints and to run the bulk edits

    public bool IsOpen => _win != null && _win.IsOpen;
    public void Close() { _win?.Close(); _win = null; }

    // ---- window --------------------------------------------------------------------------------------------------------------------

    public void Open()
    {
        _win = UiKit.Window(w =>
        {
            w.Size(780, 820).Draggable().Fullscreenable().Closable().Title("Players");
            w.FullscreenButton();
            w.Position(120f, 60f);

            var b = w.Body;
            b.Spacing(6f);

            b.Horizontal(r =>
            {
                _count = r.Label("", 16f).WeightW(1.4f);
                r.Button("All", () => SetAll(true), 15f);
                r.Button("None", () => SetAll(false), 15f);
                r.Button("Only me", OnlyMe, 15f);
                _live = r.Toggle("Live pictures", true, v => { if (_refreshing) return; });
                _live.WeightW(1.2f);
            }).FixedH(32f);

            b.ScrollView(list =>
            {
                list.Spacing(6f);
                list.Padding(0, 26, 0, 0);
                for (int i = 0; i < Rows; i++) _rows[i] = BuildRow(list, i);
            }).WeightH(1f);

            b.Label("Actions on the ticked players (a backup of each island is kept; \"Undo\" puts it back):", 15f);
            b.Horizontal(r =>
            {
                r.Button("Apply editor island", ApplyEditorIsland, 15f);
                r.Button("Copy codes", CopyCodes, 15f);
                r.Button("Undo (backups)", RestoreBackups, 15f);
            }).FixedH(32f);
            b.Horizontal(r =>
            {
                r.Button("Coast: Grow", () => Bulk("Coast grown", m => { m.Morph("grow"); return true; }), 15f);
                r.Button("Coast: Shrink", () => Bulk("Coast shrunk", m => { m.Morph("shrink"); return true; }), 15f);
                r.Button("Coast: Outline", () => Bulk("Coast outlined", m => { m.Morph("outline"); return true; }), 15f);
            }).FixedH(32f);
            b.Horizontal(r =>
            {
                r.Button("Clear nature under buildings", () => Bulk("Nature under buildings removed", m => { m.ClearNatureUnderBuildings(); return true; }), 15f);
                r.Button("All HP default", () => Bulk("Building HP set to default", m => { m.SetAllHpToDefault(); return true; }), 15f);
            }).FixedH(32f);
            _status = b.Paragraph("Tick players, then use an action. Click a picture or \"Edit\" to open that island in the editor.", 15f);
        });
        ModPlugin.L.LogInfo("[ui] players window built.");
    }

    private Row BuildRow(ContainerHandle list, int index)
    {
        var row = new Row();
        int side = 49 * Px;
        row.Tex = new Texture2D(side, side, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        row.Pixels = new Color32[side * side];
        row.Root = list.Horizontal(r =>
        {
            r.Spacing(10f);
            row.Picture = r.Image(row.Tex).Filter(FilterMode.Point).FixedW(116f).FixedH(116f);
            row.Picture.OnPointer(e => { if (e.Kind == PointerKind.Click && e.Button == 0) EditorUi.EditPlayer(index); });
            r.Vertical(col =>
            {
                col.Spacing(4f);
                row.Pick = col.Toggle("", false, v => { if (_refreshing) return; });
                row.Info = col.Paragraph("", 15f);
                col.Horizontal(bb =>
                {
                    bb.Button("Edit", () => EditorUi.EditPlayer(index), 15f);
                    bb.Button("Copy code", () => CopyOne(index), 15f);
                }).FixedH(30f);
            }).WeightW(1f);
        }).FixedH(124f);
        row.Root.Root.gameObject.SetActive(false);
        return row;
    }

    // ---- per-frame -------------------------------------------------------------------------------------------------------------------

    public void Tick()
    {
        if (!IsOpen) return;
        try
        {
            int count = Math.Min(IslandOps.PlayerCount, Rows);
            _refreshing = true;
            try
            {
                if (count != _shownCount)
                {
                    _shownCount = count;
                    for (int i = 0; i < Rows; i++) _rows[i].Root.Root.gameObject.SetActive(i < count);
                    _count.SetText(count == 0 ? "No players yet - start or join a match" : count + " players");
                }
                for (int i = 0; i < count; i++)
                {
                    string name = IslandOps.PlayerName(i) + (IslandOps.IsLocalPlayer(i) ? "  (you)" : "");
                    if (_rows[i].Name != name) { _rows[i].Name = name; _rows[i].Pick.SetText(name); }
                    if (_rows[i].Info.GetText() != _rows[i].InfoText) _rows[i].Info.SetText(_rows[i].InfoText);
                    if (_rows[i].PictureDirty) { _rows[i].PictureDirty = false; Upload(_rows[i]); }
                }
            }
            finally { _refreshing = false; }

            long now = Environment.TickCount64;
            if (count > 0 && _live.Value && now >= _nextStep)
            {
                _nextStep = now + RefreshEveryMs;
                _next = (_next + 1) % count;
                Snapshot(_next);
            }
        }
        catch (Exception e) { ModPlugin.L.LogError("[players] tick: " + e); }
    }

    /// <summary>Reads one player's island and redraws their picture (only when the island changed).</summary>
    private void Snapshot(int i)
    {
        var row = _rows[i];
        var grid = IslandOps.PlayerGrid(i);
        if (grid == null) { row.InfoText = "No island yet."; return; }
        byte[] bytes = IslandOps.ExportBytes(grid);
        if (bytes == null || bytes.Length == 0) { row.InfoText = "The game returned no island data."; return; }
        if (row.LastBytes != null && row.LastBytes.AsSpan().SequenceEqual(bytes)) return;
        row.LastBytes = bytes;

        var d = IslandCodec.FromCodeBytes(bytes);
        if (!d.SideIsExact) { row.InfoText = "Unexpected island layout."; return; }
        _calc.Table = BuildingTable.FromDumpOrDefault(State.BuildingDumpText);
        _calc.Load(d);
        Draw(row, d);
        int land = 0, meteor = 0;
        foreach (int g in d.Ground) { if (g >= 2) land++; if (g == 5) meteor++; }
        row.InfoText = $"Land {land} tiles, {d.Buildings.Count} buildings" + (meteor > 0 ? $", {meteor} meteorite tiles" : "") + ".";
        row.PictureDirty = true;
    }

    private void Draw(Row row, IslandData d)
    {
        int side = d.Side, n = side * Px;
        if (row.Pixels.Length != n * n)
        {
            row.Pixels = new Color32[n * n];
            row.Tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            row.Picture.SetTexture(row.Tex);
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
            foreach (var (fx, fy) in _calc.Footprint(b))
                if (_calc.InBounds(fx, fy)) Fill(px, n, fx, fy, col);
        }
    }

    // Y up like the game: row 0 of the island is the bottom of the picture (Texture2D rows also start at the bottom).
    private static void Fill(Color32[] px, int n, int gx, int gy, Color32 col)
    {
        for (int dy = 0; dy < Px; dy++)
            for (int dx = 0; dx < Px; dx++)
                px[(gy * Px + dy) * n + gx * Px + dx] = col;
    }

    private static void Upload(Row row)
    {
        row.Tex.SetPixels32(new Il2CppStructArray<Color32>(row.Pixels));
        row.Picture.Refresh();
    }

    // ---- selection -------------------------------------------------------------------------------------------------------------------

    private void SetAll(bool on)
    {
        _refreshing = true;
        try { for (int i = 0; i < Math.Min(IslandOps.PlayerCount, Rows); i++) _rows[i].Pick.SetValue(on); }
        finally { _refreshing = false; }
    }

    private void OnlyMe()
    {
        _refreshing = true;
        try { for (int i = 0; i < Math.Min(IslandOps.PlayerCount, Rows); i++) _rows[i].Pick.SetValue(IslandOps.IsLocalPlayer(i)); }
        finally { _refreshing = false; }
    }

    private List<int> Ticked()
    {
        var list = new List<int>();
        for (int i = 0; i < Math.Min(IslandOps.PlayerCount, Rows); i++) if (_rows[i].Pick.Value) list.Add(i);
        return list;
    }

    private void SetStatus(string s) => _status?.SetText(s);

    private static string PlainName(string shown) => shown.Replace("  (you)", "");

    // ---- actions ---------------------------------------------------------------------------------------------------------------------

    private void CopyOne(int i)
    {
        var bytes = IslandOps.ExportBytes(IslandOps.PlayerGrid(i));
        if (bytes == null) { SetStatus("That island cannot be read yet."); return; }
        SetStatus(Clipboard.SetText(Convert.ToBase64String(bytes)) ? $"Copied the island code of {PlainName(_rows[i].Name)}." : "Could not write to the clipboard.");
    }

    private void CopyCodes()
    {
        var ticked = Ticked();
        if (ticked.Count == 0) { SetStatus("Tick at least one player first."); return; }
        var lines = new List<string>();
        foreach (int i in ticked)
        {
            var bytes = IslandOps.ExportBytes(IslandOps.PlayerGrid(i));
            if (bytes != null) lines.Add(PlainName(_rows[i].Name) + "\t" + Convert.ToBase64String(bytes));
        }
        SetStatus(Clipboard.SetText(string.Join("\n", lines)) ? $"Copied {lines.Count} island code(s), one per line (name, tab, code)." : "Could not write to the clipboard.");
    }

    private void ApplyEditorIsland()
    {
        byte[] code = EditorWindow.CurrentCodeBytes();
        if (code == null) { SetStatus("The editor has no island yet - open the editor and load or create one."); return; }
        Run("Editor island applied", (i, current) => code);
    }

    private void Bulk(string done, Func<EditorModel, bool> op)
    {
        _calc.Table = BuildingTable.FromDumpOrDefault(State.BuildingDumpText);
        Run(done, (i, current) =>
        {
            var d = IslandCodec.FromCodeBytes(current);
            if (!d.SideIsExact) return null;
            _calc.Load(d);
            return op(_calc) ? IslandCodec.ToCodeBytes(_calc.Data) : null;
        });
    }

    /// <summary>Runs <paramref name="make"/> for every ticked player: (index, current island code) -> new island code, or null to skip. Keeps a backup first.</summary>
    private void Run(string done, Func<int, byte[], byte[]> make)
    {
        var ticked = Ticked();
        if (ticked.Count == 0) { SetStatus("Tick at least one player first."); return; }
        int ok = 0, skipped = 0, failed = 0;
        string lastError = null;
        foreach (int i in ticked)
        {
            try
            {
                var grid = IslandOps.PlayerGrid(i);
                byte[] current = IslandOps.ExportBytes(grid);
                if (grid == null || current == null) { skipped++; continue; }
                byte[] next = make(i, current);
                if (next == null) { skipped++; continue; }
                State.Backups[PlainName(_rows[i].Name)] = current;
                State.BypassedChecks = 0;
                if (IslandOps.ImportBytes(grid, next, out string error)) { ok++; _rows[i].LastBytes = null; }
                else { failed++; lastError = error; }
            }
            catch (Exception e) { failed++; lastError = e.Message; ModPlugin.L.LogError("[players] " + e); }
        }
        SetStatus($"{done}: {ok} island(s)" + (skipped > 0 ? $", {skipped} skipped" : "") + (failed > 0 ? $", {failed} FAILED ({lastError})" : "") +
                  ". Pictures update in a moment.");
    }

    private void RestoreBackups()
    {
        var ticked = Ticked();
        if (ticked.Count == 0) { SetStatus("Tick at least one player first."); return; }
        int ok = 0, none = 0, failed = 0;
        foreach (int i in ticked)
        {
            if (!State.Backups.TryGetValue(PlainName(_rows[i].Name), out var bytes)) { none++; continue; }
            if (IslandOps.ImportBytes(IslandOps.PlayerGrid(i), bytes, out _)) { ok++; _rows[i].LastBytes = null; }
            else failed++;
        }
        SetStatus($"Restored {ok} island(s)" + (none > 0 ? $", {none} had no backup" : "") + (failed > 0 ? $", {failed} FAILED" : "") + ".");
    }
}
