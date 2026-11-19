using System;
using System.Text;
using UnityEngine;

namespace DiceModders.IslandTools;

/// <summary>The system clipboard through Unity's own <c>GUIUtility.systemCopyBuffer</c> (the same one the game's text fields use), and island-code decoding.</summary>
internal static class Clipboard
{
    public static bool SetText(string text)
    {
        try { GUIUtility.systemCopyBuffer = text ?? ""; return true; }
        catch (Exception e) { Plugin.L.LogWarning("[clipboard] write: " + e.Message); return false; }
    }

    public static bool TryGetText(out string text)
    {
        try { text = GUIUtility.systemCopyBuffer; }
        catch (Exception e) { Plugin.L.LogWarning("[clipboard] read: " + e.Message); text = null; }
        return !string.IsNullOrEmpty(text);
    }

    /// <summary>Lenient base64 decode: ignores whitespace and newlines, tolerates missing padding.</summary>
    public static bool TryDecodeBase64(string s, out byte[] data)
    {
        data = null;
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            if (!char.IsWhiteSpace(c)) sb.Append(c);
        while (sb.Length % 4 != 0) sb.Append('=');
        try { data = Convert.FromBase64String(sb.ToString()); return true; }
        catch (FormatException) { return false; }
    }

    /// <summary>The island code on the clipboard, or null with the reason.</summary>
    public static byte[] ReadIslandCode(out string error)
    {
        error = null;
        if (!TryGetText(out string text)) { error = Tx.T("clipboard.noText"); return null; }
        if (!TryDecodeBase64(text, out byte[] bytes)) { error = Tx.T("clipboard.notCode"); return null; }
        return bytes;
    }
}
