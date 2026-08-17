using NUnit.Framework;

// The full per-step reward table (CLAUDE.md "Reward shape") enumerated as pure
// math: how the penalties combine, and which rows the commanded mode moves.
public class RewardComputerTests
{
    const float Tolerance = 1e-6f;

    // The documented per-step rows, named so the arithmetic below reads as
    // "alive bonus minus penalty" rather than as bare numbers.
    const float Alive = 0.0002f;
    const float WastedShot = 0.05f;

    // The documented columns (#44). EnemyAgent's serialized defaults have to
    // match these; the wiring itself is pinned in EnemyAgentTests.
    static RewardComputer DefaultRewards()
    {
        var rewards = new RewardComputer
        {
            aliveRewardPerStep = Alive,
            wastedShotPenalty = WastedShot,
            tooCloseDistance = 6f,
        };
        rewards.modes[NpcMode.Hunt] = new ModeRewardColumn
        {
            hitTarget = 0.5f, gotHit = 0.3f, closingPerMeter = 0.01f, tooClosePerStep = 0f,
        };
        rewards.modes[NpcMode.HoldCover] = new ModeRewardColumn
        {
            hitTarget = 0.5f, gotHit = 0.6f, coverPerStep = 0.005f, tooClosePerStep = 0.004f,
        };
        rewards.modes[NpcMode.Retreat] = new ModeRewardColumn
        {
            hitTarget = 0.1f, gotHit = 0.6f, closingPerMeter = -0.01f, coverPerStep = 0.002f,
            tooClosePerStep = 0.008f,
        };
        rewards.modes[NpcMode.Patrol] = new ModeRewardColumn
        {
            hitTarget = 0.1f, gotHit = 0.5f, newArea = 0.002f, tooClosePerStep = 0.002f,
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
        Assert.That(DefaultRewards().StepReward(Step()), Is.EqualTo(Alive).Within(Tolerance));
    }

    // #80: the stand-off each mode wants is the mode's own column.
    [Test]
    public void StandingTooClose_CostsTheCommandedModesPenalty(
        [Values(NpcMode.HoldCover, NpcMode.Retreat, NpcMode.Patrol)] NpcMode mode)
    {
        var rewards = DefaultRewards();
        StepRewardInput step = Step(mode);
        step.distanceToTarget = 3f;
        step.targetInSight = true;
        Assert.That(rewards.StepReward(step),
            Is.EqualTo(Alive - rewards.modes[mode].tooClosePerStep).Within(Tolerance));
    }

    [Test]
    public void Hunt_IsFreeToClose()
    {
        StepRewardInput step = Step(NpcMode.Hunt);
        step.distanceToTarget = 1f;
        step.targetInSight = true;
        Assert.That(DefaultRewards().StepReward(step), Is.EqualTo(Alive).Within(Tolerance),
            "a hunting NPC must not be taxed for doing its job");
    }

    [Test]
    public void TooCloseBoundary_IsExclusive()
    {
        StepRewardInput step = Step(NpcMode.Retreat);
        step.distanceToTarget = 6f;
        Assert.That(DefaultRewards().StepReward(step),
            Is.EqualTo(Alive).Within(Tolerance), "exactly at tooCloseDistance is not 'too close'");
    }

    [Test]
    public void DisabledTooClosePenalty_IgnoresDistance()
    {
        var rewards = DefaultRewards();
        ModeRewardColumn retreat = rewards.modes[NpcMode.Retreat];
        retreat.tooClosePerStep = 0f;
        rewards.modes[NpcMode.Retreat] = retreat;
        StepRewardInput step = Step(NpcMode.Retreat);
        step.distanceToTarget = 0.5f;
        Assert.That(rewards.StepReward(step), Is.EqualTo(Alive).Within(Tolerance));
    }

    [Test]
    public void FiringWithShotInSight_IsNotPenalized()
    {
        StepRewardInput step = Step();
        step.fired = step.didShoot = step.targetInSight = true;
        Assert.That(DefaultRewards().StepReward(step), Is.EqualTo(Alive).Within(Tolerance));
    }

    [Test]
    public void FiringWithShotWhileBlind_CostsTheWastedShotPenalty()
    {
        StepRewardInput step = Step();
        step.fired = step.didShoot = true;
        Assert.That(DefaultRewards().StepReward(step),
            Is.EqualTo(Alive - WastedShot).Within(Tolerance), "spraying at memories must not pay");
    }

    [Test]
    public void FiringWithoutAShot_IsNotPenalized()
    {
        // Cooldown or never-seen: the trigger was pulled but no bullet left the
        // barrel, so no shot was wasted.
        StepRewardInput step = Step();
        step.fired = true;
        Assert.That(DefaultRewards().StepReward(step), Is.EqualTo(Alive).Within(Tolerance));
    }

    [Test]
    public void HoldingFire_NeverPaysTheWastedShotPenalty()
    {
        // DidShoot holds a stale true from the last step the trigger was
        // pulled, so the fire branch's own choice has to gate the penalty.
        StepRewardInput step = Step();
        step.didShoot = true;
        Assert.That(DefaultRewards().StepReward(step), Is.EqualTo(Alive).Within(Tolerance));
    }

    [Test]
    public void Penalties_Stack()
    {
        var rewards = DefaultRewards();
        StepRewardInput step = Step(NpcMode.Retreat);
        step.fired = step.didShoot = true;
        step.distanceToTarget = 2f;
        Assert.That(rewards.StepReward(step),
            Is.EqualTo(Alive - rewards.modes[NpcMode.Retreat].tooClosePerStep - WastedShot).Within(Tolerance));
    }

    // The rows the commanded mode does not move: same step, every mode.
    [Test]
    public void GlobalRows_DoNotVaryByMode([ValueSource(typeof(NpcModes), nameof(NpcModes.All))] NpcMode mode)
    {
        StepRewardInput step = Step(mode);
        step.fired = step.didShoot = true;
        Assert.That(DefaultRewards().StepReward(step),
            Is.EqualTo(Alive - WastedShot).Within(Tolerance));
    }

    [Test]
    public void Hunt_PaysForClosingAndChargesForOpening()
    {
        var rewards = DefaultRewards();
        StepRewardInput closing = Step(NpcMode.Hunt);
        closing.closingDelta = 0.5f;
        Assert.That(rewards.StepReward(closing), Is.EqualTo(Alive + 0.005f).Within(Tolerance));

        StepRewardInput opening = Step(NpcMode.Hunt);
        opening.closingDelta = -0.5f;
        Assert.That(rewards.StepReward(opening), Is.EqualTo(Alive - 0.005f).Within(Tolerance));
    }

    [Test]
    public void Retreat_PaysForOpeningInstead()
    {
        var rewards = DefaultRewards();
        StepRewardInput opening = Step(NpcMode.Retreat);
        opening.closingDelta = -0.5f;
        Assert.That(rewards.StepReward(opening), Is.EqualTo(Alive + 0.005f).Within(Tolerance));

        StepRewardInput closing = Step(NpcMode.Retreat);
        closing.closingDelta = 0.5f;
        Assert.That(rewards.StepReward(closing), Is.EqualTo(Alive - 0.005f).Within(Tolerance));
    }

    [Test]
    public void HoldCoverAndPatrol_AreIndifferentToDistance(
        [Values(NpcMode.HoldCover, NpcMode.Patrol)] NpcMode mode)
    {
        StepRewardInput step = Step(mode);
        step.closingDelta = 2f;
        Assert.That(DefaultRewards().StepReward(step), Is.EqualTo(Alive).Within(Tolerance));
    }

    [Test]
    public void BreakingLineOfSight_PaysOnlyTheCoverModes()
    {
        var rewards = DefaultRewards();
        Assert.That(Hidden(rewards, NpcMode.HoldCover), Is.EqualTo(Alive + 0.005f).Within(Tolerance));
        Assert.That(Hidden(rewards, NpcMode.Retreat), Is.EqualTo(Alive + 0.002f).Within(Tolerance));
        Assert.That(Hidden(rewards, NpcMode.Hunt), Is.EqualTo(Alive).Within(Tolerance));
        Assert.That(Hidden(rewards, NpcMode.Patrol), Is.EqualTo(Alive).Within(Tolerance));
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
        float expected = mode == NpcMode.Patrol ? Alive + 0.002f : Alive;
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

    // #79: full-02 converged to running the clock out because a whole round of
    // alive bonus out-earned a kill. The terminal rewards live on EnemyAgent,
    // so they are repeated here the way the columns above are.
    [Test]
    public void StallingAWholeRound_PaysLessThanWinningOne()
    {
        const float KillTarget = 1.0f;
        const float Timeout = 0.6f;
        // 120 s round at 50 Hz with a decision period of 5.
        const int decisionStepsPerRound = 1200;

        float stalled = DefaultRewards().StepReward(Step()) * decisionStepsPerRound - Timeout;
        Assert.That(stalled, Is.LessThan(KillTarget),
            "a draw must never be worth more than a kill, or the policy learns to stall");
        Assert.That(stalled, Is.LessThan(0f), "and running the clock out must be a net loss");
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

    [Test]
    public void TooCloseColumn_FollowsTheDocumentedTable()
    {
        var rewards = DefaultRewards();
        Assert.That(rewards.modes[NpcMode.Hunt].tooClosePerStep, Is.EqualTo(0f).Within(Tolerance));
        Assert.That(rewards.modes[NpcMode.Retreat].tooClosePerStep,
            Is.GreaterThan(rewards.modes[NpcMode.HoldCover].tooClosePerStep),
            "opening distance is Retreat's job, so crowding must cost it the most");
        Assert.That(rewards.modes[NpcMode.HoldCover].tooClosePerStep,
            Is.GreaterThan(rewards.modes[NpcMode.Patrol].tooClosePerStep));
    }
}
