using NUnit.Framework;

// The full per-step reward table (CLAUDE.md "Reward shape") enumerated as pure
// math: how the penalties combine, and which rows the commanded mode moves.
public class RewardComputerTests
{
    const float Tolerance = 1e-6f;

    // The documented columns (#44). EnemyAgent's serialized defaults have to
    // match these; the wiring itself is pinned in EnemyAgentTests.
    static RewardComputer DefaultRewards()
    {
        var rewards = new RewardComputer
        {
            aliveRewardPerStep = 0.001f,
            wastedShotPenalty = 0.05f,
            tooClosePenaltyPerStep = 0.005f,
            tooCloseDistance = 6f,
        };
        rewards.modes[NpcMode.Hunt] = new ModeRewardColumn
        {
            hitTarget = 0.5f, gotHit = 0.3f, closingPerMeter = 0.01f,
        };
        rewards.modes[NpcMode.HoldCover] = new ModeRewardColumn
        {
            hitTarget = 0.5f, gotHit = 0.6f, coverPerStep = 0.005f,
        };
        rewards.modes[NpcMode.Retreat] = new ModeRewardColumn
        {
            hitTarget = 0.1f, gotHit = 0.6f, closingPerMeter = -0.01f, coverPerStep = 0.002f,
        };
        rewards.modes[NpcMode.Patrol] = new ModeRewardColumn
        {
            hitTarget = 0.1f, gotHit = 0.5f, newArea = 0.002f,
        };
        return rewards;
    }

    // A step that triggers none of the shaping rows, so a test only has to name
    // what it is about.
    static StepRewardInput Step(NpcMode mode = NpcMode.Hunt)
    {
        return new StepRewardInput { mode = mode, distanceToTarget = 20f };
    }

    [Test]
    public void PlainStep_PaysTheAliveBonus()
    {
        Assert.That(DefaultRewards().StepReward(Step()), Is.EqualTo(0.001f).Within(Tolerance));
    }

    [Test]
    public void StandingTooClose_CostsTheShapingPenalty()
    {
        StepRewardInput step = Step();
        step.distanceToTarget = 3f;
        step.targetInSight = true;
        Assert.That(DefaultRewards().StepReward(step),
            Is.EqualTo(0.001f - 0.005f).Within(Tolerance), "melee-rushing must not pay");
    }

    [Test]
    public void TooCloseBoundary_IsExclusive()
    {
        StepRewardInput step = Step();
        step.distanceToTarget = 6f;
        Assert.That(DefaultRewards().StepReward(step),
            Is.EqualTo(0.001f).Within(Tolerance), "exactly at tooCloseDistance is not 'too close'");
    }

    [Test]
    public void DisabledTooClosePenalty_IgnoresDistance()
    {
        var rewards = DefaultRewards();
        rewards.tooClosePenaltyPerStep = 0f;
        StepRewardInput step = Step();
        step.distanceToTarget = 0.5f;
        Assert.That(rewards.StepReward(step), Is.EqualTo(0.001f).Within(Tolerance));
    }

    [Test]
    public void FiringWithShotInSight_IsNotPenalized()
    {
        StepRewardInput step = Step();
        step.fired = step.didShoot = step.targetInSight = true;
        Assert.That(DefaultRewards().StepReward(step), Is.EqualTo(0.001f).Within(Tolerance));
    }

    [Test]
    public void FiringWithShotWhileBlind_CostsTheWastedShotPenalty()
    {
        StepRewardInput step = Step();
        step.fired = step.didShoot = true;
        Assert.That(DefaultRewards().StepReward(step),
            Is.EqualTo(0.001f - 0.05f).Within(Tolerance), "spraying at memories must not pay");
    }

    [Test]
    public void FiringWithoutAShot_IsNotPenalized()
    {
        // Cooldown or never-seen: the trigger was pulled but no bullet left the
        // barrel, so no shot was wasted.
        StepRewardInput step = Step();
        step.fired = true;
        Assert.That(DefaultRewards().StepReward(step), Is.EqualTo(0.001f).Within(Tolerance));
    }

    [Test]
    public void HoldingFire_NeverPaysTheWastedShotPenalty()
    {
        // DidShoot holds a stale true from the last step the trigger was
        // pulled, so the fire branch's own choice has to gate the penalty.
        StepRewardInput step = Step();
        step.didShoot = true;
        Assert.That(DefaultRewards().StepReward(step), Is.EqualTo(0.001f).Within(Tolerance));
    }

    [Test]
    public void Penalties_Stack()
    {
        StepRewardInput step = Step();
        step.fired = step.didShoot = true;
        step.distanceToTarget = 2f;
        Assert.That(DefaultRewards().StepReward(step),
            Is.EqualTo(0.001f - 0.005f - 0.05f).Within(Tolerance));
    }

