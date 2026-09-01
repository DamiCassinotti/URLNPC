using System.Collections;
using NUnit.Framework;
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
}
