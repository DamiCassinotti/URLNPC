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

    // Which modes a director may command (issue #82). Training gets all four
    // from code, the way CombatBalance owns the combat numbers: Enemy.prefab
    // pinned Hunt|Retreat, the binary FPS scene is a prefab instance that can
    // override it again, and CombatantRig composes a third director for the
    // agent-driven player — so a serialized mask can't be the single source and
    // the two self-play bodies could train on different pools. Outside training
    // the Inspector field wins, so manual play and the mode-baseline runs can
    // still pin a subset.
    public static NpcModeMask ResolveEnabled(NpcModeMask serialized, bool training)
    {
        return training ? AllMask : serialized;
    }

    public static NpcModeMask ToMask(this NpcMode mode) => (NpcModeMask)(1 << (int)mode);

    public static bool Contains(this NpcModeMask mask, NpcMode mode) => (mask & mode.ToMask()) != 0;

    // Allocates: call it when the mask changes, not per frame.
    public static NpcMode[] Enabled(this NpcModeMask mask)
    {
        return System.Array.FindAll(All, mode => mask.Contains(mode));
    }
}
