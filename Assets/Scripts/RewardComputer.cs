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

    // action is 0=Patrol, 1=Chase, 2=Attack. distanceToTarget is a true-state
    // read, which reward computation is allowed (sensory contract, issue #9).
    public float StepReward(int action, bool didShoot, bool targetInSight, float distanceToTarget)
    {
        float reward = aliveRewardPerStep;
        if (tooClosePenaltyPerStep > 0f && distanceToTarget < tooCloseDistance)
        {
            reward -= tooClosePenaltyPerStep;
        }
        if (action == 2 && didShoot && !targetInSight)
        {
            reward -= wastedShotPenalty;
        }
        return reward;
    }
}
