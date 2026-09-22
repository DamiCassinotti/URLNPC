using System.Collections.Generic;
using UnityEngine;

// Where a prompt variant comes from (issue #131): a text asset under
// Resources/Prompts, named by its id. Resources rather than a path on disk so a
// standalone -batchmode build carries its prompts, and one file per variant so
// scripts/battery.py scores the same text the game sends.
//
// The engine adapter for ModePrompt, the way GameStateSnapshotBuilder is for
// GameStateSnapshot.
public static class ModePromptLibrary
{
    public const string ResourceFolder = "Prompts/";

    static readonly Dictionary<string, ModePrompt> cache = new Dictionary<string, ModePrompt>();

    // False for a missing or unusable variant. The caller is expected to refuse
    // to run rather than substitute another prompt: a run scored against a
    // prompt other than the one it names is worse than a run that doesn't start.
    public static bool TryLoad(string id, out ModePrompt prompt)
    {
        prompt = null;
        string key = string.IsNullOrWhiteSpace(id) ? ModePrompt.DefaultId : id.Trim();
        if (cache.TryGetValue(key, out prompt)) return prompt != null;

        var asset = Resources.Load<TextAsset>(ResourceFolder + key);
        if (asset != null)
        {
            var loaded = new ModePrompt(key, asset.text);
            if (loaded.IsUsable) prompt = loaded;
        }
        cache[key] = prompt;
        return prompt != null;
    }

    // EditMode seam: Resources.Load caches by path, and a test that registers a
    // variant of its own must not leak it into the next test.
    internal static void ClearCache() => cache.Clear();
}
