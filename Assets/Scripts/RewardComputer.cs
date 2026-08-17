// Everything the environment can hand the per-step reward, gathered by
// EnemyAgent once per OnActionReceived. distanceToTarget/closingDelta are
// true-state reads, which reward computation is allowed (issue #9).
public struct StepRewardInput
{
    public NpcMode mode;
    // fired is the fire branch's choice this step; didShoot is whether the shot
    // actually left the barrel (it also needs the cooldown and a remembered
    // target), and is stale when fired is false.
    public bool fired;
    public bool didShoot;
    public bool targetInSight;
    public float distanceToTarget;
    // Metres closed on the target since the last step; negative is opening.
    public float closingDelta;
    // The target's eye-line to this body is broken.
    public bool hiddenFromTarget;
    public bool enteredNewArea;
}

// The reward mapping as pure math, engine-free so the whole table is testable
// without an Academy. EnemyAgent builds one from its serialized tunables in
// Initialize: it calls StepReward once per OnActionReceived and reads the mode
// columns for the hit/got-hit events off its Health handlers. Terminal rewards
// (kill/death/timeout) don't vary by mode and stay on the agent.
public class RewardComputer
{
    public float aliveRewardPerStep = 0.0002f;
    public float wastedShotPenalty = 0.05f;
    public float tooClosePenaltyPerStep = 0.001f;
    public float tooCloseDistance = 6f;

    public readonly ModeRewardTable modes = new ModeRewardTable();

    public float StepReward(in StepRewardInput step)
    {
        ModeRewardColumn column = modes[step.mode];

        float reward = aliveRewardPerStep;
        if (tooClosePenaltyPerStep > 0f && step.distanceToTarget < tooCloseDistance)
        {
            reward -= tooClosePenaltyPerStep;
        }
        if (step.fired && step.didShoot && !step.targetInSight)
        {
            reward -= wastedShotPenalty;
        }
        reward += column.closingPerMeter * step.closingDelta;
        if (step.hiddenFromTarget) reward += column.coverPerStep;
        if (step.enteredNewArea) reward += column.newArea;
        return reward;
    }

    // The cover flag costs a raycast to produce, so the agent only probes for
    // the modes whose column actually pays for it.
    public bool RewardsCover(NpcMode mode) => modes[mode].coverPerStep != 0f;
}
