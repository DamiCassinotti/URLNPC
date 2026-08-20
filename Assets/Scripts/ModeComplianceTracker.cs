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
    // The body is standing in a patch of the arena it had not been in when it
    // walked into it — held for the whole stay, not just the step it entered on.
    public bool inNewArea;
    // Metres the body covered since the last step, so a Patrol step spent
    // standing in fresh ground isn't patrolling it.
    public float distanceMoved;
    // Whether the target was visible on this step — what decides eligibility
    // for the modes that need a bearing to act on.
    public bool targetVisible;
    // Metres to the target, true state like closingDelta. Hunt reads it to
    // credit holding at a range it can actually shoot from.
    public float distanceToTarget;
    // Seconds since the target was last seen, infinity when never seen — the
    // window in which a Retreat step is still breaking contact with someone.
    public float timeSinceSeen;
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

    // Inside this range Hunt is engaging, whether or not it moved or fired on
    // this step (#91). The cooldown is 0.5 s against a ~0.1 s decision period,
    // so a hunter that has closed and is trading shots spends four steps in
    // five neither shooting nor closing — scored on those two alone, Hunt was
    // capped near its own weapon's duty cycle. Past ~20 m the 1.5 degree aim
    // spread is wider than a body, so out there closing is still the job.
    public float huntEngagementDistance = 20f;

    // How long after a sighting a Retreat step is still breaking contact.
    // Longer than the two seconds it takes to round a corner, short enough
    // that roaming an empty arena isn't retreating from anyone.
    public float contactSeconds = 3f;

    // The same idea for HoldCover, but over the horizon PerceptionMemory keeps
    // a sighting for (#105): holding cover is holding it against a threat you
    // still know is out there, and it is worth doing for as long as you do.
    // Scored over every step instead, the rule read the arena's ambient
    // occlusion — the target is unseen ~90% of a round, so a random walk sat at
    // 70% and outscored the heuristic bot.
    public float coverContactSeconds = 10f;

    readonly ModeTally tally = new ModeTally();

    public int TotalSteps => tally.TotalSteps;

    public void Reset() => tally.Reset();

    public void Record(in ComplianceSample sample) =>
        tally.Record(sample.mode, Compliant(sample), Eligible(sample));

    // Steps the mode had a chance to act on (#88). Hunt has nothing to close on
    // while the target is unseen, so scoring those steps made its rate mostly a
    // measure of how often the fight was joined. Retreat is scored while there
    // is someone to break contact with: sight of the target, or a sighting
    // recent enough that staying out of their eye-line is still the retreat
    // working rather than an empty corner of the arena. HoldCover is scored over
    // the longer window it takes cover against. Patrol needs no target.
    public bool Eligible(in ComplianceSample sample)
    {
        switch (sample.mode)
        {
            case NpcMode.Hunt: return sample.targetVisible;
            case NpcMode.Retreat: return sample.targetVisible || sample.timeSinceSeen <= contactSeconds;
            case NpcMode.HoldCover: return sample.targetVisible || sample.timeSinceSeen <= coverContactSeconds;
            default: return true;
        }
    }

    public bool Compliant(in ComplianceSample sample)
    {
        switch (sample.mode)
        {
            case NpcMode.Hunt:
                return sample.shotFired
                    || sample.closingDelta > movementDeadband
                    // Holding, not backing off: #102's credit paid for any step
                    // in range of a visible target, which floored a random walk
                    // at 76% because half its steps are in range and going
                    // nowhere. A hunter that is opening distance without firing
                    // is doing neither part of the job.
                    || (sample.targetVisible
                        && sample.distanceToTarget <= huntEngagementDistance
                        && sample.closingDelta >= -movementDeadband);
            case NpcMode.HoldCover: return sample.inCover;
            // Opening distance is one way out of a fight; the other is the
            // eye-line, and a body that broke it has broken contact whether or
            // not it kept walking.
            case NpcMode.Retreat: return sample.closingDelta < -movementDeadband || sample.inCover;
            // Ground covered, not doorways crossed (#105): every step walked
            // through a patch that was fresh on entry counts, where scoring the
            // entering step alone capped the rate near 6% — the arena has more
            // steps than patches — and read the same 0.5% for a sweep as for a
            // random walk. A body standing in fresh ground is not covering it.
            case NpcMode.Patrol: return sample.inNewArea && sample.distanceMoved > movementDeadband;
            default: return false;
        }
    }

    // The cover flag costs a raycast to produce; only these two rules read it,
    // the same way RewardComputer.RewardsCover gates the reward's probe.
    public static bool ReadsCover(NpcMode mode) => mode == NpcMode.HoldCover || mode == NpcMode.Retreat;

    public int Steps(NpcMode mode) => tally.Steps(mode);

    public int EligibleSteps(NpcMode mode) => tally.EligibleSteps(mode);

    public int CompliantSteps(NpcMode mode) => tally.Hits(mode);

    // Compliant steps over ELIGIBLE steps. 0 for a mode that was never
    // commanded, and for one that never got an eligible step; check
    // Steps/EligibleSteps to tell those apart from complying on no step at all.
    public float Rate(NpcMode mode) => tally.Rate(mode);

    public string ComplianceJson() => tally.Json("compliance", "compliant");
}
