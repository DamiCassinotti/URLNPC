using System.Collections;
using NUnit.Framework;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

// The reward/episode plumbing of EnemyAgent against a live Academy with no
// trainer connected, same as pressing Play. Also a wiring smoke test for the
// real Enemy prefab, whose BehaviorParameters/DecisionRequester are what let
// ML-Agents drive it at all (see CLAUDE.md).
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
    public IEnumerator StaleSerializedBrainShape_IsCorrectedBeforeTheActuatorsAreBuilt()
    {
        // The binary FPS scene pins its Enemy prefab instance to the old 3-float
        // one-branch shape, and no edit to Enemy.prefab can reach that override.
        LogAssert.ignoreFailingMessages = true;

        GameObject stale = Track(new GameObject("StaleEnemy"));
        stale.SetActive(false);
        stale.AddComponent<NavMeshAgent>().enabled = false;
        var brain = stale.AddComponent<BehaviorParameters>();
        brain.BrainParameters.VectorObservationSize = 3;
        brain.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(3);
        var staleAgent = stale.AddComponent<EnemyAgent>();
        stale.GetComponent<EnemyBehavior>().enabled = false; // no NavMesh in this fixture
        stale.SetActive(true);
        yield return null;

        Assert.That(brain.BrainParameters.VectorObservationSize, Is.EqualTo(NpcBrainSpec.ObservationSize));
        Assert.That(brain.BrainParameters.ActionSpec.BranchSizes,
            Is.EqualTo(new[] { NpcBrainSpec.MovementBranchSize, NpcBrainSpec.FireBranchSize }));
        Assert.That(staleAgent.GetStoredActionBuffers().DiscreteActions.Length, Is.EqualTo(2),
            "the actuators are built from the action spec before Initialize runs, so the fix has to land in Awake");
    }

    // The layout itself is pinned in NpcObservationsTests; this is the wiring —
    // that the real PerceptionMemory / DamageMemory / ModeChannel on the body
    // are what the frozen slots are filled from.
    [UnityTest]
    public IEnumerator Observations_AreFedByTheRealMemories()
    {
        yield return BuildAgentScene();
        var sensor = new VectorSensor(NpcBrainSpec.ObservationSize);

        agent.CollectObservations(sensor);
        float[] obs = agent.LastObservations;
        Assert.That(obs.Length, Is.EqualTo(NpcBrainSpec.ObservationSize));
        Assert.That(obs[2], Is.EqualTo(0f), "PerceptionMemory has no sighting yet");
        Assert.That(obs[6], Is.EqualTo(1f), "never-seen saturates the staleness slot");
        Assert.That(obs[7], Is.EqualTo(1f), "full health at the start of the episode");
        Assert.That(obs[13], Is.EqualTo(1f), "the auto-added ModeChannel starts on Hunt");

        // Shot from directly behind: DamageMemory buckets it, and the bucket has
        // to survive into the one-hot.
        selfHealth.DecreaseHealth(new DamageInfo(10f, agent.transform.position - agent.transform.forward * 5f));
        agent.CollectObservations(sensor);
        obs = agent.LastObservations;
        Assert.That(obs[8], Is.EqualTo(1f), "recently damaged");
        Assert.That(obs[10], Is.EqualTo(1f), "hit from behind");
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
        Assert.That(behaviorParams.BrainParameters.VectorObservationSize,
            Is.EqualTo(NpcBrainSpec.ObservationSize),
            "the prefab must declare the same observation width CollectObservations writes");
        Assert.That(behaviorParams.BrainParameters.NumStackedVectorObservations, Is.EqualTo(1));
        Assert.That(behaviorParams.BrainParameters.ActionSpec.BranchSizes,
            Is.EqualTo(new[] { NpcBrainSpec.MovementBranchSize, NpcBrainSpec.FireBranchSize }),
            "movement x fire");

        // The ray sensor bypassed PerceptionMemory entirely — live detection,
        // wider than sightFovDegrees, no last-seen memory — and its ~21 floats
        // are not in the frozen vector. Its removal is the point of #43.
        Assert.That(enemy.GetComponentInChildren<RayPerceptionSensorComponent3D>(true), Is.Null,
            "a ray sensor leaks the target's live position past the sensory contract");

        Assert.That(enemy.GetComponent<DecisionRequester>(), Is.Not.Null,
            "no DecisionRequester — the enemy would stand still in every mode");
        Assert.That(enemy.GetComponent<ModeChannel>(), Is.Not.Null,
            "the mode one-hot has nothing to read without a channel");
    }
#endif
}
