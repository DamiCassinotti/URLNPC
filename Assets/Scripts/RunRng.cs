using UnityEngine;

// Process-wide, seedable RNG for everything that affects reproducibility: two
// runs launched with the same seed build the same arenas and spawn actors in
// the same places (issue #13).
//
// Seed priority: -runSeed <int> on the command line, then a non-zero runSeed on
// ArenaManager, then a time-derived seed. Whatever wins is logged at startup,
// so even an unseeded run can be replayed after the fact.
//
// Each domain gets its own sub-stream so a variable number of draws in one
// (wander waypoints depend on what the policy does) can't shift the others.
//
// Deliberately NOT routed through here: noise consumed a non-deterministic
// number of times per frame, like EnemyWeapon aim spread — it would
// desynchronise the reproducible streams between runs.
public static class RunRng
{
    public enum Stream
    {
        Arena = 0,  // which arena layout is built each round
        Spawn = 1,  // NavMesh spawn-point sampling (player + enemy)
        Wander = 2, // patrol/wander waypoints
        Mode = 3,   // commanded-mode sampling (ModeDirector)
        Action = 4, // the random-action control policy (eval --subject random)
    }

    const string CommandLineArg = "-runSeed";
    const int StreamCount = 5;

    public static bool Initialized { get; private set; }
    public static int Seed { get; private set; }
    // "command line", "inspector" or "random".
    public static string SeedSource { get; private set; } = "uninitialized";

    static System.Random[] streams;

    // Statics survive Enter Play Mode with domain reload disabled — reset per
    // play session so a stale seed never leaks into the next run. (Fires once
    // per session, not per SceneManager.LoadScene, so the streams still
    // persist across round reloads within a run.) Internal: tests reset the
    // streams between cases for the same isolation reason.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    internal static void ResetForNewRun()
    {
        Initialized = false;
        streams = null;
        SeedSource = "uninitialized";
    }

    // Later calls are no-ops, so the streams keep advancing across scene
    // reloads instead of restarting every round. inspectorSeed 0 means "not
    // configured".
    public static void EnsureInitialized(int inspectorSeed)
    {
        if (Initialized) return;

        if (TryReadCommandLineSeed(out int cliSeed))
        {
            Seed = cliSeed;
            SeedSource = "command line";
        }
        else if (inspectorSeed != 0)
        {
            Seed = inspectorSeed;
            SeedSource = "inspector";
        }
        else
        {
            Seed = System.Environment.TickCount;
            SeedSource = "random";
        }

        streams = new System.Random[StreamCount];
        for (int i = 0; i < StreamCount; i++)
        {
            // Large odd prime keeps the per-stream seeds well separated.
            streams[i] = new System.Random(unchecked(Seed + i * 486187739));
        }
        Initialized = true;
        Debug.Log($"[RunRng] Run seed: {Seed} (source: {SeedSource}). " +
                  $"Replay with '{CommandLineArg} {Seed}' or ArenaManager.runSeed = {Seed}.");
    }

    // [minInclusive, maxExclusive), like UnityEngine.Random.Range.
    public static int Range(Stream stream, int minInclusive, int maxExclusive)
    {
        return Get(stream).Next(minInclusive, maxExclusive);
    }

    // [minInclusive, maxInclusive], like UnityEngine.Random.Range.
    public static float Range(Stream stream, float minInclusive, float maxInclusive)
    {
        return minInclusive + (float)Get(stream).NextDouble() * (maxInclusive - minInclusive);
    }

    static System.Random Get(Stream stream)
    {
        // Normally ArenaManager.Awake seeds us first (it runs at execution
        // order -10000); this guard covers any stray early caller.
        if (!Initialized) EnsureInitialized(0);
        return streams[(int)stream];
    }

    static bool TryReadCommandLineSeed(out int seed)
    {
        return CommandLineArgs.TryRead(
            System.Environment.GetCommandLineArgs(), CommandLineArg, int.TryParse, out seed);
    }
}
