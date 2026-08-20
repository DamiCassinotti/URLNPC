using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

// Issue #103: Enemy.prefab's transform is its capsule centre lifted 1 m onto
// the mesh, CombatantRig's player body has its origin at the feet. Everything
// derived off transform.position has to read the same on both, or the two sides
// of a self-play round fight different geometry — the player was firing from
// ankle height and probing the enemy's eye-line a metre above its head.
public class BodyHeightTests : PlayModeTestBase
{
    const float BodyHeight = 2f;
    const float TargetRange = 5f;

    // A body with the given origin convention, standing on ground at y = 0 and
    // facing +Z. Returned inactive so the caller can attach and configure what
    // it needs before Awake runs. No collider: the probes ignore the two bodies
    // anyway, and a capsule wrapped around a ray origin only muddies the reads.
    GameObject CreateAgentBody(string name, float z, float baseOffset)
    {
        GameObject go = Track(new GameObject(name));
        go.SetActive(false);
        go.transform.position = new Vector3(0f, baseOffset, z);
        go.transform.forward = Vector3.forward;
        NavMeshAgent nav = go.AddComponent<NavMeshAgent>();
        nav.height = BodyHeight;
        nav.baseOffset = baseOffset;
        nav.enabled = false; // no NavMesh in this fixture
        return go;
    }

    // Inert but initialized: Awake caches the agent and the memories, and the
    // disabled component skips Start's random respawn (there is nowhere to go).
    EnemyBehavior AddBehavior(GameObject go, Transform target)
    {
        EnemyBehavior behavior = go.AddComponent<EnemyBehavior>();
        behavior.enabled = false;
        behavior.target = target;
        return behavior;
    }

    EnemyWeapon CreateShooter(float baseOffset)
    {
        GameObject go = CreateAgentBody("TestShooter", 0f, baseOffset);
        EnemyWeapon weapon = go.AddComponent<EnemyWeapon>();
        go.SetActive(true);
        return weapon;
    }

    // A 1 m cube centred at the given height, TargetRange down +Z.
    Health CreateBoard(float height)
    {
        GameObject go = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        go.transform.position = new Vector3(0f, height, TargetRange);
        Physics.SyncTransforms();
        Health health = go.AddComponent<Health>();
        health.maxHealth = CombatBalance.MaxHealth;
        health.health = CombatBalance.MaxHealth;
        return health;
    }

    static bool WasHit(Health board) => board.health < board.maxHealth;

    [UnityTest]
    public IEnumerator FeetOriginBody_FiresFromMidBody_NotItsAnkles()
    {
        EnemyWeapon weapon = CreateShooter(0f);
        Health chest = CreateBoard(BodyHeight * 0.5f);
        Health ankles = CreateBoard(0f);
        yield return new WaitForFixedUpdate();

        weapon.Shoot();

        Assert.That(WasHit(chest), Is.True, "a feet-origin body must still shoot from its torso");
        Assert.That(WasHit(ankles), Is.False, "the regression: the ray left the transform origin");
    }

    [UnityTest]
    public IEnumerator LiftedOriginBody_MuzzleIsUnchanged()
    {
        // The Enemy prefab's convention. Its muzzle must not move — the trained
        // policy's accuracy was measured with it.
        EnemyWeapon weapon = CreateShooter(1f);
        Health chest = CreateBoard(BodyHeight * 0.5f);
        yield return new WaitForFixedUpdate();

        weapon.Shoot();

        Assert.That(WasHit(chest), Is.True);
    }

    // A wall spanning the ground up to `topHeight`, right in front of the body
    // at z = 10 and wide enough that neither probe gets round it.
    GameObject CreateWall(float topHeight)
    {
        GameObject wall = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        wall.transform.position = new Vector3(0f, topHeight * 0.5f, 9f);
        wall.transform.localScale = new Vector3(10f, topHeight, 1f);
        Physics.SyncTransforms();
        return wall;
    }

    [UnityTest]
    public IEnumerator ThreatEye_ReadsTheTargetsOwnBaseOffset()
    {
        // Feet-origin asker, lifted-origin target: both eyes belong at y = 2.
        GameObject targetGo = CreateAgentBody("TestTarget", 10f, 1f);
        targetGo.SetActive(true);
        GameObject askerGo = CreateAgentBody("TestAsker", 0f, 0f);
        EnemyBehavior behavior = AddBehavior(askerGo, targetGo.transform);
        askerGo.SetActive(true);

        // Just tall enough for the eye-line between two 2 m heads. Reading the
        // threat's eye off the asker's origin put it at y = 3, which clears
        // this wall a metre up.
        CreateWall(2.5f);
        yield return null;

        Assert.That(behavior.IsHiddenFromTarget(), Is.True,
            "the wall breaks the eye-line between two 2 m heads");
    }

    [UnityTest]
    public IEnumerator SightLine_AgreesWhicheverBodyIsLooking()
    {
        // The self-play pairing: a lifted-origin body (the Enemy prefab) and a
        // feet-origin one (CombatantRig's player). Both muzzles sit 1 m off the
        // ground, so the same waist-high wall has to hide each from the other.
        GameObject liftedGo = CreateAgentBody("TestLifted", 10f, 1f);
        GameObject feetGo = CreateAgentBody("TestFeet", 0f, 0f);
        EnemyBehavior lifted = AddBehavior(liftedGo, feetGo.transform);
        EnemyBehavior feet = AddBehavior(feetGo, liftedGo.transform);
        liftedGo.SetActive(true);
        feetGo.SetActive(true);
        liftedGo.transform.forward = Vector3.back; // face each other
        Physics.SyncTransforms();
        yield return null;

        Assert.That(feet.IsTargetInSight(), Is.True, "sanity: clear ground between them");
        Assert.That(lifted.IsTargetInSight(), Is.True, "sanity: and the other way round");

        CreateWall(1.5f);

        Assert.That(feet.IsTargetInSight(), Is.False,
            "the regression: this ray climbed from 1 m to the lifted body's raised origin at 2 m and cleared the wall");
        Assert.That(lifted.IsTargetInSight(), Is.False);
    }
}
