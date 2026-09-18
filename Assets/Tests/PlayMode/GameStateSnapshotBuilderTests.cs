using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

// The adapter half of the game-state snapshot (#123): auto-added by
// EnemyBehavior.Awake, samples the perception memory per frame, attributes the
// opponent's shots by root tag and wipes with the episode. The bucketing and
// accumulation rules themselves are GameStateSnapshotTests and
// ObservedTargetStatsTests.
//
// Same fixture shape as PerceptionContractTests: NavMeshAgent and behavior
// disabled (no NavMesh here), the sight check needs only transforms and rays.
public class GameStateSnapshotBuilderTests : PlayModeTestBase
{
    GameObject player;
    EnemyBehavior behavior;
    GameStateSnapshotBuilder builder;

    IEnumerator BuildScene()
    {
        // Spawn out of sight (behind AND beyond sightRange): the memory
        // refreshes every frame, so a player spawned in view would already be
        // memorized — and sampled — before the test repositions it.
        player = Track(GameObject.CreatePrimitive(PrimitiveType.Capsule));
        player.name = "TestPlayer";
        player.tag = "Player";
        player.transform.position = new Vector3(0f, 0f, -(CombatBalance.SightRange + 10f));

        GameObject enemyGo = Track(new GameObject("TestEnemy"));
        enemyGo.SetActive(false);
        enemyGo.AddComponent<NavMeshAgent>().enabled = false;
        enemyGo.AddComponent<Health>();
        behavior = enemyGo.AddComponent<EnemyBehavior>();
        behavior.enabled = false; // skip Start(): no NavMesh to spawn on
        behavior.target = player.transform;
        enemyGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // facing +Z
        enemyGo.SetActive(true); // Awake auto-adds the builder with the memories
        builder = behavior.Snapshot;
        Assert.That(builder, Is.Not.Null, "EnemyBehavior.Awake must auto-add GameStateSnapshotBuilder");

        yield return new WaitForFixedUpdate(); // let physics register the colliders
    }

    static void MoveAndSync(Transform t, Vector3 position)
    {
        t.position = position;
        Physics.SyncTransforms();
    }

    // Refresh the memory to the moved world, then let a frame run so the
    // builder's Update samples the new visibility.
    IEnumerator LetBuilderSample()
    {
        behavior.Perception.Refresh();
        yield return null;
    }

    [UnityTest]
    public IEnumerator BeforeAnySighting_TheSnapshotIsTheNeverSeenFixedPoint()
    {
        yield return BuildScene();

        GameStateSnapshot snapshot = builder.BuildSnapshot();
        Assert.That(snapshot.hpPercent, Is.EqualTo(100));
        Assert.That(snapshot.targetVisible, Is.False);
        Assert.That(snapshot.targetDistance, Is.EqualTo(DistanceBucket.None));
        Assert.That(snapshot.secondsSinceSeen, Is.EqualTo(-1));
        Assert.That(snapshot.observedSeconds, Is.Zero);
        Assert.That(snapshot.mode, Is.EqualTo(NpcMode.Hunt), "the channel's initial mode");
    }

    [UnityTest]
    public IEnumerator ASighting_ReachesTheSnapshotThroughThePerceptionMemory()
    {
        yield return BuildScene();

        MoveAndSync(player.transform, new Vector3(0f, 0f, 9f));
        yield return LetBuilderSample();

        GameStateSnapshot snapshot = builder.BuildSnapshot();
        Assert.That(snapshot.targetVisible, Is.True);
        Assert.That(snapshot.targetDistance, Is.EqualTo(DistanceBucket.Near));
        Assert.That(snapshot.secondsSinceSeen, Is.Zero);
        Assert.That(builder.Observed.VisibleSeconds, Is.GreaterThan(0f),
            "the per-frame sampling must be accumulating watched time");
    }

    [UnityTest]
    public IEnumerator OnlyTheOpponentsShots_AreHeard_AndOnlyWhileVisible()
    {
        yield return BuildScene();
        Weapon playerWeapon = CreateWeapon("Player", new Vector3(50f, 0f, 0f)); // fires into empty space
        Weapon npcWeapon = CreateWeapon("NPC", new Vector3(-50f, 0f, 0f));

        // Unseen: the opponent's shot goes unheard.
        playerWeapon.Shoot();
        Assert.That(builder.Observed.ShotsHeard, Is.Zero, "a shot from an unseen target is not an observation");

        MoveAndSync(player.transform, new Vector3(0f, 0f, 9f));
        yield return LetBuilderSample();
        Assert.That(builder.Observed.TargetVisible, Is.True, "sanity: the sighting must have been sampled");

        playerWeapon.Shoot();
        Assert.That(builder.Observed.ShotsHeard, Is.EqualTo(1));

        // Own-side fire is not the opponent's.
        npcWeapon.Shoot();
        Assert.That(builder.Observed.ShotsHeard, Is.EqualTo(1), "the NPC's own side must not be counted");

        // Sight breaks; the stats freeze again.
        MoveAndSync(player.transform, new Vector3(0f, 0f, -(CombatBalance.SightRange + 10f)));
        yield return LetBuilderSample();
        playerWeapon.Shoot();
        Assert.That(builder.Observed.ShotsHeard, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator ResetState_StartsTheNextEpisodeBlank()
    {
        yield return BuildScene();
        Weapon playerWeapon = CreateWeapon("Player", new Vector3(50f, 0f, 0f));

        MoveAndSync(player.transform, new Vector3(0f, 0f, 9f));
        yield return LetBuilderSample();
        playerWeapon.Shoot();
        Assert.That(builder.Observed.VisibleSeconds, Is.GreaterThan(0f), "sanity: something to wipe");
        Assert.That(builder.Observed.ShotsHeard, Is.EqualTo(1));

        // No NavMesh in this fixture: the respawn inside ResetState logs its
        // "could not place on NavMesh" complaints, which is expected here.
        LogAssert.ignoreFailingMessages = true;
        behavior.ResetState();

        Assert.That(builder.Observed.TargetVisible, Is.False);
        Assert.That(builder.Observed.VisibleSeconds, Is.Zero);
        Assert.That(builder.Observed.ShotsHeard, Is.Zero);
        GameStateSnapshot snapshot = builder.BuildSnapshot();
        Assert.That(snapshot.targetDistance, Is.EqualTo(DistanceBucket.None),
            "last episode's sighting must not leak into the next");
    }
}
