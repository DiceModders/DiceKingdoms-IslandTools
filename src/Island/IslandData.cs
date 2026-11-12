using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace DiceModders.IslandTools;

// Pure C# (no Unity / game types): the island code format, so the in-game editor can read and write island codes.
// Format (IslandGrid.SyncData, MLAPI packed ints), same as tools/island-editor.html:
//   buildings[], remains[]   each: type, x, y, orient, mirror, health, [flag u64 (+ payload u64)], turn
//   ground tiles, nature tiles   each: total count, then runs of (length, value)
//   inner cliffs: count, then 7 ints each
// The island code is base64 of the raw DEFLATE (no zlib header) of this stream; IslandGrid.ExportIsland / ImportIsland
// already work with the deflated bytes.

internal sealed class IslandBuilding
{
    public int Type, X, Y, Orient, Mirror, Health;
    public ulong? Disaster;      // nullable disaster: flag + payload, kept as is
    public int Turn;
    public IslandBuilding Clone() => (IslandBuilding)MemberwiseClone();
}

internal sealed class IslandCliff
{
    // FILE order: the key (kind 0 = MAIN, 1 = EXTENSION; width; height), then the transform
    public int Kind, W, H, X, Y, Orient, Mirror;
    public IslandCliff Clone() => (IslandCliff)MemberwiseClone();
}

internal sealed class IslandData
{
    public List<IslandBuilding> Buildings = new();
    public List<IslandBuilding> Remains = new();
    public int[] Ground = new int[0];
    public int[] Nature = new int[0];
    public List<IslandCliff> Cliffs = new();

    /// <summary>Bytes left over after parsing (0 = the stream was read completely).</summary>
    public int TrailingBytes;

    public int Side => (int)Math.Round(Math.Sqrt(Ground.Length));
    public bool SideIsExact => Side * Side == Ground.Length && Nature.Length == Ground.Length;

    public static IslandData Blank(int side)
    {
        int n = side * side;
        var d = new IslandData { Ground = new int[n], Nature = new int[n] };
        for (int i = 0; i < n; i++) d.Nature[i] = -1;
        return d;
    }

