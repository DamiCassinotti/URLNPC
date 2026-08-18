using NUnit.Framework;

// ModeTally: the per-mode step counter behind the compliance tally (#45) and the
// target-in-sight fraction (#87). The compliance rules themselves are pinned in
// ModeComplianceTrackerTests.
public class ModeTallyTests
{
    [Test]
    public void RateIsPerMode_AndModesDoNotBleedIntoEachOther()
    {
        var tally = new ModeTally();
        Record(tally, NpcMode.Hunt, hit: true, times: 3);
        Record(tally, NpcMode.Hunt, hit: false, times: 1);
        Record(tally, NpcMode.Retreat, hit: true, times: 1);

        Assert.That(tally.Steps(NpcMode.Hunt), Is.EqualTo(4));
        Assert.That(tally.Hits(NpcMode.Hunt), Is.EqualTo(3));
        Assert.That(tally.Rate(NpcMode.Hunt), Is.EqualTo(0.75f).Within(1e-6f));
        Assert.That(tally.Rate(NpcMode.Retreat), Is.EqualTo(1f).Within(1e-6f));
        Assert.That(tally.TotalSteps, Is.EqualTo(5));
    }

    [Test]
    public void AModeNeverCommanded_HasNoStepsAndNoRate()
    {
        var tally = new ModeTally();
        Record(tally, NpcMode.Hunt, hit: true, times: 1);

        Assert.That(tally.Steps(NpcMode.Patrol), Is.Zero);
        Assert.That(tally.Rate(NpcMode.Patrol), Is.EqualTo(0f), "no steps is reported as zero, not as a division by zero");
        Assert.That(tally.Json("visible", "visible_steps"), Does.Not.Contain("Patrol"));
    }

    [Test]
    public void AModeThatNeverHit_IsStillReported()
    {
        // The whole point of the visibility row: a mode whose steps the target
        // was never visible in has to show up as 0-of-N, not go missing.
        var tally = new ModeTally();
        Record(tally, NpcMode.Retreat, hit: false, times: 3);

        Assert.That(tally.Json("visible", "visible_steps"), Is.EqualTo(
            "\"visible\":{\"Retreat\":{\"steps\":3,\"visible_steps\":0,\"rate\":0}}"));
    }

    [Test]
    public void Json_UsesTheCallersKeys_AndReportsEveryCommandedMode()
    {
        var tally = new ModeTally();
        Record(tally, NpcMode.Hunt, hit: true, times: 3);
        Record(tally, NpcMode.Hunt, hit: false, times: 1);
        Record(tally, NpcMode.Retreat, hit: true, times: 2);

        Assert.That(tally.Json("visible", "visible_steps"), Is.EqualTo(
            "\"visible\":{\"Hunt\":{\"steps\":4,\"visible_steps\":3,\"rate\":0.75},"
            + "\"Retreat\":{\"steps\":2,\"visible_steps\":2,\"rate\":1}}"));
    }

    [Test]
    public void Reset_StartsTheNextEpisodeEmpty()
    {
        var tally = new ModeTally();
        Record(tally, NpcMode.Hunt, hit: true, times: 2);
        tally.Reset();

        Assert.That(tally.TotalSteps, Is.Zero);
        Assert.That(tally.Steps(NpcMode.Hunt), Is.Zero);
        Assert.That(tally.Hits(NpcMode.Hunt), Is.Zero);
        Assert.That(tally.Json("visible", "visible_steps"), Is.EqualTo("\"visible\":{}"));
    }

    static void Record(ModeTally tally, NpcMode mode, bool hit, int times)
    {
        for (int i = 0; i < times; i++) tally.Record(mode, hit);
    }
}
