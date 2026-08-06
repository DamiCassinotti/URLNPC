using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

// The sensory contract (issue #9) against real geometry: the NPC knows the
// player's position only while line of sight holds, the memory freezes at the
// last-seen point the moment sight breaks, and episode resets wipe it.
//
// The enemy is assembled with its NavMeshAgent and EnemyBehavior disabled —
// there is no NavMesh here, and the sight check needs only transforms and
// raycasts.
public class PerceptionContractTests : PlayModeTestBase
{
    GameObject player;
    GameObject enemyGo;
    EnemyBehavior behavior;
    PerceptionMemory memory;

    IEnumerator BuildScene()
    {
        // Spawn out of sight (behind AND beyond sightRange): PerceptionMemory
        // auto-refreshes every frame, so a player spawned in view would be
        // memorized before the test repositions it.
        player = Track(GameObject.CreatePrimitive(PrimitiveType.Capsule));
        player.name = "TestPlayer";
        player.tag = "Player";
        player.transform.position = new Vector3(0f, 0f, -30f);

        enemyGo = Track(new GameObject("TestEnemy"));
        enemyGo.SetActive(false);
        enemyGo.AddComponent<NavMeshAgent>().enabled = false;
        behavior = enemyGo.AddComponent<EnemyBehavior>();
        behavior.enabled = false; // skip Start(): no NavMesh to spawn on
        behavior.target = player.transform;
        enemyGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // facing +Z, toward the player
        enemyGo.SetActive(true); // Awake auto-adds PerceptionMemory
        memory = behavior.Perception;
        Assert.That(memory, Is.Not.Null, "EnemyBehavior.Awake must auto-add PerceptionMemory");

        yield return new WaitForFixedUpdate(); // let physics register the colliders
    }

    static void MoveAndSync(Transform t, Vector3 position)
    {
        t.position = position;
        Physics.SyncTransforms();
    }

    [UnityTest]
    public IEnumerator Memory_TracksWhileVisible_FreezesWhenSightBreaks()
    {
        yield return BuildScene();

        MoveAndSync(player.transform, new Vector3(0f, 0f, 10f)); // step into view
        memory.Refresh();
        Assert.That(memory.CurrentlyVisible, Is.True, "clear line of sight in range and FOV");
        Assert.That(memory.HasEverSeen, Is.True);
        Assert.That(memory.LastSeenPosition, Is.EqualTo(player.transform.position));

        // While visible the memory tracks the live position.
        MoveAndSync(player.transform, new Vector3(2f, 0f, 10f));
        memory.Refresh();
        Assert.That(memory.CurrentlyVisible, Is.True);
        Assert.That(memory.LastSeenPosition, Is.EqualTo(player.transform.position));
        Vector3 lastSeen = memory.LastSeenPosition;

        // A wall between them breaks sight; the memory must freeze, not track.
        GameObject wall = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        wall.transform.localScale = new Vector3(8f, 4f, 1f);
        MoveAndSync(wall.transform, new Vector3(1f, 0f, 5f));
        memory.Refresh();
        Assert.That(memory.CurrentlyVisible, Is.False, "a wall must break line of sight");
        Assert.That(memory.HasEverSeen, Is.True, "memory outlives sight");
        Assert.That(memory.LastSeenPosition, Is.EqualTo(lastSeen), "position must freeze at the last sighting");

        // The player moving behind the wall must not leak into the memory.
        MoveAndSync(player.transform, new Vector3(-4f, 0f, 12f));
        memory.Refresh();
        Assert.That(memory.CurrentlyVisible, Is.False);
        Assert.That(memory.LastSeenPosition, Is.EqualTo(lastSeen), "the NPC must not wallhack-track the player");

        // Time since the sighting keeps growing while blind.
        float before = memory.TimeSinceSeen;
        yield return new WaitForSeconds(0.05f);
        Assert.That(memory.TimeSinceSeen, Is.GreaterThan(before));
    }

    [UnityTest]
    public IEnumerator TargetOutsideFov_IsNotVisible()
    {
        yield return BuildScene();

        MoveAndSync(player.transform, new Vector3(0f, 0f, -10f)); // dead behind (FOV is 120°)
        memory.Refresh();
        Assert.That(memory.CurrentlyVisible, Is.False);
        Assert.That(memory.HasEverSeen, Is.False, "an unseen player must leave no trace");
    }

    [UnityTest]
    public IEnumerator TargetBeyondSightRange_IsNotVisible()
    {
        yield return BuildScene();

        MoveAndSync(player.transform, new Vector3(0f, 0f, 50f)); // sightRange is 20
        memory.Refresh();
        Assert.That(memory.CurrentlyVisible, Is.False);
        Assert.That(memory.HasEverSeen, Is.False);
    }

    [UnityTest]
    public IEnumerator ResetState_WipesMemoryAndShotFlag()
    {
        yield return BuildScene();

        MoveAndSync(player.transform, new Vector3(0f, 0f, 10f)); // step into view
        memory.Refresh();
        Assert.That(memory.HasEverSeen, Is.True);

        // No NavMesh in this fixture: the respawn inside ResetState logs its
        // "could not place on NavMesh" complaints, which is expected here.
        LogAssert.ignoreFailingMessages = true;
        behavior.ResetState();

        Assert.That(memory.HasEverSeen, Is.False, "last episode's sighting must not leak into the next");
        Assert.That(memory.CurrentlyVisible, Is.False);
        Assert.That(memory.LastSeenPosition, Is.EqualTo(Vector3.zero));
        Assert.That(behavior.DidShoot, Is.False);
    }
}
