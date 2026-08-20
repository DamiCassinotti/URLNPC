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

    // A shooter with the given agent origin convention, aimed along +Z.
    EnemyWeapon CreateShooter(float baseOffset)
    {
        GameObject go = Track(new GameObject("TestShooter"));
        go.SetActive(false);
        go.transform.position = new Vector3(0f, baseOffset, 0f);
        go.transform.forward = Vector3.forward;
        NavMeshAgent nav = go.AddComponent<NavMeshAgent>();
        nav.height = BodyHeight;
        nav.baseOffset = baseOffset;
        nav.enabled = false; // no NavMesh in this fixture
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

    [UnityTest]
    public IEnumerator ThreatEye_ReadsTheTargetsOwnBaseOffset()
    {
        // Feet-origin asker, lifted-origin target: both eyes belong at y = 2.
        GameObject targetGo = Track(new GameObject("TestTarget"));
        targetGo.SetActive(false);
        targetGo.transform.position = new Vector3(0f, 1f, 10f);
        NavMeshAgent targetNav = targetGo.AddComponent<NavMeshAgent>();
        targetNav.height = BodyHeight;
        targetNav.baseOffset = 1f;
        targetNav.enabled = false;
        targetGo.SetActive(true);

        GameObject askerGo = Track(new GameObject("TestAsker"));
        askerGo.SetActive(false);
        askerGo.transform.position = Vector3.zero;
        NavMeshAgent nav = askerGo.AddComponent<NavMeshAgent>();
        nav.height = BodyHeight;
        nav.baseOffset = 0f;
        nav.enabled = false;
        EnemyBehavior behavior = askerGo.AddComponent<EnemyBehavior>();
        behavior.enabled = false; // skip Start's random respawn: there is no NavMesh
        behavior.target = targetGo.transform;
        askerGo.SetActive(true);

        // Right in front of the target, and just tall enough for the eye-line
        // between two 2 m heads. Reading the threat's eye off the asker's
        // origin put it at y = 3, which clears this wall a metre up.
        GameObject wall = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        wall.transform.position = new Vector3(0f, 1.25f, 9f);
        wall.transform.localScale = new Vector3(10f, 2.5f, 1f);
        Physics.SyncTransforms();
        yield return null;

        Assert.That(behavior.IsHiddenFromTarget(), Is.True,
            "the wall breaks the eye-line between two 2 m heads");
    }
}
