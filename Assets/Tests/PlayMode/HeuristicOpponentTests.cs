using System.Collections;
using System.IO;
using NUnit.Framework;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

// PlayerAgent swaps the player side onto the scripted heuristic for a fraction
// of the training episodes (#109). The cadence itself is OpponentScheduleTests;
// this is the training gate, which is what keeps the swap out of eval and human
// play — an eval session picks each side's policy itself, and a mid-run swap
// there would silently change what is being scored.
public class HeuristicOpponentTests : PlayModeTestBase
{
    PlayerAgent agent;

    IEnumerator BuildPlayerAgent()
    {
        LogAssert.ignoreFailingMessages = true; // respawn logs placement complaints without a NavMesh

        GameObject npc = Track(GameObject.CreatePrimitive(PrimitiveType.Capsule));
        npc.tag = "NPC";
        npc.transform.position = new Vector3(0f, 0f, 10f);
        npc.AddComponent<Health>();

        GameObject playerGo = Track(new GameObject("TestPlayerAgent"));
        playerGo.SetActive(false);
        playerGo.AddComponent<NavMeshAgent>().enabled = false;
        agent = playerGo.AddComponent<PlayerAgent>();
        EnemyBehavior behavior = playerGo.GetComponent<EnemyBehavior>();
        behavior.enabled = false; // no NavMesh in this fixture: skip Start's spawn
        behavior.targetTag = "NPC";
        behavior.target = npc.transform;

        playerGo.SetActive(true);
        yield return null;
    }

    [UnityTest]
    public IEnumerator OutsideTraining_TheOpponentPolicyIsLeftAlone()
    {
        yield return BuildPlayerAgent();
        var parameters = agent.GetComponent<BehaviorParameters>();
        parameters.BehaviorType = BehaviorType.Default;
        // Every episode would be a heuristic one if the gate weren't there.
        agent.schedule = new OpponentSchedule(1f);

        agent.OnEpisodeBegin();

        Assert.That(parameters.BehaviorType, Is.EqualTo(BehaviorType.Default),
            "no communicator: nothing is learning from this body, so its policy is not ours to swap");
    }

    // The test runner has no trainer to connect, so training is stubbed; the
    // real gate is the negative test above.
    [UnityTest]
    public IEnumerator InTraining_TheScheduledEpisodesRunTheHeuristic()
    {
        yield return BuildPlayerAgent();
        var parameters = agent.GetComponent<BehaviorParameters>();
        parameters.BehaviorType = BehaviorType.Default;
        agent.isTraining = () => true;
        agent.schedule = new OpponentSchedule(0.5f); // every other episode

        agent.OnEpisodeBegin();
        Assert.That(parameters.BehaviorType, Is.EqualTo(BehaviorType.Default), "episode 1 is a self-play one");

        agent.OnEpisodeBegin();
        Assert.That(parameters.BehaviorType, Is.EqualTo(BehaviorType.HeuristicOnly), "episode 2 is the scripted bot");

        agent.OnEpisodeBegin();
        Assert.That(parameters.BehaviorType, Is.EqualTo(BehaviorType.Default), "and back to the shared policy");
    }

    // The heuristic never reads the commanded mode, but the director goes on
    // commanding one — so a per-mode row from such an episode would be labelled
    // with a mode that drove nothing.
    [UnityTest]
    public IEnumerator AHeuristicEpisode_ReportsNoPerModeRows()
    {
        TelemetryLogger logger = TelemetryLogger.Instance;
        if (logger == null) Assert.Ignore("no TelemetryLogger bootstrapped in this run");
        var captured = new StringWriter();
        TextWriter sessionWriter = logger.SwapWriter(captured);
        try
        {
            yield return BuildPlayerAgent();
            agent.isTraining = () => true;
            agent.schedule = new OpponentSchedule(1f); // every episode is the bot

            agent.OnEpisodeBegin();
            agent.GetComponent<ModeChannel>().SetMode(NpcMode.Retreat);
            Step(MovementAction.Advance);
            agent.OnRoundTimeout();

            Assert.That(captured.ToString(), Does.Not.Contain("mode_change"));
            Assert.That(captured.ToString(), Does.Not.Contain("mode_compliance"));
        }
        finally
        {
            logger.SwapWriter(sessionWriter);
            captured.Dispose();
        }
    }

    void Step(MovementAction movement)
    {
        var discrete = new ActionSegment<int>(new[] { (int)movement, NpcBrainSpec.DontFire });
        agent.OnActionReceived(new ActionBuffers(ActionSegment<float>.Empty, discrete));
    }
}
