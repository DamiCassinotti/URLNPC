using NUnit.Framework;

// ModeMeanTally: the per-mode mean of a signed per-step value, behind the range
// delta reported per episode (#120). The sign convention it is fed under lives
// in EnemyAgent; here it only has to average what it is given.
public class ModeMeanTallyTests
{
    [Test]
    public void MeanAndTotal_ArePerMode_AndModesDoNotBleedIntoEachOther()
    {
        var tally = new ModeMeanTally();
        tally.Record(NpcMode.Hunt, -0.2f);
        tally.Record(NpcMode.Hunt, -0.4f);
        tally.Record(NpcMode.Retreat, 0.5f);

        Assert.That(tally.Steps(NpcMode.Hunt), Is.EqualTo(2));
        Assert.That(tally.Total(NpcMode.Hunt), Is.EqualTo(-0.6f).Within(1e-5f));
        Assert.That(tally.Mean(NpcMode.Hunt), Is.EqualTo(-0.3f).Within(1e-5f));
        Assert.That(tally.Mean(NpcMode.Retreat), Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(tally.TotalSteps, Is.EqualTo(3));
    }

    [Test]
    public void OpposedSteps_CancelInsteadOfSummingTheirMagnitudes()
    {
        // The whole point of reporting it signed: a body that walked in and back
        // out again netted no range, where an unsigned metric would call it the
        // busiest mode on the board.
        var tally = new ModeMeanTally();
        tally.Record(NpcMode.HoldCover, -1f);
        tally.Record(NpcMode.HoldCover, 1f);

        Assert.That(tally.Total(NpcMode.HoldCover), Is.EqualTo(0f).Within(1e-5f));
        Assert.That(tally.Mean(NpcMode.HoldCover), Is.EqualTo(0f).Within(1e-5f));
        Assert.That(tally.Steps(NpcMode.HoldCover), Is.EqualTo(2), "the steps still happened");
    }

    [Test]
    public void IneligibleSteps_CountIntoTheTotalButNotTheEligibleOne()
    {
        // Unlike ModeTally, the two figures sit side by side: a pursuit closes
        // most of its range with the target out of sight, so dropping those
        // steps from the primary total would drop the charge it measures.
        var tally = new ModeMeanTally();
        tally.Record(NpcMode.Hunt, -1f, stepIsEligible: false);
        tally.Record(NpcMode.Hunt, -1f, stepIsEligible: false);
        tally.Record(NpcMode.Hunt, -0.5f, stepIsEligible: true);

        Assert.That(tally.Steps(NpcMode.Hunt), Is.EqualTo(3));
        Assert.That(tally.Total(NpcMode.Hunt), Is.EqualTo(-2.5f).Within(1e-5f));
        Assert.That(tally.EligibleSteps(NpcMode.Hunt), Is.EqualTo(1));
        Assert.That(tally.EligibleTotal(NpcMode.Hunt), Is.EqualTo(-0.5f).Within(1e-5f));
        Assert.That(tally.EligibleMean(NpcMode.Hunt), Is.EqualTo(-0.5f).Within(1e-5f));
    }

    [Test]
    public void AModeWithNoEligibleStep_ReportsZeroRatherThanDividingByIt()
    {
        var tally = new ModeMeanTally();
        tally.Record(NpcMode.Patrol, -0.3f, stepIsEligible: false);

        Assert.That(tally.EligibleSteps(NpcMode.Patrol), Is.Zero);
        Assert.That(tally.EligibleTotal(NpcMode.Patrol), Is.EqualTo(0f));
        Assert.That(tally.EligibleMean(NpcMode.Patrol), Is.EqualTo(0f));
        Assert.That(tally.Mean(NpcMode.Patrol), Is.EqualTo(-0.3f).Within(1e-5f),
            "the step still counts where it is not gated on visibility");
    }

    [Test]
    public void AModeNeverCommanded_HasNoStepsAndNoMean()
    {
        var tally = new ModeMeanTally();
        tally.Record(NpcMode.Hunt, -0.1f);

        Assert.That(tally.Steps(NpcMode.Patrol), Is.Zero);
        Assert.That(tally.Mean(NpcMode.Patrol), Is.EqualTo(0f), "no steps is reported as zero, not as a division by zero");
        Assert.That(tally.Json("closing"), Does.Not.Contain("Patrol"));
    }

    [Test]
    public void ManySmallSteps_KeepTheirSum()
    {
        // An episode is hundreds of steps of a few centimetres, which is what
        // the sum is accumulated in double for.
        var tally = new ModeMeanTally();
        for (int i = 0; i < 1000; i++) tally.Record(NpcMode.Hunt, -0.01f);

        Assert.That(tally.Total(NpcMode.Hunt), Is.EqualTo(-10f).Within(1e-3f));
        Assert.That(tally.Mean(NpcMode.Hunt), Is.EqualTo(-0.01f).Within(1e-6f));
    }

    [Test]
    public void Json_ReportsEveryCommandedModeUnderTheCallersKey()
    {
        var tally = new ModeMeanTally();
        tally.Record(NpcMode.Hunt, -0.25f, stepIsEligible: true);
        tally.Record(NpcMode.Hunt, -0.75f, stepIsEligible: false);
        tally.Record(NpcMode.Retreat, 0.5f, stepIsEligible: true);

        Assert.That(tally.Json("closing"), Is.EqualTo(
            "\"closing\":{\"Hunt\":{\"steps\":2,\"total\":-1,\"mean\":-0.5,"
            + "\"eligible\":1,\"eligibleTotal\":-0.25,\"eligibleMean\":-0.25},"
            + "\"Retreat\":{\"steps\":1,\"total\":0.5,\"mean\":0.5,"
            + "\"eligible\":1,\"eligibleTotal\":0.5,\"eligibleMean\":0.5}}"));
    }

    [Test]
    public void Reset_StartsTheNextEpisodeEmpty()
    {
        var tally = new ModeMeanTally();
        tally.Record(NpcMode.Hunt, -0.5f);
        tally.Reset();

        Assert.That(tally.TotalSteps, Is.Zero);
        Assert.That(tally.Steps(NpcMode.Hunt), Is.Zero);
        Assert.That(tally.Total(NpcMode.Hunt), Is.EqualTo(0f));
        Assert.That(tally.EligibleSteps(NpcMode.Hunt), Is.Zero);
        Assert.That(tally.EligibleTotal(NpcMode.Hunt), Is.EqualTo(0f));
        Assert.That(tally.Json("closing"), Is.EqualTo("\"closing\":{}"));
    }
}
