using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// The balance only holds if it survives deserialization: the FPS scene's Enemy
// instance overrides attackCooldown and the weapon damage, and CombatantRig
// composes the player side from the script defaults. Both paths go through
// Awake, so that is what these pin (issue #71).
public class CombatBalanceForcingTests : PlayModeTestBase
{
    [UnityTest]
    public IEnumerator EnemyWeapon_DealsTheBalanceDamage()
    {
        // Close enough that the aim spread can't take the shot off a 1 m cube.
        Health target = CreateCombatant("Player", new Vector3(0f, 0f, 5f), CombatBalance.MaxHealth);
        GameObject shooter = Track(new GameObject("Shooter"));
        shooter.transform.position = Vector3.zero;
        shooter.transform.forward = Vector3.forward;
        Physics.SyncTransforms();
        var weapon = shooter.AddComponent<EnemyWeapon>();
        yield return new WaitForFixedUpdate();

        weapon.Shoot();

        Assert.That(CombatBalance.MaxHealth - target.health,
            Is.EqualTo(CombatBalance.ShotDamage).Within(0.01f),
            "Awake must force the balance damage over whatever the scene serialized");
    }

    [Test]
    public void EnemyBehavior_UsesTheBalanceCooldown()
    {
        var body = Track(new GameObject("Combatant")).AddComponent<EnemyBehavior>();

        Assert.That(body.AttackCooldown, Is.EqualTo(CombatBalance.AttackCooldown).Within(0.001f));
    }
}
