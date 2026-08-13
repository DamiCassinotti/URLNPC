using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

// EnemyAgent.OnEpisodeBegin must redraw the commanded mode through the director
// on its own body (#65), so a fresh episode doesn't inherit the mode and dwell
// the last one ended on. The director's own dwell/draw/guard rules are pinned
// in ModeDirectorTests / ModeDirectorGuardTests; this is the agent wiring.
public class ModeDirectorResetTests : PlayModeTestBase
{
    EnemyAgent agent;

    // The director is disabled so its autonomous FixedUpdate can't write — the
    // only writer that runs is the explicit ResetState from OnEpisodeBegin, so
    // a mode change is unambiguously the episode-begin redraw. A one-mode pool
    // (Retreat, not the Hunt initialMode) makes the redraw's result exact.
    IEnumerator BuildAgentWithDirector()
    {
        LogAssert.ignoreFailingMessages = true; // respawn logs placement complaints without a NavMesh

        GameObject player = Track(GameObject.CreatePrimitive(PrimitiveType.Capsule));
        player.tag = "Player";
        player.transform.position = new Vector3(0f, 0f, 10f);
        player.AddComponent<Health>();

        GameObject enemyGo = Track(new GameObject("TestEnemyAgent"));
        enemyGo.tag = "NPC";
        enemyGo.SetActive(false);
        enemyGo.AddComponent<NavMeshAgent>().enabled = false;
        agent = enemyGo.AddComponent<EnemyAgent>();
        EnemyBehavior behavior = enemyGo.GetComponent<EnemyBehavior>();
        behavior.enabled = false; // no NavMesh in this fixture: skip Start's spawn
        behavior.target = player.transform;

        // Added while inactive (before Initialize) so the agent caches it.
        // RequireComponent pulls in the ModeChannel the agent reads.
        ModeDirector director = enemyGo.AddComponent<ModeDirector>();
        director.enabled = false;
        director.trainingOnly = false; // no communicator in the test runner
        director.enabledModes = NpcModeMask.Retreat;

        enemyGo.SetActive(true);
        yield return null;
    }

    [UnityTest]
    public IEnumerator EpisodeBegin_RedrawsTheCommandedMode()
    {
        yield return BuildAgentWithDirector();
        ModeChannel channel = agent.GetComponent<ModeChannel>();
        Assert.That(channel.CurrentMode, Is.EqualTo(NpcMode.Hunt),
            "initialMode holds until a writer runs");

        agent.OnEpisodeBegin();

        Assert.That(channel.CurrentMode, Is.EqualTo(NpcMode.Retreat),
            "the episode-begin redraw is the only writer that ran");
    }

    [UnityTest]
    public IEnumerator EpisodeBegin_RestartsTheDwellEvenWhenTheModeIsUnchanged()
    {
        yield return BuildAgentWithDirector();
        ModeChannel channel = agent.GetComponent<ModeChannel>();

        // Land on the mode the one-mode pool will redraw, and let its dwell run.
        channel.SetMode(NpcMode.Retreat);
        yield return new WaitForFixedUpdate();
        Assert.That(channel.TimeInMode, Is.GreaterThan(0f));

        int changes = 0;
        channel.ModeChanged += (_, __) => changes++;
        agent.OnEpisodeBegin();

        Assert.That(changes, Is.EqualTo(1),
            "a reset redraw is a fresh interval even when the mode is unchanged");
        Assert.That(channel.TimeInMode, Is.LessThan(0.05f), "the dwell restarts");
    }
}
