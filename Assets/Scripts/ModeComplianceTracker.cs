// One decision step as the compliance rules see it. The positional fields are
// the same true-state reads the mode reward columns are paid from, so the
// metric and the reward can't disagree about what the step did.
public struct ComplianceSample
{
    public NpcMode mode;
    // Metres closed on the target since the last step; negative is opening.
    public float closingDelta;
    // A shot actually left the barrel — not merely the fire branch's choice,
    // which a policy could hold down through the cooldown and look busy.
    public bool shotFired;
    // The target's eye-line to this body is broken — the same true-state probe
    // the cover reward column pays for, so a step the metric calls compliant is
    // a step that paid.
    public bool inCover;
    public bool enteredNewArea;
}

// Mode compliance (#45): of the decision steps spent under each commanded mode,
// how many did what that mode asks for. This is what says the policy obeys the
// mode one-hot instead of playing the same fight whatever it is told, so it is
// recorded during the training runs themselves — EnemyAgent feeds it one sample
// per decision step and flushes the tally per episode to the telemetry log and
// to the trainer's StatsRecorder.
public class ModeComplianceTracker
{
    // Metres per step below which movement is jitter rather than closing or
    // opening: rotation and NavMesh settling move the body a little every step.
    public float movementDeadband = 0.05f;

    readonly ModeTally tally = new ModeTally();

    public int TotalSteps => tally.TotalSteps;

    public void Reset() => tally.Reset();

    public void Record(in ComplianceSample sample) => tally.Record(sample.mode, Compliant(sample));

    public bool Compliant(in ComplianceSample sample)
    {
        switch (sample.mode)
        {
            case NpcMode.Hunt: return sample.shotFired || sample.closingDelta > movementDeadband;
            case NpcMode.HoldCover: return sample.inCover;
            case NpcMode.Retreat: return sample.closingDelta < -movementDeadband;
            // A cell is only new once, so this rate has a low ceiling by
            // construction — read it as new ground per step, comparable
            // between policies, not as a percentage of correct behavior.
            case NpcMode.Patrol: return sample.enteredNewArea;
            default: return false;
        }
    }

    // The cover flag costs a raycast to produce; only HoldCover's rule reads
    // it, the same way RewardComputer.RewardsCover gates the reward's probe.
    public static bool ReadsCover(NpcMode mode) => mode == NpcMode.HoldCover;

    public int Steps(NpcMode mode) => tally.Steps(mode);

    public int CompliantSteps(NpcMode mode) => tally.Hits(mode);

    // 0 for a mode that was never commanded this episode; check Steps to tell
    // that apart from a mode that complied on no step at all.
    public float Rate(NpcMode mode) => tally.Rate(mode);

    public string ComplianceJson() => tally.Json("compliance", "compliant");
}
