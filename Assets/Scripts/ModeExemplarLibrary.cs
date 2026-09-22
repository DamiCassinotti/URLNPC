using System.Collections.Generic;
using UnityEngine;

// Where an exemplar bank comes from (issue #132): a text asset under
// Resources/Exemplars, named by its id — the same arrangement as
// ModePromptLibrary, for the same reasons (a -batchmode build carries it, and
// scripts/battery.py reads the very file the game sends).
public static class ModeExemplarLibrary
{
    public const string ResourceFolder = "Exemplars/";

    static readonly Dictionary<string, ModeExemplars> cache = new Dictionary<string, ModeExemplars>();

    // False for a missing or malformed bank; the caller refuses to run rather
    // than dropping to zero-shot, since a few-shot run that is quietly
    // zero-shot is an ablation arm reported as the other one.
    public static bool TryLoad(string id, out ModeExemplars bank, out string error)
    {
        bank = null;
        error = "";
        string key = string.IsNullOrWhiteSpace(id) ? ModeExemplars.DefaultId : id.Trim();
        if (cache.TryGetValue(key, out bank))
        {
            if (bank == null) error = $"no usable bank '{key}'";
            return bank != null;
        }

        var asset = Resources.Load<TextAsset>(ResourceFolder + key);
        if (asset == null)
        {
            error = $"no bank '{key}' under Resources/{ResourceFolder}";
        }
        else if (!ModeExemplars.TryParse(key, asset.text, out bank, out error))
        {
            bank = null;
        }
        cache[key] = bank;
        return bank != null;
    }

    // EditMode seam, like ModePromptLibrary's: a test that registers a bank of
    // its own must not leak it into the next test.
    internal static void ClearCache() => cache.Clear();
}
