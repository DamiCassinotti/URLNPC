using UnityEngine;

/// <summary>
/// Process-wide, seedable RNG for everything that affects evaluation
/// reproducibility: arena selection, spawn-point sampling and patrol/wander
/// waypoints. Two runs launched with the same seed produce the same arena
/// sequence and the same spawn positions, which makes evaluation runs
/// replayable (issue #13).
///
/// Seed priority: <c>-runSeed &lt;int&gt;</c> on the command line, then the
/// non-zero <c>runSeed</c> Inspector field on <see cref="ArenaManager"/>,
/// then a time-derived random seed. Whatever wins is logged at startup so
/// even an unseeded run can be replayed after the fact.
///
/// Each random domain gets its own sub-stream so a variable number of draws
/// in one domain (e.g. wander waypoints, which depend on what the policy
/// does) cannot shift the sequences of the others (arena choice, spawns).
///
/// Deliberately NOT routed through here: gameplay noise that is consumed a
/// non-deterministic number of times per frame, like <c>EnemyWeapon</c> aim
/// spread — feeding it from these streams would desynchronise the
/// reproducible domains between runs.
/// </summary>
public static class RunRng
{
    public enum Stream
    {
        Arena = 0,  // which arena layout is built each round
        Spawn = 1,  // NavMesh spawn-point sampling (player + enemy)
        Wander = 2, // patrol/wander waypoints
    }

    const string CommandLineArg = "-runSeed";
    const int StreamCount = 3;

    public static bool Initialized { get; private set; }
    /// <summary>The effective run seed. Valid once <see cref="Initialized"/>.</summary>
    public static int Seed { get; private set; }
    /// <summary>Where the seed came from: "command line", "inspector" or "random".</summary>
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

    /// <summary>
    /// Seed the run once. Later calls are no-ops, so the streams keep
    /// advancing across scene reloads instead of restarting every round.
    /// <paramref name="inspectorSeed"/> 0 means "not configured".
    /// </summary>
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

    /// <summary>Random int in [minInclusive, maxExclusive), like UnityEngine.Random.Range.</summary>
    public static int Range(Stream stream, int minInclusive, int maxExclusive)
    {
        return Get(stream).Next(minInclusive, maxExclusive);
    }

    /// <summary>Random float in [minInclusive, maxInclusive], like UnityEngine.Random.Range.</summary>
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
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == CommandLineArg && int.TryParse(args[i + 1], out seed))
            {
                return true;
            }
        }
        seed = 0;
        return false;
    }
}
