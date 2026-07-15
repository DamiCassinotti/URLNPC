/// <summary>
/// The per-decision-step reward as a pure mapping, engine-free so the whole
/// table is unit-testable without an Academy. EnemyAgent builds one from its
/// serialized tunables in Initialize and calls it once per OnActionReceived;
/// event-driven terminal rewards (hit/kill/death/timeout) are plain constants
/// and stay on the agent's Health-event handlers.
/// </summary>
public class RewardComputer
{
    public float aliveRewardPerStep = 0.001f;
    public float wastedShotPenalty = 0.05f;
    public float tooClosePenaltyPerStep = 0.005f;
    public float tooCloseDistance = 6f;

    /// <summary>
    /// Reward for one decision step, given what the step did: the chosen
    /// action (0=Patrol, 1=Chase, 2=Attack), whether a shot actually left the
    /// barrel, whether the target was in sight, and the true distance to the
    /// target (environment-side read — reward computation may use true state,
    /// see the sensory contract notes in CLAUDE.md).
    /// </summary>
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
