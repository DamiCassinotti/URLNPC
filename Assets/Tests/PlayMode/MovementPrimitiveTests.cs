using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

// The movement primitives (issue #41) driven against a real arena and NavMesh:
// each one moves the agent the way its name says, every target comes from the
// perceived position rather than the live one, and MoveToCover ends somewhere
// the threat's line of sight is actually broken.
public class MovementPrimitiveTests : PlayModeTestBase
{
    // The enemy's own sight mask (EnemyBehavior defaults to ~0) and eye height.
    const int SightMask = ~0;
    const float EyeHeight = 1f;

    // The Courtyard's south lane: open floor wall to wall, with the central
    // building and the low walls out of the way.
    static readonly Vector3 EnemyStart = new Vector3(0f, 0f, -14f);
    static readonly Vector3 PlayerStart = new Vector3(0f, 0f, -9f);

    ArenaManager arena;
    GameObject player;
    GameObject enemyGo;
    EnemyBehavior behavior;
    NavMeshAgent nav;
    float travelled;

    ArenaManager CreateArena(int index)
    {
        GameObject go = Track(new GameObject("TestArenaManager"));
        go.SetActive(false);
        var manager = go.AddComponent<ArenaManager>();
        manager.runSeed = 20260811;
        manager.forcedArenaIndex = index;
        manager.repositionPlayerOnStart = false;
        go.SetActive(true); // Awake: builds the arena and bakes the NavMesh
        // Physics.autoSyncTransforms is off, so the fresh boxes are still at
        // the origin for raycasts until this runs.
        Physics.SyncTransforms();
        return manager;
    }

    IEnumerator PlaceCombatants(Vector3 enemyAt, Vector3 playerAt, Vector3? facing = null, float baseOffset = 0f)
    {
        // No collider on the player: with nothing in the way the sight ray
        // reports visible exactly as it would for a capsule, and the test's own
        // LOS probe can start at the player's eye without hitting its own body.
        player = Track(new GameObject("TestPlayer"));
        player.tag = "Player";
        player.transform.position = playerAt;

        enemyGo = Track(new GameObject("TestEnemy"));
        enemyGo.SetActive(false);
        nav = enemyGo.AddComponent<NavMeshAgent>();
        // The shipped Enemy prefab lifts the body a metre off the mesh; pass 1
        // to reproduce that when a query's origin snapping is under test.
        nav.baseOffset = baseOffset;
        // MoveToCover judges cover against the agent's head — its height. This
        // bare rig has no body, so pin height to the eye the LOS helper checks.
        nav.height = EyeHeight;
        behavior = enemyGo.AddComponent<EnemyBehavior>();
        behavior.enabled = false; // skip Start's random respawn: the test picks the spot
        behavior.target = player.transform;
        enemyGo.SetActive(true);  // Awake auto-adds PerceptionMemory

        Assert.That(nav.Warp(SnapToNavMesh(enemyAt)), Is.True, $"the enemy must start on the NavMesh at {enemyAt}");
        FaceTowards(facing ?? playerAt);
        Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();

        behavior.Perception.Refresh();
    }

    IEnumerator BuildScene(int arenaIndex = 0)
    {
        arena = CreateArena(arenaIndex);
        yield return PlaceCombatants(EnemyStart, PlayerStart);
        Assert.That(behavior.Perception.HasEverSeen, Is.True, "the enemy must have a sighting to work from");
    }

    // Stands in for the DecisionRequester: one primitive per fixed step, which
    // is where the agent's decisions land.
    IEnumerator Drive(MovementAction action, float seconds)
    {
        travelled = 0f;
        Vector3 last = enemyGo.transform.position;
        float until = Time.time + seconds;
        while (Time.time < until)
        {
            behavior.Move(action);
            yield return new WaitForFixedUpdate();
            travelled += Vector3.Distance(last, enemyGo.transform.position);
            last = enemyGo.transform.position;
        }
    }

    void FaceTowards(Vector3 point)
    {
        Vector3 flat = new Vector3(point.x, enemyGo.transform.position.y, point.z);
        enemyGo.transform.LookAt(flat);
    }

    Vector3 EnemyPos => enemyGo.transform.position;

