using System.Collections;
using NUnit.Framework;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

/// <summary>
/// The reward/episode plumbing of EnemyAgent against a live Academy (no
/// trainer connected — same as pressing Play): health events map to the
/// documented reward shape, and both terminal paths (timeout, target death)
/// end the episode and reset state. Also a wiring smoke test for the actual
/// Enemy prefab, whose BehaviorParameters/DecisionRequester are required for
/// ML-Agents to drive it at all (see CLAUDE.md).
/// </summary>
public class EnemyAgentTests : PlayModeTestBase
{
    GameObject player;
    Health playerHealth;
    EnemyAgent agent;
    Health selfHealth;

    IEnumerator BuildAgentScene()
    {
        player = Track(GameObject.CreatePrimitive(PrimitiveType.Capsule));
        player.name = "TestPlayer";
        player.tag = "Player";
        player.transform.position = new Vector3(0f, 0f, 10f);
        playerHealth = player.AddComponent<Health>();

        GameObject enemyGo = Track(new GameObject("TestEnemyAgent"));
        enemyGo.SetActive(false);
        enemyGo.AddComponent<NavMeshAgent>().enabled = false;
        agent = enemyGo.AddComponent<EnemyAgent>(); // RequireComponent pulls in EnemyBehavior + Health (+ BehaviorParameters)
        var behavior = enemyGo.GetComponent<EnemyBehavior>();
        behavior.enabled = false; // no NavMesh in this fixture: skip Start's spawn
        behavior.target = player.transform;
        selfHealth = enemyGo.GetComponent<Health>();
        enemyGo.SetActive(true); // Agent.OnEnable → Academy init → Initialize()
        yield return null;       // EnemyAgent.Update subscribes to the target's Health
    }

    [UnityTest]
    public IEnumerator HealthEvents_MapToTheDocumentedRewardShape()
    {
        yield return BuildAgentScene();

        // No DecisionRequester here, so no per-step rewards muddy the water:
        // the cumulative reward moves only on health events.
        float baseline = agent.GetCumulativeReward();

        playerHealth.DecreaseHealth(10f);
        Assert.That(agent.GetCumulativeReward() - baseline, Is.EqualTo(0.5f).Within(1e-4f),
            "hitting the target must pay hitTargetReward (+0.5)");

        selfHealth.DecreaseHealth(10f);
        Assert.That(agent.GetCumulativeReward() - baseline, Is.EqualTo(0f).Within(1e-4f),
            "getting hit must cost gotHitPenalty (-0.5)");
    }

    [UnityTest]
    public IEnumerator RoundTimeout_EndsTheEpisode()
    {
        yield return BuildAgentScene();
        // The episode reset respawns the enemy; without a NavMesh that logs
        // placement complaints, which are expected in this fixture.
        LogAssert.ignoreFailingMessages = true;

        int episodes = agent.CompletedEpisodes;
        agent.OnRoundTimeout();
        Assert.That(agent.CompletedEpisodes, Is.EqualTo(episodes + 1));
    }

    [UnityTest]
    public IEnumerator TargetDeath_EndsTheEpisode_AndResetsBothHealths()
    {
        yield return BuildAgentScene();
        LogAssert.ignoreFailingMessages = true;

        selfHealth.DecreaseHealth(30f); // dent the enemy so the reset is observable
        int episodes = agent.CompletedEpisodes;

        playerHealth.DecreaseHealth(playerHealth.maxHealth + 1f);

        Assert.That(agent.CompletedEpisodes, Is.EqualTo(episodes + 1), "killing the target must end the episode");
        Assert.That(playerHealth.health, Is.EqualTo(playerHealth.maxHealth), "OnEpisodeBegin must reset the target's health");
        Assert.That(selfHealth.health, Is.EqualTo(selfHealth.maxHealth), "OnEpisodeBegin must reset the enemy's health");
    }

#if UNITY_EDITOR
    [UnityTest]
    public IEnumerator EnemyPrefab_CarriesMlAgentsWiring_AndTheClockOwnsTimeout()
    {
        // The prefab spawns without a NavMesh here and may carry a stale
        // serialized MaxStep (which Initialize overrides with a warning).
        LogAssert.ignoreFailingMessages = true;

        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Characters/Enemy.prefab");
        Assert.That(prefab, Is.Not.Null, "Enemy prefab not found at the path CLAUDE.md documents");

        player = Track(GameObject.CreatePrimitive(PrimitiveType.Capsule));
        player.tag = "Player";
        player.transform.position = new Vector3(0f, 0f, 30f); // out of sight range: the enemy just patrols
        player.AddComponent<Health>();

        GameObject enemy = Track(Object.Instantiate(prefab, new Vector3(0f, 0f, -30f), Quaternion.identity));
        yield return null;
        yield return null;

        var prefabAgent = enemy.GetComponent<EnemyAgent>();
        Assert.That(prefabAgent, Is.Not.Null);
        Assert.That(prefabAgent.MaxStep, Is.Zero,
            "the round clock is the single owner of time-based episode termination");

        var behaviorParams = enemy.GetComponent<BehaviorParameters>();
        Assert.That(behaviorParams, Is.Not.Null, "no BehaviorParameters — ML-Agents cannot drive the enemy");
        Assert.That(behaviorParams.BehaviorName, Is.EqualTo("URLNPC"));
        Assert.That(behaviorParams.BrainParameters.VectorObservationSize, Is.EqualTo(3));
        Assert.That(behaviorParams.BrainParameters.ActionSpec.BranchSizes, Is.EqualTo(new[] { 3 }));

        Assert.That(enemy.GetComponent<DecisionRequester>(), Is.Not.Null,
            "no DecisionRequester — the enemy would stand still in every mode");
    }
#endif
}
