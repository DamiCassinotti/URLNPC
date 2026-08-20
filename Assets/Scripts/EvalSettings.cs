// What a headless evaluation run was asked for (issue #52), as pure logic —
// engine-free so the argument parsing is testable without a player build.
// EvalSession is the adapter half.
public class EvalSettings
{
    public const string EpisodesArg = "-evalEpisodes";
    public const string ModelArg = "-evalModel";
    public const string OpponentArg = "-evalOpponent";
    public const string SubjectArg = "-evalSubject";
    public const string ModesArg = "-evalModes";
    public const string TimeScaleArg = "-evalTimeScale";

    public enum OpponentKind
    {
        Policy,    // the far side runs the same model: self-play
        Heuristic, // the far side runs EnemyAgent.Heuristic, the scripted baseline
    }

    // What drives the NPC side — the side the summary scores. Anything but
    // Policy is a control run: the numbers a model has to beat.
    public enum SubjectKind
    {
        Policy,    // the model named by -evalModel
        Heuristic, // EnemyAgent.Heuristic, the scripted baseline
        Random,    // uniform over the 7x2 action branches
    }

    public enum ModeSourceKind
    {
        Scripted, // ModeDirector's random schedule, the baseline the LLM selector is compared against
        Fixed,    // one mode for the whole run
        None,     // nobody writes the channel; it keeps ModeChannel.initialMode
    }

    // Set only by -evalEpisodes: without it the build is an ordinary play or
    // training session and nothing below applies.
    public bool Enabled;
    public int Episodes;
    // Resources path (no extension) of the model both eval-driven agents run.
    // Runtime ONNX import is editor-only, so the model is baked into the build
    // and named here rather than loaded from a file path.
    public string ModelResource;
    public SubjectKind Subject = SubjectKind.Policy;
    public OpponentKind Opponent = OpponentKind.Policy;
    public ModeSourceKind ModeSource = ModeSourceKind.Scripted;
    public NpcMode FixedMode = NpcMode.Hunt;
    // Game time per rendered frame, in physics steps (EvalSession drives the
    // clock off captureDeltaTime). 1 is the most faithful; raising it trades
    // fidelity for speed the way the trainer's time_scale does.
    public float TimeScale = 1f;

    // Null when the settings are usable. A malformed value is an error rather
    // than a default, so a typo can't silently evaluate the wrong condition.
    public string Error;

    // Unknown arguments are skipped, not rejected: the player is launched with
    // plenty this code knows nothing about (-batchmode, -logFile, ...).
    public static EvalSettings Parse(string[] args)
    {
        var settings = new EvalSettings();
        if (args == null) return settings;

        for (int i = 0; i < args.Length - 1; i++) // one short: every flag takes a value
        {
            string value = args[i + 1];
            if (value == null) continue;
            switch (args[i])
            {
                case EpisodesArg:
                    settings.Enabled = true;
                    if (!int.TryParse(value, out settings.Episodes) || settings.Episodes <= 0)
                    {
                        return settings.Fail($"{EpisodesArg} needs a positive integer, got '{value}'.");
                    }
                    break;
                case ModelArg:
                    settings.ModelResource = value;
                    break;
                case OpponentArg:
                    switch (value.ToLowerInvariant())
                    {
                        case "policy": settings.Opponent = OpponentKind.Policy; break;
                        case "heuristic": settings.Opponent = OpponentKind.Heuristic; break;
                        default: return settings.Fail($"{OpponentArg} takes policy|heuristic, got '{value}'.");
                    }
                    break;
                case SubjectArg:
                    switch (value.ToLowerInvariant())
                    {
                        case "policy": settings.Subject = SubjectKind.Policy; break;
                        case "heuristic": settings.Subject = SubjectKind.Heuristic; break;
                        case "random": settings.Subject = SubjectKind.Random; break;
                        default: return settings.Fail($"{SubjectArg} takes policy|heuristic|random, got '{value}'.");
                    }
                    break;
                case ModesArg:
                    if (!ParseModeSource(settings, value))
                    {
                        return settings.Fail($"{ModesArg} takes scripted|none|<mode>, got '{value}'.");
                    }
                    break;
                case TimeScaleArg:
                    if (!float.TryParse(value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out settings.TimeScale)
                        || settings.TimeScale <= 0f)
                    {
                        return settings.Fail($"{TimeScaleArg} needs a positive number, got '{value}'.");
                    }
                    break;
            }
        }

        if (settings.Enabled && settings.Error == null && string.IsNullOrEmpty(settings.ModelResource))
        {
            settings.Fail($"{ModelArg} is required: an eval run has to say which policy it is scoring.");
        }
        return settings;
    }

    static bool ParseModeSource(EvalSettings settings, string value)
    {
        switch (value.ToLowerInvariant())
        {
            case "scripted":
                settings.ModeSource = ModeSourceKind.Scripted;
                return true;
            case "none":
                settings.ModeSource = ModeSourceKind.None;
                return true;
        }
        if (!System.Enum.TryParse(value, true, out NpcMode mode)) return false;
        settings.ModeSource = ModeSourceKind.Fixed;
        settings.FixedMode = mode;
        return true;
    }

    EvalSettings Fail(string message)
    {
        Error = message;
        return this;
    }
}
