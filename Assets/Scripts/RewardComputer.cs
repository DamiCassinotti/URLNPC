// The per-decision-step reward as a pure mapping, engine-free so the whole
// table is testable without an Academy. EnemyAgent builds one from its
// serialized tunables in Initialize and calls it once per OnActionReceived;
// terminal rewards (hit/kill/death/timeout) stay on the agent's Health-event
// handlers.
public class RewardComputer
{
    public float aliveRewardPerStep = 0.001f;
    public float wastedShotPenalty = 0.05f;
    public float tooClosePenaltyPerStep = 0.005f;
    public float tooCloseDistance = 6f;

    // fired is the fire branch's choice this step; didShoot is whether the shot
    // actually left the barrel (it also needs the cooldown and a remembered
    // target), and is stale when fired is false. distanceToTarget is a
    // true-state read, which reward computation is allowed (issue #9).
    public float StepReward(bool fired, bool didShoot, bool targetInSight, float distanceToTarget)
    {
        float reward = aliveRewardPerStep;
        if (tooClosePenaltyPerStep > 0f && distanceToTarget < tooCloseDistance)
        {
            reward -= tooClosePenaltyPerStep;
        }
        if (fired && didShoot && !targetInSight)
        {
            reward -= wastedShotPenalty;
        }
        return reward;
    }
}
