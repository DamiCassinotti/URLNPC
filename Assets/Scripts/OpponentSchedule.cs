// Which opponent the player side plays each training episode (#109). Pure
// from-scratch self-play produced two mutually weak agents — the policy lost
// ~2:1 to the scripted hunt-and-shoot heuristic in every mode, because neither
// side was ever aggressive enough to teach the other to punish aggression.
// Running the heuristic on a fraction of the episodes puts a competent
// aggressor in the arena without giving up self-play the rest of the time.
//
// The fraction is spread evenly rather than drawn: a policy-dependent number of
// draws would have to stay off the seeded streams the arena and spawns share,
// and an exact cadence makes two runs at the same fraction comparable. Pure
// logic; PlayerAgent is the adapter that swaps the BehaviorType.
public class OpponentSchedule
{
    public const string FractionArg = "-heuristicOpponent";

    public float HeuristicFraction { get; }

    int episodes;
    int heuristicEpisodes;

    public OpponentSchedule(float heuristicFraction)
    {
        HeuristicFraction = heuristicFraction < 0f ? 0f
                          : heuristicFraction > 1f ? 1f : heuristicFraction;
    }

    // Counting owed episodes against the total rather than accumulating the
    // fraction: 0.1 added ten times lands under 1 and would drop an episode
    // every so often.
    public bool NextEpisodeIsHeuristic()
    {
        episodes++;
        int owed = (int)(episodes * (double)HeuristicFraction);
        if (owed <= heuristicEpisodes) return false;
        heuristicEpisodes++;
        return true;
    }

    // A share in [0, 1]. Invariant culture: the argument comes off a command
    // line, not a locale-formatted UI.
    public static bool TryParseFraction(string value, out float fraction)
    {
        fraction = 0f;
        if (!float.TryParse(value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float parsed))
        {
            return false;
        }
        if (float.IsNaN(parsed) || parsed < 0f || parsed > 1f) return false;
        fraction = parsed;
        return true;
    }

    static bool commandLineRead;
    static float commandLineFraction;

    // Read once, like NpcModes' training pool: the argument can't change
    // mid-process and this is asked per episode.
    public static float CommandLineFraction
    {
        get
        {
            if (commandLineRead) return commandLineFraction;
            commandLineRead = true;
            string[] args = System.Environment.GetCommandLineArgs();
            if (CommandLineArgs.TryRead(args, FractionArg, TryParseFraction, out float parsed))
            {
                commandLineFraction = parsed;
            }
            else if (CommandLineArgs.Contains(args, FractionArg))
            {
                // Loud, but the run continues on pure self-play: this is read
                // mid-training, where there is no clean exit. scripts/train.sh
                // rejects a bad value before the run starts.
                UnityEngine.Debug.LogError(
                    $"[OpponentSchedule] {FractionArg} takes a fraction in [0, 1] — training against the policy only.");
            }
            return commandLineFraction;
        }
    }
}
