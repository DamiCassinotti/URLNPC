using NUnit.Framework;
using UnityEngine;

// Health drives the whole reward/win-loss chain — EnemyAgent subscribes to
// OnDamaged/OnDied and GameManager decides the round from deaths — so its event
// semantics are load-bearing.
//
// EditMode, so Start() never runs: the GameManager lookup stays null and
// CheckDeath's null guard keeps death processing local to the component.
public class HealthTests
{
    GameObject go;
    Health health;

    [SetUp]
    public void SetUp()
    {
        go = new GameObject("HealthTest");
        health = go.AddComponent<Health>();
        health.maxHealth = 100f;
        health.health = 100f;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(go);
    }

    [Test]
    public void DecreaseHealth_SubtractsAndFiresOnDamagedWithAmount()
    {
        float reported = -1f;
        health.OnDamaged += amount => reported = amount;

        health.DecreaseHealth(30f);

        Assert.That(health.health, Is.EqualTo(70f));
        Assert.That(reported, Is.EqualTo(30f));
    }

    [Test]
    public void LethalDamage_FiresOnDiedOnce_AndFurtherDamageIsIgnored()
    {
        int deaths = 0;
        int damagedEvents = 0;
        health.OnDied += () => deaths++;
        health.OnDamaged += _ => damagedEvents++;

        health.DecreaseHealth(150f);
        float healthAtDeath = health.health;
        health.DecreaseHealth(10f);

        Assert.That(deaths, Is.EqualTo(1));
        Assert.That(damagedEvents, Is.EqualTo(1), "damage while dead must not fire events");
        Assert.That(health.health, Is.EqualTo(healthAtDeath), "damage while dead must not change health");
    }

    [Test]
    public void ExactlyZeroHealth_CountsAsDead()
    {
        int deaths = 0;
        health.OnDied += () => deaths++;

        health.DecreaseHealth(100f);

        Assert.That(deaths, Is.EqualTo(1));
    }

    [Test]
    public void ResetHealth_RestoresMaxAndReArmsDeath()
    {
        int deaths = 0;
        health.OnDied += () => deaths++;
        health.DecreaseHealth(200f);

        health.ResetHealth();
        Assert.That(health.health, Is.EqualTo(health.maxHealth));

        health.DecreaseHealth(200f);
        Assert.That(deaths, Is.EqualTo(2), "a reset entity must be able to die again");
    }
}