    /// <summary>
    /// A copy on a grid of another size, with the island in the middle: a bigger grid gets water around it, a smaller one cuts the edges off.
    /// Buildings, remains and inner cliffs move with the tiles; the ones whose position falls outside are left out (<paramref name="dropped"/>).
    /// </summary>
    public IslandData Fitted(int side, out int dropped)
    {
        int old = Side, shift = (side - old) / 2;
        var d = Blank(side);
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                int ox = x - shift, oy = y - shift;
                if (ox < 0 || oy < 0 || ox >= old || oy >= old) continue;
                d.Ground[y * side + x] = Ground[oy * old + ox];
                d.Nature[y * side + x] = Nature[oy * old + ox];
            }
        bool Inside(int x, int y) => x >= 0 && y >= 0 && x < side && y < side;
        dropped = 0;
        foreach (var (from, to) in new[] { (Buildings, d.Buildings), (Remains, d.Remains) })
            foreach (var b in from)
            {
                var c = b.Clone();
                c.X += shift; c.Y += shift;
                if (Inside(c.X, c.Y)) to.Add(c); else dropped++;
            }
        foreach (var cliff in Cliffs)
        {
            var c = cliff.Clone();
            c.X += shift; c.Y += shift;
            if (Inside(c.X, c.Y)) d.Cliffs.Add(c); else dropped++;
        }
        return d;
    }

    public IslandData Clone()
    {
        var d = new IslandData
        {
            Ground = (int[])Ground.Clone(), Nature = (int[])Nature.Clone(), TrailingBytes = TrailingBytes,
        };
        foreach (var b in Buildings) d.Buildings.Add(b.Clone());
        foreach (var b in Remains) d.Remains.Add(b.Clone());
        foreach (var c in Cliffs) d.Cliffs.Add(c.Clone());
        return d;
    }

    // ---- reading -----------------------------------------------------------------------------------------------

    private sealed class Reader
    {
        private readonly byte[] _b;
        public int Pos;
        public Reader(byte[] b) { _b = b; }
        public int Left => _b.Length - Pos;

        private int Byte()
        {
            if (Pos >= _b.Length) throw new FormatException("unexpected end of the island data");
            return _b[Pos++];
        }

        public ulong U64()
        {
            int b = Byte();
            if (b <= 0xF0) return (ulong)b;
            if (b < 0xF9) return (ulong)((b * 256 - 0xF010) + Byte());
            if (b == 0xF9) { int hi = Byte(), lo = Byte(); return (ulong)(hi * 256 + lo + 0x8F0); }
            int n = b - 0xF7;
            ulong v = 0;
            for (int i = 0; i < n; i++) v |= (ulong)Byte() << (8 * i);
            return v;
        }

        public int I32()
        {
            ulong x = U64();
            long v = (x & 1) == 0 ? (long)(x / 2) : -(long)((x + 1) / 2);
            if (v < int.MinValue || v > int.MaxValue) throw new FormatException("number out of range in the island data");
            return (int)v;
        }
    }

    private static List<IslandBuilding> ReadBuildings(Reader r)
    {
        int n = r.I32();
        if (n < 0 || n > 100000) throw new FormatException("bad building count " + n);
        var list = new List<IslandBuilding>(n);
        for (int i = 0; i < n; i++)
        {
            var b = new IslandBuilding { Type = r.I32(), X = r.I32(), Y = r.I32(), Orient = r.I32(), Mirror = r.I32(), Health = r.I32() };
            b.Disaster = r.U64() != 0 ? r.U64() : (ulong?)null;
            b.Turn = r.I32();
            list.Add(b);
        }
        return list;
    }

    private static int[] ReadRle(Reader r)
    {
        int total = r.I32();
        if (total < 0 || total > 4000000) throw new FormatException("bad tile count " + total);
        var vals = new int[total];
        int filled = 0;
        while (filled < total)
        {
            int n = r.I32(), v = r.I32();
            if (n <= 0 || n > total - filled) throw new FormatException("bad run length " + n);
            for (int i = 0; i < n; i++) vals[filled++] = v;
        }
        return vals;
    }

    /// <summary>Parses the INFLATED island stream.</summary>
    public static IslandData Parse(byte[] raw)
    {
        var r = new Reader(raw);
        var d = new IslandData();
        d.Buildings = ReadBuildings(r);
        d.Remains = ReadBuildings(r);
        d.Ground = ReadRle(r);
        d.Nature = ReadRle(r);
        int nc = r.I32();
        if (nc < 0 || nc > 100000) throw new FormatException("bad cliff count " + nc);
        for (int i = 0; i < nc; i++)
            d.Cliffs.Add(new IslandCliff { Kind = r.I32(), W = r.I32(), H = r.I32(), X = r.I32(), Y = r.I32(), Orient = r.I32(), Mirror = r.I32() });
        d.TrailingBytes = r.Left;
        return d;
    }

    // ---- writing -----------------------------------------------------------------------------------------------

    private sealed class Writer
    {
        public readonly List<byte> A = new();

        public void U64(ulong v)
        {
            if (v <= 0xF0) A.Add((byte)v);
            else if (v <= 2287) { v -= 0xF0; A.Add((byte)(v / 256 + 0xF1)); A.Add((byte)(v % 256)); }
            else if (v <= 67823) { v -= 0x8F0; A.Add(0xF9); A.Add((byte)(v / 256)); A.Add((byte)(v % 256)); }
            else
            {
                int bits = 64 - System.Numerics.BitOperations.LeadingZeroCount(v);
                int n = Math.Max(3, (bits + 7) / 8);
                A.Add((byte)(0xF7 + n));
                for (int i = 0; i < n; i++) { A.Add((byte)(v & 0xFF)); v >>= 8; }
            }
        }

        public void I32(int v) => U64(v >= 0 ? (ulong)v * 2 : (ulong)(-(long)v) * 2 - 1);
    }

    private static void WriteBuildings(Writer w, List<IslandBuilding> list)
    {
        w.I32(list.Count);
        foreach (var b in list)
        {
            w.I32(b.Type); w.I32(b.X); w.I32(b.Y); w.I32(b.Orient); w.I32(b.Mirror); w.I32(b.Health);
            if (b.Disaster == null) w.U64(0); else { w.U64(1); w.U64(b.Disaster.Value); }
            w.I32(b.Turn);
        }
    }

    private static void WriteRle(Writer w, int[] arr)
    {
        w.I32(arr.Length);
        int i = 0;
        while (i < arr.Length)
        {
            int j = i;
            while (j < arr.Length && arr[j] == arr[i]) j++;
            w.I32(j - i); w.I32(arr[i]);
            i = j;
        }
    }

    /// <summary>Writes the (not yet deflated) island stream.</summary>
    public byte[] Encode()
    {
        var w = new Writer();
        WriteBuildings(w, Buildings);
        WriteBuildings(w, Remains);
        WriteRle(w, Ground);
        WriteRle(w, Nature);
        w.I32(Cliffs.Count);
        foreach (var c in Cliffs)
        {
            w.I32(c.Kind); w.I32(c.W); w.I32(c.H); w.I32(c.X); w.I32(c.Y); w.I32(c.Orient); w.I32(c.Mirror);
        }
        return w.A.ToArray();
    }
}

internal static class IslandCodec
{
    /// <summary>Raw DEFLATE (no zlib header) -> bytes.</summary>
    public static byte[] Inflate(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var ds = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        ds.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>Bytes -> raw DEFLATE (no zlib header).</summary>
    public static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var ds = new DeflateStream(output, CompressionLevel.Optimal, true))
            ds.Write(data, 0, data.Length);
        return output.ToArray();
    }

    public static IslandData FromCodeBytes(byte[] deflated) => IslandData.Parse(Inflate(deflated));
    public static byte[] ToCodeBytes(IslandData d) => Deflate(d.Encode());
}
