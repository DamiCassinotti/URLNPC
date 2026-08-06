using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// Hitscan weapons have no distance cap (issue #18): line of sight is the only
// thing deciding whether a shot lands. Exercised through a test subclass so aim
// is exact — EnemyWeapon's spread would miss at these ranges.
public class WeaponRangeTests : PlayModeTestBase
{
    class TestWeapon : Weapon
    {
        public Vector3 position;
        public Vector3 forward = Vector3.forward;

        protected override Vector3 GetPosition() => position;
        protected override Vector3 GetForward() => forward;
    }

    TestWeapon weapon;

    [SetUp]
    public void SetUp()
    {
        SweepTracers(); // tracers self-destruct on a timer, which may outlive a test
        weapon = Track(new GameObject("TestWeapon")).AddComponent<TestWeapon>();
    }

    static void SweepTracers()
    {
        foreach (LineRenderer tracer in Object.FindObjectsByType<LineRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Object.DestroyImmediate(tracer.gameObject);
        }
    }

    Health SpawnTargetAt(float distance)
    {
        GameObject target = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        target.transform.position = new Vector3(0f, 0f, distance);
        Physics.SyncTransforms();
        return target.AddComponent<Health>();
    }

    [UnityTest]
    public IEnumerator Shot_DamagesTarget_AtAnyDistance()
    {
        foreach (float distance in new[] { 5f, 100f, 500f })
        {
            Health target = SpawnTargetAt(distance);
            yield return new WaitForFixedUpdate();

            weapon.Shoot();

            Assert.That(target.health, Is.LessThan(target.maxHealth),
                $"a hitscan shot must land at {distance} units with clear line of sight");
            Object.DestroyImmediate(target.gameObject);
        }
    }

    [UnityTest]
    public IEnumerator Shot_IsBlockedByGeometry_NotByDistance()
    {
        Health target = SpawnTargetAt(500f);
        GameObject wall = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
        wall.transform.localScale = new Vector3(10f, 10f, 1f);
        wall.transform.position = new Vector3(0f, 0f, 20f);
        Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();

        weapon.Shoot();

        Assert.That(target.health, Is.EqualTo(target.maxHealth),
            "removing the range cap must not let shots pass through walls");
    }

    [UnityTest]
    public IEnumerator MissingShot_DrawsFiniteTracer()
    {
        yield return new WaitForFixedUpdate();

        weapon.Shoot(); // empty scene: nothing to hit

        var tracer = Object.FindAnyObjectByType<LineRenderer>();
        Assert.That(tracer, Is.Not.Null, "a missing shot still draws a tracer");
        Vector3 end = tracer.GetPosition(1);
        Assert.That(float.IsFinite(end.z), Is.True, "an unbounded raycast must not produce an infinite tracer");
        Assert.That(end.z, Is.EqualTo(100f).Within(0.01f), "default tracerMissDistance ahead of the muzzle");
    }
}
