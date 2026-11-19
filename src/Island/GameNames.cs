using System.Collections.Generic;

namespace DiceModders.IslandTools;

/// <summary>
/// Building names in the game's language, from the game's own texts ("Archery Range" -> building.ARCHERY_RANGE.name), remembered per language
/// so the map's hover line stays cheap. The listed English name is the fallback.
/// </summary>
internal static class GameNames
{
    private static readonly Dictionary<string, string> Cache = new();
    private static string _language;

    internal static string Building(string english)
    {
        if (string.IsNullOrEmpty(english)) return english;
        string language = DiceModders.ModCore.ModCore.Texts.Language;
        if (language != _language) { Cache.Clear(); _language = language; }
        if (Cache.TryGetValue(english, out var name)) return name;
        name = DiceModders.ModCore.ModCore.Texts.Game("building." + english.ToUpperInvariant().Replace(' ', '_') + ".name", english);
        Cache[english] = name;
        return name;
    }
}
