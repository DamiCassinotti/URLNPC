using NUnit.Framework;

// EpisodeProgress: the closing delta and the new-area check the Hunt/Retreat and
// Patrol reward columns are paid from (#44).
public class EpisodeProgressTests
{
    const float Tolerance = 1e-6f;

    static EpisodeProgress Fresh() => new EpisodeProgress { areaCellSize = 6f, maxClosingDelta = 2f };

    [Test]
    public void FirstStep_HasNoBaselineSoItPaysNothing()
    {
        Assert.That(Fresh().Closing(20f), Is.EqualTo(0f).Within(Tolerance));
    }

    [Test]
    public void ClosingIsPositive_OpeningIsNegative()
    {
        var progress = Fresh();
        progress.Closing(20f);
        Assert.That(progress.Closing(19.5f), Is.EqualTo(0.5f).Within(Tolerance));
        Assert.That(progress.Closing(20.5f), Is.EqualTo(-1f).Within(Tolerance));
    }

    [Test]
    public void ATeleportSizedJump_IsCappedNotPaidInFull()
    {
        // Respawns and NavMesh warps move the body further in one step than
        // walking can, and that is not progress.
        var progress = Fresh();
        progress.Closing(40f);
        Assert.That(progress.Closing(5f), Is.EqualTo(2f).Within(Tolerance));
        Assert.That(progress.Closing(45f), Is.EqualTo(-2f).Within(Tolerance));
    }

    [Test]
    public void NoTarget_PaysNothingAndDropsTheBaseline()
    {
        // DistanceToTarget reports infinity with no target; the distance after
        // one reappears is not a step's worth of movement either.
        var progress = Fresh();
        progress.Closing(20f);
        Assert.That(progress.Closing(float.PositiveInfinity), Is.EqualTo(0f).Within(Tolerance));
        Assert.That(progress.Closing(8f), Is.EqualTo(0f).Within(Tolerance));
        Assert.That(progress.Closing(7.5f), Is.EqualTo(0.5f).Within(Tolerance), "and it picks back up from there");
    }

    [Test]
    public void Reset_ForgetsTheBaseline()
    {
        var progress = Fresh();
        progress.Closing(20f);
        progress.Reset();
        Assert.That(progress.Closing(10f), Is.EqualTo(0f).Within(Tolerance),
            "the respawn between episodes is not ground the agent covered");
    }

    [Test]
    public void EachCell_PaysOnce()
    {
        var progress = Fresh();
        Assert.That(progress.EnterArea(0f, 0f), Is.True);
        Assert.That(progress.EnterArea(1f, 1f), Is.False, "still the same patch");
        Assert.That(progress.EnterArea(7f, 0f), Is.True, "a cell over");
        Assert.That(progress.EnterArea(0f, 0f), Is.False, "coming back is not new ground");
        Assert.That(progress.VisitedAreas, Is.EqualTo(2));
    }

    [Test]
    public void CellsDoNotCollideAcrossTheAxes()
    {
        var progress = Fresh();
        Assert.That(progress.EnterArea(7f, 0f), Is.True);
        Assert.That(progress.EnterArea(0f, 7f), Is.True, "(1,0) and (0,1) are different patches");
        Assert.That(progress.EnterArea(-7f, 0f), Is.True, "and so is (-1,0)");
        Assert.That(progress.EnterArea(0f, -7f), Is.True);
        Assert.That(progress.VisitedAreas, Is.EqualTo(4));
    }

    [Test]
    public void Reset_ForgetsWhereItHasBeen()
    {
        var progress = Fresh();
        progress.EnterArea(0f, 0f);
        progress.Reset();
        Assert.That(progress.EnterArea(0f, 0f), Is.True, "every episode explores the arena again");
        Assert.That(progress.VisitedAreas, Is.EqualTo(1));
    }

    [Test]
    public void NonFinitePosition_IsNotNewGround()
    {
        var progress = Fresh();
        Assert.That(progress.EnterArea(float.NaN, 0f), Is.False);
        Assert.That(progress.EnterArea(0f, float.PositiveInfinity), Is.False);
        Assert.That(progress.VisitedAreas, Is.Zero);
    }

    [Test]
    public void ZeroCellSize_DisablesTheAreaReward()
    {
        var progress = new EpisodeProgress { areaCellSize = 0f };
        Assert.That(progress.EnterArea(0f, 0f), Is.False);
        Assert.That(progress.EnterArea(100f, 100f), Is.False);
    }

    [Test]
    public void InNewArea_HoldsForTheWholeCrossing_AndDropsOnTroddenGround()
    {
        // Patrol's compliance rule scores every step of a fresh patch, not the
        // one that entered it (#105).
        var progress = Fresh();
        progress.EnterArea(0f, 0f);
        Assert.That(progress.InNewArea, Is.True);
        progress.EnterArea(1f, 1f);
        Assert.That(progress.InNewArea, Is.True, "still walking through the same fresh patch");

        progress.EnterArea(7f, 0f);
        Assert.That(progress.InNewArea, Is.True, "a cell over, also fresh");
        progress.EnterArea(0f, 0f);
        Assert.That(progress.InNewArea, Is.False, "back on ground it has already covered");
        progress.EnterArea(1f, 1f);
        Assert.That(progress.InNewArea, Is.False, "and it stays false for the whole re-crossing");
    }

    [Test]
    public void Reset_ForgetsTheFreshPatch()
    {
        var progress = Fresh();
        progress.EnterArea(0f, 0f);
        progress.EnterArea(7f, 0f);
        progress.EnterArea(0f, 0f);
        progress.Reset();
        Assert.That(progress.InNewArea, Is.False, "nothing has been entered yet");
        progress.EnterArea(0f, 0f);
        Assert.That(progress.InNewArea, Is.True);
    }

    [Test]
    public void Travelled_MeasuresGroundCoveredSinceTheLastStep()
    {
        var progress = Fresh();
        Assert.That(progress.Travelled(0f, 0f), Is.EqualTo(0f).Within(Tolerance), "no baseline yet");
        Assert.That(progress.Travelled(0.3f, 0.4f), Is.EqualTo(0.5f).Within(Tolerance));
        Assert.That(progress.Travelled(0.3f, 0.4f), Is.EqualTo(0f).Within(Tolerance), "standing still");
    }

    [Test]
    public void Travelled_CapsATeleportAndRecoversFromANonFinitePosition()
    {
        var progress = Fresh();
        progress.Travelled(0f, 0f);
        Assert.That(progress.Travelled(50f, 0f), Is.EqualTo(2f).Within(Tolerance), "a respawn is not ground covered");

        Assert.That(progress.Travelled(float.NaN, 0f), Is.EqualTo(0f).Within(Tolerance));
        Assert.That(progress.Travelled(50f, 0f), Is.EqualTo(0f).Within(Tolerance), "no baseline to measure from");
        Assert.That(progress.Travelled(50.5f, 0f), Is.EqualTo(0.5f).Within(Tolerance), "and it picks back up from there");
    }

    [Test]
    public void Reset_ForgetsTheTravelBaseline()
    {
        var progress = Fresh();
        progress.Travelled(0f, 0f);
        progress.Reset();
        Assert.That(progress.Travelled(30f, 30f), Is.EqualTo(0f).Within(Tolerance));
    }
}
