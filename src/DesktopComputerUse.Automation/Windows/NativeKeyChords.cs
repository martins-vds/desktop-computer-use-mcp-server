using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.Windows;

public static class NativeKeyChords
{
    private static readonly IReadOnlyDictionary<string, ushort> Keys = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
    {
        ["Tab"] = 0x09, ["Enter"] = 0x0D, ["Escape"] = 0x1B, ["Backspace"] = 0x08,
        ["Delete"] = 0x2E, ["Space"] = 0x20, ["Home"] = 0x24, ["End"] = 0x23,
        ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
        ["PageUp"] = 0x21, ["PageDown"] = 0x22,
        ["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73,
        ["F5"] = 0x74, ["F6"] = 0x75, ["F7"] = 0x76, ["F8"] = 0x77,
        ["F9"] = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B
    };
    private static readonly IReadOnlyDictionary<string, ushort[]> Chords = BuildChords();

    public static IReadOnlyList<ushort> Parse(string chord, bool allowSystemKeys)
    {
        var canonical = Normalize(chord);
        if (canonical == "ALT+F4" && !allowSystemKeys) throw Invalid();
        if (!Chords.TryGetValue(canonical, out var keys)) throw Invalid();
        return keys.ToArray();
    }

    private static string Normalize(string chord)
    {
        if (string.IsNullOrWhiteSpace(chord) || chord.Length > 32) throw Invalid();
        return chord.Trim().ToUpperInvariant();
    }

    private static IReadOnlyDictionary<string, ushort[]> BuildChords()
    {
        var chords = Keys.ToDictionary(pair => pair.Key.ToUpperInvariant(), pair => new[] { pair.Value });
        chords["ALT+F4"] = [0x12, 0x73];
        chords["SHIFT+TAB"] = [0x10, 0x09];
        foreach (var key in "AZYFS") chords["CTRL+" + key] = [0x11, key];
        foreach (var pair in Keys.Where(pair => pair.Value is >= 0x21 and <= 0x28))
        {
            var key = pair.Key.ToUpperInvariant();
            chords["CTRL+" + key] = [0x11, pair.Value];
            chords["SHIFT+" + key] = [0x10, pair.Value];
            chords["CTRL+SHIFT+" + key] = [0x11, 0x10, pair.Value];
        }
        return chords;
    }

    private static NativeOperationException Invalid() =>
        new(NativeFailureCode.InvalidArgument, "keyPress", "validateChord", "Key chord is not allowlisted.");
}
