using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;

// Issue #74: spawns came straight off NavMesh.SamplePosition, which is happy to
// hand back a point hard against a crate or on a stair step the bake never
// carved — the body then clips into the box. Every layout is checked, but The
// Pit and Ramparts are where it actually showed up in play.
public class ArenaSpawnClearanceTests : PlayModeTestBase
{
    // The Enemy/player capsule radius: the bar a spawn has to clear to not be
    // visibly inside the geometry. Deliberately below ArenaManager's own
    // clearance, so the test pins the symptom and not the constant.
    const float BodyRadius = 0.5f;

    ArenaManager CreateManager(int arenaIndex, int seed)
    {
        GameObject go = Track(new GameObject("TestArenaManager"));
        go.SetActive(false);
        var manager = go.AddComponent<ArenaManager>();
        manager.runSeed = seed;
        manager.forcedArenaIndex = arenaIndex;
        manager.repositionPlayerOnStart = false;
        go.SetActive(true); // Awake: builds the arena and bakes the NavMesh
        Physics.SyncTransforms();
        return manager;
    }

    // The arena's cover boxes read off the scene, independent of whatever the
    // manager registered internally.
    static Collider[] CoverBoxes()
    {
        GameObject root = GameObject.Find("Arena (Generated)");
        Assert.That(root, Is.Not.Null, "no generated arena to inspect");
        var boxes = new List<Collider>();
        foreach (Transform child in root.transform)
        {
            if (child.name == "Floor" || child.name.StartsWith("Wall_")) continue;
            Collider col = child.GetComponent<Collider>();
            if (col != null) boxes.Add(col);
        }
        return boxes.ToArray();
    }

    [Test]
    public void EveryLayout_SpawnsClearOfCover([Values(0, 1, 2, 3, 4)] int arenaIndex)
    {
        ArenaManager manager = CreateManager(arenaIndex, 20260815);
        Collider[] boxes = CoverBoxes();
        Assert.That(boxes, Is.Not.Empty, $"{manager.ActiveArenaName} has no cover to stay clear of");

        for (int i = 0; i < 200; i++)
        {
            Vector3 point = manager.RandomGroundPoint();
            foreach (Collider box in boxes)
            {
                float distance = Mathf.Sqrt(box.bounds.SqrDistance(point));
                Assert.That(distance, Is.GreaterThanOrEqualTo(BodyRadius),
                    $"{manager.ActiveArenaName}: spawn #{i} at {point} is {distance:0.00} m from {box.name} at {box.bounds.center}");
            }
        }
    }

    [Test]
    public void SpawnsStayOnTheNavMesh([Values(0, 1, 2, 3, 4)] int arenaIndex)
    {
        ArenaManager manager = CreateManager(arenaIndex, 776655);

        for (int i = 0; i < 60; i++)
        {
            Vector3 point = manager.RandomGroundPoint();
            Assert.That(NavMesh.SamplePosition(point, out NavMeshHit _, 0.1f, NavMesh.AllAreas), Is.True,
                $"{manager.ActiveArenaName}: spawn #{i} at {point} is off the NavMesh");
        }
    }

    // The clearance filter rejects candidates, so it must not turn the sampler
    // into a one-spot generator when a layout is tight.
    [Test]
    public void SpawnsStillSpreadAcrossTheArena([Values(1, 4)] int arenaIndex)
    {
        ArenaManager manager = CreateManager(arenaIndex, 314159);

        Vector3 first = manager.RandomGroundPoint();
        float furthest = 0f;
        for (int i = 0; i < 40; i++)
        {
            furthest = Mathf.Max(furthest, Vector3.Distance(first, manager.RandomGroundPoint()));
        }
        Assert.That(furthest, Is.GreaterThan(manager.SpawnSeparationCap * 0.5f),
            $"{manager.ActiveArenaName}: spawns collapsed into one region");
    }

    // Falling back is better than handing back the off-mesh origin, which a
    // Warp rejects and which strands the body at the world centre.
    [Test]
    public void FallsBackToAnOnMeshPoint_WhenNothingIsClear()
    {
        ArenaManager manager = CreateManager(1, 42);

        // Blow up a box the builder already registered as cover — a new
        // GameObject wouldn't be in the manager's list — until it swallows the
        // whole floor, so no sampled point can come back clear. The NavMesh is
        // not rebaked, so the mesh underneath stays walkable.
        GameObject pillar = GameObject.Find("Arena (Generated)/Pillar");
        Assert.That(pillar, Is.Not.Null, "the pit should have a Pillar to blow up");
        pillar.transform.position = new Vector3(0f, 0.1f, 0f);
        pillar.transform.localScale = new Vector3(400f, 0.2f, 400f);
        Physics.SyncTransforms();

        Vector3 point = manager.RandomGroundPoint();
        Assert.That(manager.IsClearOfCover(point), Is.False, "sanity: nowhere is clear under the blanket");
        Assert.That(NavMesh.SamplePosition(point, out NavMeshHit _, 0.1f, NavMesh.AllAreas), Is.True,
            $"the fallback spawn {point} must still be on the NavMesh");
    }
}
