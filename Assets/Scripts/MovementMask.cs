// Which movement primitives each commanded mode may pick (#113). One shared
// policy across four modes converged on near-identical behavior whatever the
// one-hot said, and the per-mode reward columns alone did not separate them;
// masking the primitives a mode has no business picking forces the
// differentiation. The fire branch is left free — every mode may shoot.
//
// The frozen 7x2 action space (#43) is unchanged: a mask only zeroes
// probabilities within branch 0. Changing the table still invalidates trained
// models, since the policy optimizes within it.
//
// This gates what the policy may *pick*, not what the body may do: a masked
// primitive is still reachable as another one's fallback inside
// EnemyBehavior.Move (MoveToCover falls back to Retreat with no cover in the
// layout, and everything falls through to Wander before the first sighting).
public static class MovementMask
{
    // Indexed by (int)NpcMode, in NpcModes.All order. Every row has to leave at
    // least one primitive open — ML-Agents throws on a fully masked branch.
    static readonly MovementAction[][] blocked =
    {
        // Hunt: closing is the job, so neither backing off nor hiding.
        new[] { MovementAction.Retreat, MovementAction.MoveToCover },
        // HoldCover: hold the position that breaks the eye-line, don't roam.
        new[] { MovementAction.Advance, MovementAction.Retreat, MovementAction.Wander },
        // Retreat: no closing, and no standing still to be shot at.
        new[] { MovementAction.Advance, MovementAction.Hold },
        // Patrol: cover the ground, don't engage or hide.
        new[] { MovementAction.Advance, MovementAction.Retreat, MovementAction.MoveToCover },
    };

    static readonly MovementAction[] none = new MovementAction[0];

    // A mode outside the enum (a stale serialized value in the binary scene)
    // masks nothing rather than indexing off the end.
    public static MovementAction[] BlockedFor(NpcMode mode)
    {
        int index = (int)mode;
        return index >= 0 && index < blocked.Length ? blocked[index] : none;
    }

    public static bool IsAllowed(NpcMode mode, MovementAction action)
    {
        return System.Array.IndexOf(BlockedFor(mode), action) < 0;
    }
}
