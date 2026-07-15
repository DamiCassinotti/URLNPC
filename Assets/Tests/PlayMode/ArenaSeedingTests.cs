using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

/// <summary>
/// End-to-end reproducibility (issue #13): a run seed fully determines which
/// arena is built and where actors spawn, spawn points land on the freshly
/// baked NavMesh at floor level, and the enemy's spawn keeps its distance
/// from the player.
/// </summary>
public class ArenaSeedingTests : PlayModeTestBase
{
    ArenaManager CreateManager(int seed, int forcedIndex = -1)
    {
        GameObject go = Track(new GameObject("TestArenaManager"));
        go.SetActive(false);
        var manager = go.AddComponent<ArenaManager>();
        manager.runSeed = seed;
        manager.forcedArenaIndex = forcedIndex;
        manager.repositionPlayerOnStart = false;
        go.SetActive(true); // Awake: seeds RunRng, builds the arena, bakes the NavMesh
        return manager;
    }

    void DestroyArena(ArenaManager manager)
    {
        Object.DestroyImmediate(manager.gameObject);
        SweepArenas();
    }

    // A -runSeed on the editor's own command line outranks the inspector
    // seed, which would collapse "two different seeds" into one.
    static void RequireInspectorSeedInEffect()
    {
        if (RunRng.SeedSource == "command line")
        {
            Assert.Ignore("Editor was launched with -runSeed; distinct-seed tests do not apply.");
        }
    }

    static Vector3[] DrawGroundPoints(ArenaManager manager, int count)
    {
        var points = new Vector3[count];
        for (int i = 0; i < count; i++) points[i] = manager.RandomGroundPoint();
        return points;
    }

    [Test]
    public void SameSeed_RebuildsSameArena_AndSameSpawnSequence()
    {
        ArenaManager m1 = CreateManager(12345);
        int index1 = m1.ActiveArenaIndex;
        Vector3[] points1 = DrawGroundPoints(m1, 5);

        DestroyArena(m1);
        RunRng.ResetForNewRun(); // a fresh run with the same seed

        ArenaManager m2 = CreateManager(12345);
        Assert.That(m2.ActiveArenaIndex, Is.EqualTo(index1), "same seed must select the same arena");
        Vector3[] points2 = DrawGroundPoints(m2, 5);
        for (int i = 0; i < points1.Length; i++)
        {
            Assert.That((points2[i] - points1[i]).magnitude, Is.LessThan(1e-3f),
                $"spawn point #{i} must replay identically for the same seed");
        }
    }

    [Test]
    public void DifferentSeeds_ProduceDifferentSpawns()
    {
        ArenaManager m1 = CreateManager(111);
        RequireInspectorSeedInEffect();
        Vector3 p1 = m1.RandomGroundPoint();

        DestroyArena(m1);
        RunRng.ResetForNewRun();

        ArenaManager m2 = CreateManager(222);
        Vector3 p2 = m2.RandomGroundPoint();
        Assert.That((p1 - p2).magnitude, Is.GreaterThan(1e-4f));
    }

    [Test]
    public void ForcedArenaIndex_OverridesRandomSelection()
    {
        ArenaManager manager = CreateManager(1, forcedIndex: 3);
        Assert.That(manager.ActiveArenaIndex, Is.EqualTo(3));
        Assert.That(manager.ActiveArenaName, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void GroundPoints_LandOnTheBakedNavMesh_AtFloorLevel()
    {
        ArenaManager manager = CreateManager(42);
        for (int i = 0; i < 20; i++)
        {
            Vector3 point = manager.RandomGroundPoint();
            Assert.That(NavMesh.SamplePosition(point, out NavMeshHit hit, 0.5f, NavMesh.AllAreas), Is.True,
                $"point #{i} {point} is not on the NavMesh");
            Assert.That((hit.position - point).magnitude, Is.LessThan(0.11f));
            Assert.That(point.y, Is.LessThanOrEqualTo(0.65f), "spawns must stay on the open floor, not on cover");
        }
    }

    [UnityTest]
    public IEnumerator EnemySpawn_LandsOnNavMesh_AwayFromThePlayer()
    {
        ArenaManager manager = CreateManager(777, forcedIndex: 0); // Courtyard: the big open floor

        GameObject player = Track(GameObject.CreatePrimitive(PrimitiveType.Capsule));
        player.name = "TestPlayer";
        player.tag = "Player";
        player.transform.position = new Vector3(-15f, 1f, -15f);
        Physics.SyncTransforms();

        GameObject enemyGo = Track(new GameObject("TestEnemy"));
        enemyGo.SetActive(false);
        enemyGo.AddComponent<NavMeshAgent>();
        var behavior = enemyGo.AddComponent<EnemyBehavior>();
        behavior.target = player.transform;
        enemyGo.SetActive(true);
        yield return null; // Start → InitAtRandomPosition on the baked NavMesh

        Vector3 spawn = enemyGo.transform.position;
        Assert.That(NavMesh.SamplePosition(spawn, out NavMeshHit _, 1.5f, NavMesh.AllAreas), Is.True,
            $"enemy spawned off the NavMesh at {spawn}");

        float minSeparation = Mathf.Min(25f, manager.SpawnSeparationCap); // 25 is EnemyBehavior's default
        float distance = Vector3.Distance(spawn, player.transform.position);
        Assert.That(distance, Is.GreaterThanOrEqualTo(minSeparation - 1.5f),
            "enemy must not spawn on top of the player");
    }
}
