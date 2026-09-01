using NUnit.Framework;

// The heuristic-opponent cadence (#109): which training episodes put the player
// side on the scripted bot instead of the shared policy.
public class OpponentScheduleTests
{
    static int CountHeuristic(OpponentSchedule schedule, int episodes)
    {
        int count = 0;
        for (int i = 0; i < episodes; i++)
        {
            if (schedule.NextEpisodeIsHeuristic()) count++;
        }
        return count;
    }

    [Test]
    public void ZeroFractionIsPureSelfPlay()
    {
        Assert.That(CountHeuristic(new OpponentSchedule(0f), 100), Is.Zero);
    }

    [Test]
    public void FullFractionIsAlwaysTheHeuristic()
    {
        Assert.That(CountHeuristic(new OpponentSchedule(1f), 100), Is.EqualTo(100));
    }

    // 0.1 is the case a running float sum drifts under 1 on and drops episodes.
    [TestCase(0.1f, 100)]
    [TestCase(0.25f, 250)]
    [TestCase(0.5f, 500)]
    [TestCase(0.75f, 750)]
    public void ItDeliversTheFractionOverTheRun(float fraction, int expected)
    {
        Assert.That(CountHeuristic(new OpponentSchedule(fraction), 1000), Is.EqualTo(expected));
    }

    [Test]
    public void TheEpisodesAreSpreadOutRatherThanBunched()
    {
        var schedule = new OpponentSchedule(0.25f);
        bool[] first = new bool[8];
        for (int i = 0; i < first.Length; i++) first[i] = schedule.NextEpisodeIsHeuristic();

        Assert.That(first, Is.EqualTo(new[] { false, false, false, true, false, false, false, true }));
    }

    [TestCase(-0.5f, 0f)]
    [TestCase(2f, 1f)]
    public void AnOutOfRangeFractionIsClamped(float given, float expected)
    {
        Assert.That(new OpponentSchedule(given).HeuristicFraction, Is.EqualTo(expected));
    }

    [TestCase("0", 0f)]
    [TestCase("0.25", 0.25f)]
    [TestCase("1", 1f)]
    public void ItParsesAFraction(string value, float expected)
    {
        Assert.That(OpponentSchedule.TryParseFraction(value, out float parsed), Is.True);
        Assert.That(parsed, Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("half")]
    [TestCase("-0.1")]
    [TestCase("1.5")]
    [TestCase("NaN")]
    public void ItRejectsAnythingOutsideZeroToOne(string value)
    {
        Assert.That(OpponentSchedule.TryParseFraction(value, out float parsed), Is.False);
        Assert.That(parsed, Is.Zero);
    }

    [Test]
    public void TheArgumentReadsOffACommandLine()
    {
        string[] args = { "URLNPC.x86_64", "-playerDriver", "agent", OpponentSchedule.FractionArg, "0.3" };

        Assert.That(CommandLineArgs.TryRead(args, OpponentSchedule.FractionArg,
                                            OpponentSchedule.TryParseFraction, out float parsed), Is.True);
        Assert.That(parsed, Is.EqualTo(0.3f));
    }
}
