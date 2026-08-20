// The commanded mode the NPC policy is conditioned on: one shared policy that
// behaves differently depending on which mode is in the ModeChannel. A scripted
// ModeDirector writes it during training, the LLM selector at inference.
public enum NpcMode
{
    Hunt = 0,
    HoldCover = 1,
    Retreat = 2,
    Patrol = 3,
}

// Inspector-friendly set of modes (ModeDirector.enabledModes). Bit i is
// NpcMode i.
[System.Flags]
public enum NpcModeMask
{
    None = 0,
    Hunt = 1 << (int)NpcMode.Hunt,
    HoldCover = 1 << (int)NpcMode.HoldCover,
    Retreat = 1 << (int)NpcMode.Retreat,
    Patrol = 1 << (int)NpcMode.Patrol,
}

public static class NpcModes
{
    // Declaration order is the observation order of the mode one-hot; changing
    // it invalidates every trained model.
    public static readonly NpcMode[] All =
    {
        NpcMode.Hunt, NpcMode.HoldCover, NpcMode.Retreat, NpcMode.Patrol,
    };

    public const NpcModeMask AllMask = NpcModeMask.Hunt | NpcModeMask.HoldCover
                                     | NpcModeMask.Retreat | NpcModeMask.Patrol;

    public const string TrainModesArg = "-trainModes";

    // Which modes a director may command (issue #82). Training gets its pool
    // from code, the way CombatBalance owns the combat numbers: Enemy.prefab
    // pinned Hunt|Retreat, the binary FPS scene is a prefab instance that can
    // override it again, and CombatantRig composes a third director for the
    // agent-driven player — so a serialized mask can't be the single source and
    // the two self-play bodies could train on different pools. `-trainModes`
    // narrows it for a restricted-pool run (#97) and reaches both bodies,
    // because it is one process-wide argument rather than three fields.
    // Outside training the Inspector field wins, so manual play and the
    // mode-baseline runs can still pin a subset.
    public static NpcModeMask ResolveEnabled(NpcModeMask serialized, bool training)
    {
        return ResolveEnabled(serialized, training, CommandLineTrainModes);
    }

    internal static NpcModeMask ResolveEnabled(NpcModeMask serialized, bool training, NpcModeMask trainingPool)
    {
        if (!training) return serialized;
        return trainingPool == NpcModeMask.None ? AllMask : trainingPool;
    }

    // "all", or a comma-separated list of mode names. None is not a legal
    // value: it would leave the director with nothing to draw.
    internal static bool TryParseModeList(string value, out NpcModeMask mask)
    {
        mask = NpcModeMask.None;
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Trim().ToLowerInvariant() == "all")
        {
            mask = AllMask;
            return true;
        }
        NpcModeMask parsed = NpcModeMask.None;
        foreach (string part in value.Split(','))
        {
            // IsDefined as well as TryParse: TryParse happily accepts a number,
            // and (NpcMode)99 would shift its way onto some unrelated bit.
            if (!System.Enum.TryParse(part.Trim(), true, out NpcMode mode)
                || !System.Enum.IsDefined(typeof(NpcMode), mode))
            {
                return false;
            }
            parsed |= mode.ToMask();
        }
        if (parsed == NpcModeMask.None) return false;
        mask = parsed;
        return true;
    }

    static bool commandLineRead;
    static NpcModeMask commandLineMask;

    // Read once: ResolveEnabled runs per mode draw, and the argument can't
    // change mid-process.
    static NpcModeMask CommandLineTrainModes
    {
        get
        {
            if (commandLineRead) return commandLineMask;
            commandLineRead = true;
            string[] args = System.Environment.GetCommandLineArgs();
            if (CommandLineArgs.TryRead(args, TrainModesArg, TryParseModeList, out NpcModeMask parsed))
            {
                commandLineMask = parsed;
            }
            else if (CommandLineArgs.Contains(args, TrainModesArg))
            {
                // Falls back to all four rather than failing the run: this is
                // read mid-training, where there is no clean exit. Loudly,
                // though — a run on the wrong pool is hours wasted.
                UnityEngine.Debug.LogError(
                    $"[NpcModes] {TrainModesArg} takes all|<Mode>[,<Mode>...] — training all four modes instead.");
            }
            return commandLineMask;
        }
    }

    public static NpcModeMask ToMask(this NpcMode mode) => (NpcModeMask)(1 << (int)mode);

    public static bool Contains(this NpcModeMask mask, NpcMode mode) => (mask & mode.ToMask()) != 0;

    // Allocates: call it when the mask changes, not per frame.
    public static NpcMode[] Enabled(this NpcModeMask mask)
    {
        return System.Array.FindAll(All, mode => mask.Contains(mode));
    }
}
