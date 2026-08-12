using NUnit.Framework;

// The full per-step reward table (CLAUDE.md "Reward shape") enumerated as pure
// math, including how the penalties combine.
public class RewardComputerTests
{
    const float Tolerance = 1e-6f;

    static RewardComputer DefaultRewards() => new RewardComputer
    {
        aliveRewardPerStep = 0.001f,
        wastedShotPenalty = 0.05f,
        tooClosePenaltyPerStep = 0.005f,
        tooCloseDistance = 6f,
    };

    [Test]
    public void PlainStep_PaysTheAliveBonus()
    {
        var rewards = DefaultRewards();
        Assert.That(rewards.StepReward(fired: false, didShoot: false, targetInSight: false, distanceToTarget: 20f),
            Is.EqualTo(0.001f).Within(Tolerance));
    }

    [Test]
    public void StandingTooClose_CostsTheShapingPenalty()
    {
        var rewards = DefaultRewards();
        Assert.That(rewards.StepReward(false, false, true, 3f),
            Is.EqualTo(0.001f - 0.005f).Within(Tolerance), "melee-rushing must not pay");
    }

    [Test]
    public void TooCloseBoundary_IsExclusive()
    {
        var rewards = DefaultRewards();
        Assert.That(rewards.StepReward(false, false, true, 6f),
            Is.EqualTo(0.001f).Within(Tolerance), "exactly at tooCloseDistance is not 'too close'");
    }

    [Test]
    public void DisabledTooClosePenalty_IgnoresDistance()
    {
        var rewards = DefaultRewards();
        rewards.tooClosePenaltyPerStep = 0f;
        Assert.That(rewards.StepReward(false, false, true, 0.5f),
            Is.EqualTo(0.001f).Within(Tolerance));
    }

    [Test]
    public void FiringWithShotInSight_IsNotPenalized()
    {
        var rewards = DefaultRewards();
        Assert.That(rewards.StepReward(true, didShoot: true, targetInSight: true, distanceToTarget: 15f),
            Is.EqualTo(0.001f).Within(Tolerance));
    }

    [Test]
    public void FiringWithShotWhileBlind_CostsTheWastedShotPenalty()
    {
        var rewards = DefaultRewards();
        Assert.That(rewards.StepReward(true, didShoot: true, targetInSight: false, distanceToTarget: 15f),
            Is.EqualTo(0.001f - 0.05f).Within(Tolerance), "spraying at memories must not pay");
    }

    [Test]
    public void FiringWithoutAShot_IsNotPenalized()
    {
        // Cooldown or never-seen: the trigger was pulled but no bullet left the
        // barrel, so no shot was wasted.
        var rewards = DefaultRewards();
        Assert.That(rewards.StepReward(true, didShoot: false, targetInSight: false, distanceToTarget: 15f),
            Is.EqualTo(0.001f).Within(Tolerance));
    }

    [Test]
    public void HoldingFire_NeverPaysTheWastedShotPenalty()
    {
        // DidShoot holds a stale true from the last step the trigger was
        // pulled, so the fire branch's own choice has to gate the penalty.
        var rewards = DefaultRewards();
        Assert.That(rewards.StepReward(false, didShoot: true, targetInSight: false, distanceToTarget: 15f),
            Is.EqualTo(0.001f).Within(Tolerance));
    }

    [Test]
    public void Penalties_Stack()
    {
        var rewards = DefaultRewards();
        Assert.That(rewards.StepReward(true, didShoot: true, targetInSight: false, distanceToTarget: 2f),
            Is.EqualTo(0.001f - 0.005f - 0.05f).Within(Tolerance));
    }
}