    static Vector3 SnapToNavMesh(Vector3 point)
    {
        return NavMesh.SamplePosition(point + Vector3.up, out NavMeshHit hit, 4f, NavMesh.AllAreas)
            ? hit.position
            : point;
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // The same eye-line ArenaManager promises to have blocked.
    static bool LosBlocked(Vector3 threat, Vector3 point)
    {
        Vector3 eye = threat + Vector3.up * EyeHeight;
        Vector3 toPoint = (point + Vector3.up * EyeHeight) - eye;
        return Physics.Raycast(eye, toPoint.normalized, toPoint.magnitude - 0.1f,
            SightMask, QueryTriggerInteraction.Ignore);
    }

    [UnityTest]
    public IEnumerator Advance_ClosesOnTheTarget()
    {
        yield return BuildScene();

        float before = FlatDistance(EnemyPos, PlayerStart);
        yield return Drive(MovementAction.Advance, 1f);

        Assert.That(FlatDistance(EnemyPos, PlayerStart), Is.LessThan(before - 1f),
            "Advance must close the gap to the target");
    }

    [UnityTest]
    public IEnumerator Advance_HeadsForTheRememberedSpot_NotTheLiveOne()
    {
        yield return BuildScene();

        // Behind the enemy, so it is outside the sight cone and the memory
        // freezes at PlayerStart.
        Vector3 hidden = new Vector3(0f, 0f, -18f);
        player.transform.position = hidden;
        Physics.SyncTransforms();
        behavior.Perception.Refresh();
        Assert.That(behavior.Perception.CurrentlyVisible, Is.False, "sanity: the player must be out of sight");

        float toRemembered = FlatDistance(EnemyPos, PlayerStart);
        float toLive = FlatDistance(EnemyPos, hidden);
        yield return Drive(MovementAction.Advance, 1f);

        Assert.That(FlatDistance(EnemyPos, PlayerStart), Is.LessThan(toRemembered - 1f),
            "Advance must head for the last-seen position");
        Assert.That(FlatDistance(EnemyPos, hidden), Is.GreaterThan(toLive),
            "the NPC must not track the player it can no longer see");
    }

    [UnityTest]
    public IEnumerator Retreat_OpensDistanceFromTheTarget()
    {
        yield return BuildScene();

        float before = FlatDistance(EnemyPos, PlayerStart);
        yield return Drive(MovementAction.Retreat, 1f);

        Assert.That(FlatDistance(EnemyPos, PlayerStart), Is.GreaterThan(before + 1f),
            "Retreat must open the gap to the target");
    }

    // Every bearing-relative primitive with nothing seen — the state every
    // episode opens in, spawns being further apart than sight range. There is
    // no bearing to move along, and running off own facing would bury the NPC
    // nose-first in the first wall it meets, where it stops moving, stops
    // turning and never sees anything to break out with. They must Wander.
    [UnityTest]
    public IEnumerator BlindMovingPrimitive_WandersInsteadOfWedging(
        [Values(MovementAction.Advance, MovementAction.Retreat,
                MovementAction.StrafeLeft, MovementAction.StrafeRight)] MovementAction action)
    {
        arena = CreateArena(0);
        // Nose to the south wall, so running off own facing has a wall in the
        // way whichever direction the primitive steps relative to it.
        yield return PlaceCombatants(new Vector3(0f, 0f, -18.5f), new Vector3(0f, 0f, 18f),
            facing: new Vector3(0f, 0f, -25f));
        Assert.That(behavior.Perception.HasEverSeen, Is.False, "sanity: nothing seen");

        yield return Drive(action, 2.5f);

        Assert.That(travelled, Is.GreaterThan(3f), $"{action} with no target must cover ground to find one");
    }

    // The shipped Enemy prefab carries NavMeshAgent baseOffset 1, so its
    // transform sits a metre above the mesh — the config SetStepDestination
    // snaps its raycast origin for. This pins that the stepping primitives trim
    // correctly against a wall in that configuration (the test rig otherwise
    // runs at baseOffset 0, on the mesh).
    [UnityTest]
    public IEnumerator SteppingPrimitive_TrimsCorrectlyWithTheBodyLiftedOffTheMesh()
    {
        arena = CreateArena(0);
        yield return PlaceCombatants(new Vector3(0f, 0f, -18f), new Vector3(0f, 0f, -9f), baseOffset: 1f);
        Assert.That(behavior.Perception.HasEverSeen, Is.True, "sanity: the target is visible");
        Assert.That(EnemyPos.y, Is.GreaterThan(0.5f), "sanity: the base offset must actually lift the body off the mesh");

        yield return Drive(MovementAction.Advance, 0.4f); // give it a path north to abandon
        Vector3 turnedAt = EnemyPos;
        yield return Drive(MovementAction.Retreat, 1f);

        Assert.That(EnemyPos.z, Is.LessThan(turnedAt.z - 0.5f),
            "Retreat must still turn the agent around off a lifted body, not stall against the wall");
    }

    [UnityTest]
    public IEnumerator Retreat_AgainstTheArenaEdge_StillBacksAway()
    {
        arena = CreateArena(0);
        // Backed up against the south wall with the target to the north, so a
        // full retreat step lands several metres past the floor edge.
        yield return PlaceCombatants(new Vector3(0f, 0f, -18f), new Vector3(0f, 0f, -9f));
        Assert.That(behavior.Perception.HasEverSeen, Is.True, "sanity: the target is visible");

        yield return Drive(MovementAction.Advance, 0.4f); // give it a path north to abandon
        Vector3 turnedAt = EnemyPos;
        yield return Drive(MovementAction.Retreat, 1f);

        Assert.That(EnemyPos.z, Is.LessThan(turnedAt.z - 0.5f),
            "a step that overshoots the arena edge must still turn the agent around");
    }

    [UnityTest]
    public IEnumerator Retreat_Cornered_DoesNotKeepRunningTheLastAdvance()
    {
        arena = CreateArena(0);
        // On the southern NavMesh boundary with the target to the north, so
        // the retreat step has nowhere to go and is skipped.
        Assert.That(NavMesh.SamplePosition(new Vector3(0f, 0f, -25f), out NavMeshHit edge, 10f, NavMesh.AllAreas),
            Is.True, "no walkable ground against the south wall");
        yield return PlaceCombatants(edge.position, new Vector3(0f, 0f, -9f));
        Assert.That(behavior.Perception.HasEverSeen, Is.True, "sanity: the target is visible");
        Assert.That(NavMesh.Raycast(EnemyPos, EnemyPos + Vector3.back * 6f, out NavMeshHit south, NavMesh.AllAreas)
            && south.distance < 0.5f, Is.True, "sanity: the enemy is backed against the boundary");

        // No frame in between, so the agent is still hard against the boundary
        // when the retreat is issued.
        behavior.Move(MovementAction.Advance);
        Assert.That(nav.destination.z, Is.GreaterThan(EnemyPos.z), "sanity: the advance heads north at the target");

        behavior.Move(MovementAction.Retreat);

        Assert.That(nav.hasPath || nav.pathPending, Is.False,
            "a cornered Retreat must drop the advance it replaced, not leave it running at the target");
    }

    [UnityTest]
    public IEnumerator Strafes_StepSidewaysWhileKeepingTheTargetInSight(
        [Values(MovementAction.StrafeLeft, MovementAction.StrafeRight)] MovementAction action)
    {
        yield return BuildScene();

        // The enemy starts due south of the player, so "right" is +X.
        float expectedSign = action == MovementAction.StrafeRight ? 1f : -1f;
        Vector3 before = EnemyPos;
        yield return Drive(action, 1f);

        Vector3 moved = EnemyPos - before;
        Assert.That(moved.x * expectedSign, Is.GreaterThan(1f), $"{action} must step to the agent's {(expectedSign > 0f ? "right" : "left")}");
        Assert.That(Mathf.Abs(moved.z), Is.LessThan(Mathf.Abs(moved.x)), "a strafe is sideways, not a chase or a retreat");
        Assert.That(behavior.Perception.CurrentlyVisible, Is.True,
            "strafing must keep facing the target, or the sidestep walks it out of the sight cone");
    }

    [UnityTest]
    public IEnumerator Hold_StopsTheAgentAndKeepsWatching()
    {
        yield return BuildScene();

        yield return Drive(MovementAction.Advance, 0.5f); // get it moving first
        yield return Drive(MovementAction.Hold, 0.2f);

        Vector3 stopped = EnemyPos;
        yield return Drive(MovementAction.Hold, 0.5f);

        Assert.That(FlatDistance(EnemyPos, stopped), Is.LessThan(0.1f), "Hold must keep the agent where it is");
        Assert.That(behavior.Perception.CurrentlyVisible, Is.True, "a held agent keeps watching the target");
    }

    [UnityTest]
    public IEnumerator Wander_KeepsMovingWithoutASighting()
    {
        arena = CreateArena(0);
        // The player is never placed in view: Wander is the one primitive that
        // owes nothing to the perceived position.
        yield return PlaceCombatants(EnemyStart, new Vector3(0f, 0f, 18f));
        Assert.That(behavior.Perception.HasEverSeen, Is.False,
            "sanity: 32 m away, past the sight range and the central building");

        yield return Drive(MovementAction.Wander, 2f);

        Assert.That(travelled, Is.GreaterThan(2f), "Wander must keep picking new waypoints and walking to them");
    }

    [UnityTest]
    public IEnumerator MoveToCover_EndsWhereTheThreatCannotSeeIt()
    {
        arena = CreateArena(0);
        Assert.That(FindCoverScenario(arena, out Vector3 from, out Vector3 threat), Is.True,
            "the Courtyard should offer a visible-threat/cover-nearby pair");

        yield return PlaceCombatants(from, threat);
        Assert.That(behavior.Perception.HasEverSeen, Is.True, "sanity: the threat starts visible");
        Assert.That(LosBlocked(threat, EnemyPos), Is.False, "sanity: the enemy starts in the open");

        // Walk until it arrives, then check where it ended up. The travelled
        // guard keeps the frame before the first path is computed, where
        // remainingDistance is still 0, from counting as an arrival.
        bool arrived = false;
        float deadline = Time.time + 10f;
        while (Time.time < deadline && !arrived)
        {
            behavior.Move(MovementAction.MoveToCover);
            yield return new WaitForFixedUpdate();
            arrived = !nav.pathPending
                && nav.remainingDistance <= nav.stoppingDistance + 0.2f
                && FlatDistance(EnemyPos, from) > 0.5f;
        }
        Assert.That(arrived, Is.True, $"the agent never reached cover from {from}");

        Assert.That(LosBlocked(threat, EnemyPos), Is.True,
            $"MoveToCover left the agent at {EnemyPos}, still in the threat's line of sight from {threat}");
    }

    [UnityTest]
    public IEnumerator MoveToCover_QueriesTheFrameItFirstSeesTheThreat()
    {
        arena = CreateArena(0);
        Assert.That(FindCoverScenario(arena, out Vector3 from, out Vector3 threat), Is.True,
            "the Courtyard should offer a visible-threat/cover-nearby pair");

        // Start blind, facing away from the threat, and command MoveToCover:
        // the query short-circuits on "nothing seen". The rate limiter must not
        // then swallow the query on the very next step once the threat appears.
        yield return PlaceCombatants(from, threat, facing: from + (from - threat));
        Assert.That(behavior.Perception.HasEverSeen, Is.False, "sanity: the threat starts unseen");

        behavior.Move(MovementAction.MoveToCover); // arms nothing; there is no query to rate-limit
        yield return new WaitForFixedUpdate();

        FaceTowards(threat); // now the threat is in the cone
        behavior.Perception.Refresh();
        Assert.That(behavior.Perception.CurrentlyVisible, Is.True, "sanity: the threat is now visible");

        behavior.Move(MovementAction.MoveToCover);
        yield return new WaitForFixedUpdate();

        // Retreat's fallback destination is a step into the open; a real cover
        // point has the threat's LOS to it broken. That tells them apart, and
        // it is only reachable if the query ran this frame rather than sitting
        // out the interval a no-sighting MoveToCover would otherwise have armed.
        Assert.That(LosBlocked(threat, nav.destination), Is.True,
            "the cover query must run the frame the threat is first seen, not fall back to Retreat");
    }

    [UnityTest]
    public IEnumerator MoveToCover_FallsBackToRetreat_WhenTheLayoutHasNoCover()
    {
        yield return BuildScene();

        StripCoverGeometry();
        Physics.SyncTransforms();
        Assert.That(arena.NearestCoverPoint(EnemyPos, PlayerStart, SightMask, null, EyeHeight, out Vector3 _), Is.False,
            "sanity: no cover left to move to");

        float before = FlatDistance(EnemyPos, PlayerStart);
        yield return Drive(MovementAction.MoveToCover, 1f);

        Assert.That(FlatDistance(EnemyPos, PlayerStart), Is.GreaterThan(before + 1f),
            "with no cover available MoveToCover must fall back to Retreat, not stand still");
    }

    // A pair the primitive can be tested on: the threat visible from the start
    // point, and cover close enough to reach inside the test's budget.
    static bool FindCoverScenario(ArenaManager manager, out Vector3 from, out Vector3 threat)
    {
        for (int i = 0; i < 200; i++)
        {
            from = manager.RandomGroundPoint();
            threat = manager.RandomGroundPoint();
            float distance = Vector3.Distance(from, threat);
            if (distance < 6f || distance > 15f) continue;
            if (LosBlocked(threat, from)) continue;
            if (!manager.NearestCoverPoint(from, threat, SightMask, null, EyeHeight, out Vector3 cover)) continue;
            float toCover = Vector3.Distance(from, cover);
            if (toCover < 2f || toCover > 10f) continue;
            return true;
        }
        from = default;
        threat = default;
        return false;
    }

    // Everything the arena builder registered as cover, removed — the floor and
    // perimeter stay, so there is still somewhere to walk.
    static void StripCoverGeometry()
    {
        GameObject root = GameObject.Find("Arena (Generated)");
        Assert.That(root, Is.Not.Null, "no generated arena to strip");

        var doomed = new List<GameObject>();
        foreach (Transform child in root.transform)
        {
            if (child.name == "Floor" || child.name.StartsWith("Wall_")) continue;
            doomed.Add(child.gameObject);
        }
        foreach (GameObject go in doomed) Object.DestroyImmediate(go);
    }
}
