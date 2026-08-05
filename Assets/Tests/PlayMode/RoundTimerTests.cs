using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// The round clock in GameManager: countdown, re-arming, the draw-on-timeout
/// path (human play — no communicator in tests), and the "clock disabled"
/// configuration. Uses sub-second rounds so the suite stays fast.
/// </summary>
public class RoundTimerTests : PlayModeTestBase
{
    [UnityTest]
    public IEnumerator Clock_CountsDownWhileRoundRuns()
    {
        GameManager gm = CreateGameManager(5f);
        yield return null; // Start arms the clock

        float initial = gm.RemainingRoundTime;
        Assert.That(initial, Is.GreaterThan(4f).And.LessThanOrEqualTo(5f));

        yield return null;
        yield return null;
        Assert.That(gm.RemainingRoundTime, Is.LessThan(initial));
    }

    [UnityTest]
    public IEnumerator ResetRoundClock_RearmsFullDuration()
    {
        GameManager gm = CreateGameManager(5f);
        yield return null;
        yield return new WaitForSeconds(0.1f);
        Assert.That(gm.RemainingRoundTime, Is.LessThan(5f));

        gm.ResetRoundClock();
        Assert.That(gm.RemainingRoundTime, Is.EqualTo(5f));
    }

    [UnityTest]
    public IEnumerator Timeout_RecordsSingleDraw_AndFreezesRound()
    {
        CreateCounterHud();
        GameManager gm = CreateGameManager(0.3f);
        int drawsBefore = CounterData.readDraws();
        int userBefore = CounterData.readUserPoints();
        int npcBefore = CounterData.readNpcPoints();

        // FinishRound zeroes timeScale, so wait on unscaled time.
        float safety = 5f;
        while (Time.timeScale > 0f && safety > 0f)
        {
            safety -= Time.unscaledDeltaTime;
            yield return null;
        }

        Assert.That(Time.timeScale, Is.Zero, "timeout must freeze the scene in human play");
        Assert.That(gm.RemainingRoundTime, Is.Zero);
        Assert.That(CounterData.readDraws(), Is.EqualTo(drawsBefore + 1));
        Assert.That(CounterData.readUserPoints(), Is.EqualTo(userBefore), "a draw must not hand anyone the win");
        Assert.That(CounterData.readNpcPoints(), Is.EqualTo(npcBefore));

        // The finished round must not keep counting draws.
        for (int i = 0; i < 5; i++) yield return null;
        Assert.That(CounterData.readDraws(), Is.EqualTo(drawsBefore + 1));
    }

    [UnityTest]
    public IEnumerator NonPositiveDuration_DisablesTheClock()
    {
        CreateCounterHud();
        GameManager gm = CreateGameManager(0f);
        int drawsBefore = CounterData.readDraws();

        yield return new WaitForSeconds(0.2f);

        Assert.That(Time.timeScale, Is.EqualTo(1f), "no clock, no timeout, no freeze");
        Assert.That(gm.RemainingRoundTime, Is.Zero);
        Assert.That(CounterData.readDraws(), Is.EqualTo(drawsBefore));
    }
}