    // The rows the commanded mode does not move: same step, every mode.
    [Test]
    public void GlobalRows_DoNotVaryByMode([ValueSource(typeof(NpcModes), nameof(NpcModes.All))] NpcMode mode)
    {
        StepRewardInput step = Step(mode);
        step.fired = step.didShoot = true;
        step.distanceToTarget = 2f;
        Assert.That(DefaultRewards().StepReward(step),
            Is.EqualTo(0.001f - 0.005f - 0.05f).Within(Tolerance));
    }

    [Test]
    public void Hunt_PaysForClosingAndChargesForOpening()
    {
        var rewards = DefaultRewards();
        StepRewardInput closing = Step(NpcMode.Hunt);
        closing.closingDelta = 0.5f;
        Assert.That(rewards.StepReward(closing), Is.EqualTo(0.001f + 0.005f).Within(Tolerance));

        StepRewardInput opening = Step(NpcMode.Hunt);
        opening.closingDelta = -0.5f;
        Assert.That(rewards.StepReward(opening), Is.EqualTo(0.001f - 0.005f).Within(Tolerance));
    }

    [Test]
    public void Retreat_PaysForOpeningInstead()
    {
        var rewards = DefaultRewards();
        StepRewardInput opening = Step(NpcMode.Retreat);
        opening.closingDelta = -0.5f;
        Assert.That(rewards.StepReward(opening), Is.EqualTo(0.001f + 0.005f).Within(Tolerance));

        StepRewardInput closing = Step(NpcMode.Retreat);
        closing.closingDelta = 0.5f;
        Assert.That(rewards.StepReward(closing), Is.EqualTo(0.001f - 0.005f).Within(Tolerance));
    }

    [Test]
    public void HoldCoverAndPatrol_AreIndifferentToDistance(
        [Values(NpcMode.HoldCover, NpcMode.Patrol)] NpcMode mode)
    {
        StepRewardInput step = Step(mode);
        step.closingDelta = 2f;
        Assert.That(DefaultRewards().StepReward(step), Is.EqualTo(0.001f).Within(Tolerance));
    }

    [Test]
    public void BreakingLineOfSight_PaysOnlyTheCoverModes()
    {
        var rewards = DefaultRewards();
        Assert.That(Hidden(rewards, NpcMode.HoldCover), Is.EqualTo(0.001f + 0.005f).Within(Tolerance));
        Assert.That(Hidden(rewards, NpcMode.Retreat), Is.EqualTo(0.001f + 0.002f).Within(Tolerance));
        Assert.That(Hidden(rewards, NpcMode.Hunt), Is.EqualTo(0.001f).Within(Tolerance));
        Assert.That(Hidden(rewards, NpcMode.Patrol), Is.EqualTo(0.001f).Within(Tolerance));
    }

    static float Hidden(RewardComputer rewards, NpcMode mode)
    {
        StepRewardInput step = Step(mode);
        step.hiddenFromTarget = true;
        return rewards.StepReward(step);
    }

    [Test]
    public void NewGround_PaysOnlyPatrol([ValueSource(typeof(NpcModes), nameof(NpcModes.All))] NpcMode mode)
    {
        StepRewardInput step = Step(mode);
        step.enteredNewArea = true;
        float expected = mode == NpcMode.Patrol ? 0.001f + 0.002f : 0.001f;
        Assert.That(DefaultRewards().StepReward(step), Is.EqualTo(expected).Within(Tolerance));
    }

    // The flag that lets the agent skip the cover raycast must never disagree
    // with the column it is standing in for.
    [Test]
    public void RewardsCover_MatchesTheColumn([ValueSource(typeof(NpcModes), nameof(NpcModes.All))] NpcMode mode)
    {
        var rewards = DefaultRewards();
        Assert.That(rewards.RewardsCover(mode), Is.EqualTo(rewards.modes[mode].coverPerStep != 0f));
    }

    [Test]
    public void HitAndGotHitColumns_FollowTheDocumentedTable()
    {
        var rewards = DefaultRewards();
        Assert.That(rewards.modes[NpcMode.Hunt].hitTarget, Is.EqualTo(0.5f).Within(Tolerance));
        Assert.That(rewards.modes[NpcMode.Retreat].hitTarget, Is.EqualTo(0.1f).Within(Tolerance),
            "a retreating NPC is not there to trade shots");
        Assert.That(rewards.modes[NpcMode.Hunt].gotHit, Is.EqualTo(0.3f).Within(Tolerance));
        Assert.That(rewards.modes[NpcMode.HoldCover].gotHit, Is.EqualTo(0.6f).Within(Tolerance),
            "being hit while the job is to stay covered has to cost more");
    }
}
