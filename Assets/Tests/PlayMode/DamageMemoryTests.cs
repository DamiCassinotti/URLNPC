using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// The adapter half of the recently-damaged memory, end to end from a real shot:
// a weapon raycast has to land as "hit, from that quarter" on the victim. The
// decay and bucketing rules themselves are DamageStateTests.
//
// PlayMode because it needs Awake (the Health subscription) and real colliders.
public class DamageMemoryTests : PlayModeTestBase
{
    DamageMemory AttachMemory(Health victim)
    {
        return victim.gameObject.AddComponent<DamageMemory>();
    }

    [UnityTest]
    public IEnumerator ShotFromBehind_IsRememberedAsBack()
    {
        Health victim = CreateCombatant("Player", Vector3.zero, 100f);
        victim.transform.forward = Vector3.forward;
        DamageMemory memory = AttachMemory(victim);
        Weapon weapon = CreateWeapon("NPC", new Vector3(0f, 0f, -10f));
        Physics.SyncTransforms();

        weapon.Shoot();
        yield return null;

        Assert.That(victim.health, Is.LessThan(100f), "the test shot must actually connect");
        Assert.That(memory.RecentlyDamaged, Is.True);
        Assert.That(memory.LastHitDirection, Is.EqualTo(HitDirection.Back));
    }

    [UnityTest]
    public IEnumerator TheBucketFollowsTheVictimsFacing()
    {
        Health victim = CreateCombatant("Player", Vector3.zero, 100f);
        // Turned a quarter turn: the same shot now comes from the victim's
        // right rather than its back.
        victim.transform.forward = Vector3.right;
        DamageMemory memory = AttachMemory(victim);
        Weapon weapon = CreateWeapon("NPC", new Vector3(0f, 0f, -10f));
        Physics.SyncTransforms();

        weapon.Shoot();
        yield return null;

        Assert.That(memory.LastHitDirection, Is.EqualTo(HitDirection.Right));
    }

    [UnityTest]
    public IEnumerator DamageWithNoShooter_SetsTheFlagWithoutADirection()
    {
        Health victim = CreateCombatant("NPC", Vector3.zero, 100f);
        DamageMemory memory = AttachMemory(victim);
        yield return null;

        victim.DecreaseHealth(10f);

        Assert.That(memory.RecentlyDamaged, Is.True);
        Assert.That(memory.LastHitDirection, Is.EqualTo(HitDirection.None));
    }

    [UnityTest]
    public IEnumerator Forget_WipesTheMemoryForTheNextEpisode()
    {
        Health victim = CreateCombatant("NPC", Vector3.zero, 100f);
        DamageMemory memory = AttachMemory(victim);
        Weapon weapon = CreateWeapon("Player", new Vector3(0f, 0f, -10f));
        Physics.SyncTransforms();

        weapon.Shoot();
        yield return null;
        Assert.That(memory.RecentlyDamaged, Is.True);

        memory.Forget();

        Assert.That(memory.RecentlyDamaged, Is.False);
        Assert.That(memory.LastHitDirection, Is.EqualTo(HitDirection.None));
        Assert.That(memory.TimeSinceDamaged, Is.EqualTo(Mathf.Infinity));
    }
}
