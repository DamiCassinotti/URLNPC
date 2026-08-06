using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// Win/loss accounting: ProcessDeath maps the loser's tag to the opposite side's
// score and ends the round. No trainer is connected here, so this is the
// human-play path, exactly like pressing Play.
public class GameManagerDeathTests : PlayModeTestBase
{
    [UnityTest]
    public IEnumerator NpcDeath_CountsUserWin_AndFinishesRound()
    {
        CreateCounterHud();
        GameManager gm = CreateGameManager(60f);
        yield return null; // Start wires the counter

        int userBefore = CounterData.readUserPoints();
        int npcBefore = CounterData.readNpcPoints();

        gm.ProcessDeath("NPC");

        Assert.That(CounterData.readUserPoints(), Is.EqualTo(userBefore + 1));
        Assert.That(CounterData.readNpcPoints(), Is.EqualTo(npcBefore));
        Assert.That(Time.timeScale, Is.Zero, "a decided round must freeze the scene");
    }

    [UnityTest]
    public IEnumerator PlayerDeath_CountsNpcWin_AndFinishesRound()
    {
        CreateCounterHud();
        GameManager gm = CreateGameManager(60f);
        yield return null;

        int userBefore = CounterData.readUserPoints();
        int npcBefore = CounterData.readNpcPoints();

        gm.ProcessDeath("Player");

        Assert.That(CounterData.readNpcPoints(), Is.EqualTo(npcBefore + 1));
        Assert.That(CounterData.readUserPoints(), Is.EqualTo(userBefore));
        Assert.That(Time.timeScale, Is.Zero);
    }
}
