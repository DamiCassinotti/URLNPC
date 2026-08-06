using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Contract for ArenaManager.NearestCoverPoint (issue #14): whatever it hands
/// back sits on the NavMesh, has the threat's eye-line to it blocked and is
/// walkable from the query origin — or it correctly reports no cover at all.
/// </summary>
public class ArenaCoverTests : PlayModeTestBase
{
    // Must match ArenaManager's cover constants.
    const float EyeHeight = 1f;

    ArenaManager CreateManager(int arenaIndex, int seed = 20260714)
    {
        GameObject go = Track(new GameObject("TestArenaManager"));
        go.SetActive(false);
        var manager = go.AddComponent<ArenaManager>();
        manager.runSeed = seed;
        manager.forcedArenaIndex = arenaIndex;
        manager.repositionPlayerOnStart = false;
        go.SetActive(true); // Awake: builds the arena and bakes the NavMesh
        // Physics.autoSyncTransforms is off, so the freshly built boxes are
        // still at the origin for raycasts until this runs.
        Physics.SyncTransforms();
        return manager;
    }

    static Vector3 SnapToNavMesh(Vector3 point)
    {
        return NavMesh.SamplePosition(point + Vector3.up, out NavMeshHit hit, 4f, NavMesh.AllAreas)
            ? hit.position
            : point;
    }

    // The same eye-line the implementation promises to have blocked.
    static bool LosBlocked(Vector3 threat, Vector3 point)
    {
        Vector3 eye = threat + Vector3.up * EyeHeight;
        Vector3 toPoint = (point + Vector3.up * EyeHeight) - eye;
        return Physics.Raycast(eye, toPoint.normalized, toPoint.magnitude - 0.1f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    static bool Reachable(Vector3 from, Vector3 to)
    {
        var path = new NavMeshPath();
        return NavMesh.CalculatePath(SnapToNavMesh(from), to, NavMesh.AllAreas, path)
            && path.status == NavMeshPathStatus.PathComplete;
    }

    [Test]
    public void EveryLayout_ReturnsCoverThatIsHidden_AndWalkableTo([Values(0, 1, 2, 3, 4)] int arenaIndex)
    {
        ArenaManager manager = CreateManager(arenaIndex);

        int found = 0;
        for (int i = 0; i < 60; i++)
        {
            Vector3 from = manager.RandomGroundPoint();
            Vector3 threat = manager.RandomGroundPoint();
            if (Vector3.Distance(from, threat) < 4f) continue;

            if (!manager.NearestCoverPoint(from, threat, out Vector3 cover)) continue;
            found++;

            string where = $"{manager.ActiveArenaName}: cover {cover} for from {from}, threat {threat}";
            Assert.That(NavMesh.SamplePosition(cover, out NavMeshHit _, 0.5f, NavMesh.AllAreas), Is.True,
                $"{where} is off the NavMesh");
            Assert.That(LosBlocked(threat, cover), Is.True, $"{where} is in the threat's line of sight");
            Assert.That(Reachable(from, cover), Is.True, $"{where} has no complete path from the query origin");
        }

        Assert.That(found, Is.GreaterThan(0), $"{manager.ActiveArenaName} never offered any cover");
    }

    [Test]
    public void PicksTheNearestOfTheAvailableCoverPoints()
    {
        ArenaManager manager = CreateManager(0); // Courtyard: cover in every corner
        Vector3 threat = Vector3.zero;
        Vector3 west = new Vector3(-14f, 0f, -12f);
        Vector3 east = new Vector3(14f, 0f, -12f);

        Assert.That(manager.NearestCoverPoint(west, threat, out Vector3 westCover), Is.True);
        Assert.That(manager.NearestCoverPoint(east, threat, out Vector3 eastCover), Is.True);

        // Same threat means both queries saw the same candidates, so each
        // origin must have kept the one nearer to it — as long as the other
        // origin's pick was reachable from here too.
        Assume.That(Reachable(west, eastCover) && Reachable(east, westCover), Is.True);
        Assert.That(Vector3.Distance(west, westCover), Is.LessThanOrEqualTo(Vector3.Distance(west, eastCover)));
        Assert.That(Vector3.Distance(east, eastCover), Is.LessThanOrEqualTo(Vector3.Distance(east, westCover)));
    }

    [Test]
    public void ReportsNoCover_WhenTheLayoutHasNoneLeft()
    {
        ArenaManager manager = CreateManager(0);
        Vector3 from = new Vector3(-14f, 0f, -14f);
        Vector3 threat = Vector3.zero;
        Assert.That(manager.NearestCoverPoint(from, threat, out Vector3 _), Is.True,
            "sanity: the intact courtyard has cover");

        StripCoverGeometry();
        Physics.SyncTransforms();

        Assert.That(manager.NearestCoverPoint(from, threat, out Vector3 _), Is.False,
            "with every cover box gone the caller must be told to fall back");
    }

    [Test]
    public void ThreatStandingOnACoverBox_SkipsItWithoutPoisoningTheResult()
    {
        ArenaManager manager = CreateManager(0);
        Vector3 crate = new Vector3(-12f, 0f, 10f); // dead centre of a corner crate stack

        Assert.That(manager.NearestCoverPoint(new Vector3(0f, 0f, -14f), crate, out Vector3 cover), Is.True,
            "the rest of the courtyard still offers cover");
        Assert.That(float.IsNaN(cover.x) || float.IsNaN(cover.z), Is.False, "degenerate direction leaked a NaN");
        Assert.That(LosBlocked(crate, cover), Is.True);
    }

    [Test]
    public void ALowStairStep_IsNotCover()
    {
        ArenaManager manager = CreateManager(1); // The Pit — the only layout with stairs

        // Leave a single 0.25 m step standing: it is registered as a cover
        // candidate, but nobody hides behind a 25 cm step, so the eye-height
        // LOS check has to be what rejects it.
        Transform step = StripCoverGeometryExceptOneStep();
        Physics.SyncTransforms();
        Bounds bounds = step.GetComponent<Collider>().bounds;
        Assert.That(bounds.size.y, Is.LessThan(EyeHeight),
            "sanity: the surviving step must be shorter than eye height");

        // The stairs run along +Z: uphill is behind the step, the open pit
        // floor in front of it.
        Vector3 center = new Vector3(bounds.center.x, 0f, bounds.center.z);
        Vector3 threat = center + new Vector3(0f, 0f, 6f);
        Vector3 from = center + new Vector3(0f, 0f, -8f);
        Assert.That(NavMesh.SamplePosition(center + new Vector3(0f, 1f, -3.5f), out NavMeshHit _, 1.5f, NavMesh.AllAreas),
            Is.True, "sanity: there is walkable floor on the far side of the step");

        Assert.That(manager.NearestCoverPoint(from, threat, out Vector3 cover), Is.False,
            $"a {bounds.size.y:0.00} m step is not cover, but {cover} was offered");
    }

    [Test]
    public void ALowStairStep_IsNotCover_FromAnywhereInTheArena()
    {
        ArenaManager manager = CreateManager(1);
        Vector3[] origins = new Vector3[24];
        Vector3[] threats = new Vector3[origins.Length];
        for (int i = 0; i < origins.Length; i++)
        {
            origins[i] = manager.RandomGroundPoint();
            threats[i] = manager.RandomGroundPoint();
        }

        int coveredWhileIntact = 0;
        for (int i = 0; i < origins.Length; i++)
        {
            if (manager.NearestCoverPoint(origins[i], threats[i], out Vector3 _)) coveredWhileIntact++;
        }
        Assert.That(coveredWhileIntact, Is.GreaterThan(0), "sanity: the intact pit covers some of these pairs");

        StripCoverGeometryExceptOneStep();
        Physics.SyncTransforms();

        for (int i = 0; i < origins.Length; i++)
        {
            Assert.That(manager.NearestCoverPoint(origins[i], threats[i], out Vector3 cover), Is.False,
                $"pair #{i} (from {origins[i]}, threat {threats[i]}) was offered {cover} behind a stair step");
        }
    }

    // Everything the arena builder registered as cover, removed — leaving the
    // floor and perimeter behind.
    static void StripCoverGeometry()
    {
        StripCoverGeometry(null);
    }

    // Same, but keeps the lowest step of the +Z staircase (see BuildThePit).
    // Returns the survivor.
    static Transform StripCoverGeometryExceptOneStep()
    {
        GameObject root = GameObject.Find("Arena (Generated)");
        Assert.That(root, Is.Not.Null, "no generated arena to strip");
        Transform step = null;
        foreach (Transform child in root.transform)
        {
            // Both staircases have a Step_0; take the one facing +Z.
            if (child.name == "Step_0" && child.position.z > 0f) step = child;
        }
        Assert.That(step, Is.Not.Null, "the pit should have a +Z staircase to keep a step from");
        StripCoverGeometry(step);
        return step;
    }

    static void StripCoverGeometry(Transform keep)
    {
        GameObject root = GameObject.Find("Arena (Generated)");
        Assert.That(root, Is.Not.Null, "no generated arena to strip");

        var doomed = new List<GameObject>();
        foreach (Transform child in root.transform)
        {
            if (child == keep || child.name == "Floor" || child.name.StartsWith("Wall_")) continue;
            doomed.Add(child.gameObject);
        }
        foreach (GameObject go in doomed) Object.DestroyImmediate(go);
    }
}
