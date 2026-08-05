using NUnit.Framework;

/// <summary>
/// The full per-step reward table (CLAUDE.md "Reward shape"), enumerated as
/// pure math: alive bonus, too-close shaping penalty, wasted-shot penalty,
/// and how they combine. Actions: 0=Patrol, 1=Chase, 2=Attack.
/// </summary>
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
        Assert.That(rewards.StepReward(action: 0, didShoot: false, targetInSight: false, distanceToTarget: 20f),
            Is.EqualTo(0.001f).Within(Tolerance));
    }

    [Test]
    public void StandingTooClose_CostsTheShapingPenalty()
    {
        var rewards = DefaultRewards();
        Assert.That(rewards.StepReward(1, false, true, 3f),
            Is.EqualTo(0.001f - 0.005f).Within(Tolerance), "melee-rushing must not pay");
    }

    [Test]
    public void TooCloseBoundary_IsExclusive()
    {
        var rewards = DefaultRewards();
        Assert.That(rewards.StepReward(1, false, true, 6f),
            Is.EqualTo(0.001f).Within(Tolerance), "exactly at tooCloseDistance is not 'too close'");
    }

    [Test]
    public void DisabledTooClosePenalty_IgnoresDistance()
    {
        var rewards = DefaultRewards();
        rewards.tooClosePenaltyPerStep = 0f;
        Assert.That(rewards.StepReward(1, false, true, 0.5f),
            Is.EqualTo(0.001f).Within(Tolerance));
    }

    [Test]
    public void AttackWithShotInSight_IsNotPenalized()
    {
        var rewards = DefaultRewards();
        Assert.That(rewards.StepReward(2, didShoot: true, targetInSight: true, distanceToTarget: 15f),
            Is.EqualTo(0.001f).Within(Tolerance));
    }

    [Test]
    public void AttackWithShotWhileBlind_CostsTheWastedShotPenalty()
    {
        var rewards = DefaultRewards();
        Assert.That(rewards.StepReward(2, didShoot: true, targetInSight: false, distanceToTarget: 15f),
            Is.EqualTo(0.001f - 0.05f).Within(Tolerance), "spraying at memories must not pay");
    }

    [Test]
    public void AttackWithoutAShot_IsNotPenalized()
    {
        // Cooldown or never-seen: the attack action fired no bullet, so no
        // shot was wasted.
        var rewards = DefaultRewards();
        Assert.That(rewards.StepReward(2, didShoot: false, targetInSight: false, distanceToTarget: 15f),
            Is.EqualTo(0.001f).Within(Tolerance));
    }

    [Test]
    public void NonAttackActions_NeverPayTheWastedShotPenalty()
    {
        // DidShoot can hold a stale true from an earlier step; only the
        // attack action can waste a shot.
        var rewards = DefaultRewards();
        Assert.That(rewards.StepReward(0, didShoot: true, targetInSight: false, distanceToTarget: 15f),
            Is.EqualTo(0.001f).Within(Tolerance));
        Assert.That(rewards.StepReward(1, didShoot: true, targetInSight: false, distanceToTarget: 15f),
            Is.EqualTo(0.001f).Within(Tolerance));
    }

    [Test]
    public void Penalties_Stack()
    {
        var rewards = DefaultRewards();
        Assert.That(rewards.StepReward(2, didShoot: true, targetInSight: false, distanceToTarget: 2f),
            Is.EqualTo(0.001f - 0.005f - 0.05f).Within(Tolerance));
    }
}
