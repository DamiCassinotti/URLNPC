// One reward column per commanded mode (#44): the mode in the ModeChannel picks
// which column is live, so the mode one-hot the policy observes actually changes
// what pays. Rows the mode doesn't move — kill, death, timeout, alive-per-step,
// wasted shot — stay global on EnemyAgent.
public struct ModeRewardColumn
{
    public float hitTarget;
    // Positive magnitude; the agent subtracts it.
    public float gotHit;
    // Per metre closed on the target since the last step. Negative pays for
    // opening distance instead.
    public float closingPerMeter;
    // Per step while the target's eye-line to this body is broken.
    public float coverPerStep;
    // Once per patch of the arena first entered this episode.
    public float newArea;
    // Positive magnitude, subtracted per step while inside tooCloseDistance.
    // Per mode (#80): as one global row it also taxed Hunt, whose job is to close.
    public float tooClosePerStep;
}

public class ModeRewardTable
{
    readonly ModeRewardColumn[] columns = new ModeRewardColumn[NpcModes.All.Length];

    public ModeRewardColumn this[NpcMode mode]
    {
        get => columns[(int)mode];
        set => columns[(int)mode] = value;
    }
}
