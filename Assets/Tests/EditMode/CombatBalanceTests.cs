using NUnit.Framework;
using UnityEngine;

// Issue #71: a full-health combatant must survive six hits and die on the
// seventh. The numbers are only meaningful as a pair, so pin the pair rather
// than the constants.
public class CombatBalanceTests
{
    GameObject go;
    Health health;

    [SetUp]
    public void SetUp()
    {
        go = new GameObject("BalanceTest");
        health = go.AddComponent<Health>();
        health.maxHealth = CombatBalance.MaxHealth;
        health.health = CombatBalance.MaxHealth;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(go);
    }

    [Test]
    public void SevenShots_KillAFullHealthCombatant()
    {
        for (int shot = 1; shot <= 6; shot++)
        {
            health.DecreaseHealth(CombatBalance.ShotDamage);
            Assert.That(health.health, Is.GreaterThan(0f), $"shot {shot} of 7 must not be lethal");
        }

        health.DecreaseHealth(CombatBalance.ShotDamage);

        Assert.That(health.health, Is.LessThanOrEqualTo(0f), "the seventh shot must kill");
    }

    [Test]
    public void HealthDefaults_MatchTheBalance()
    {
        // The Enemy prefab and the binary scene both ship 100 HP bodies; the
        // hits-to-kill target is only right if the script default agrees.
        var fresh = new GameObject("Defaults").AddComponent<Health>();
        try
        {
            Assert.That(fresh.maxHealth, Is.EqualTo(CombatBalance.MaxHealth));
        }
        finally
        {
            Object.DestroyImmediate(fresh.gameObject);
        }
    }

    [Test]
    public void EnemyBehavior_ForcesTheBalanceCooldown()
    {
        // EditMode, so Awake runs on AddComponent but Start (which wants a
        // NavMesh) never does.
        var body = new GameObject("Combatant").AddComponent<EnemyBehavior>();
        try
        {
            Assert.That(body.AttackCooldown, Is.EqualTo(CombatBalance.AttackCooldown).Within(0.001f),
                "the scene's Enemy instance overrides this to 1 s — Awake must win");
        }
        finally
        {
            Object.DestroyImmediate(body.gameObject);
        }
    }

    [Test]
    public void EnemyBehavior_ForcesTheBalanceSightRange()
    {
        var body = new GameObject("Combatant").AddComponent<EnemyBehavior>();
        try
        {
            Assert.That(body.SightRange, Is.EqualTo(CombatBalance.SightRange).Within(0.001f),
                "the scene's Enemy instance may pin the old 20 m — Awake must win");
        }
        finally
        {
            Object.DestroyImmediate(body.gameObject);
        }
    }

    [Test]
    public void SightRange_CoversTheWidestSpawnSeparation()
    {
        // Twin Towers is the biggest layout at 60x60 m, so its cap is the
        // widest gap any spawn pair can open (ArenaManager.SpawnSeparationCap
        // = min half-extent * 1.4). Below that the NPC starts rounds blind.
        Assert.That(CombatBalance.SightRange, Is.GreaterThanOrEqualTo(30f * 1.4f));
    }

    [Test]
    public void FireRate_LeavesRoomForADecisionStep()
    {
        // The agent decides every 5 physics steps (0.1 s); a cooldown shorter
        // than that would make the observed "can attack" flag meaningless.
        Assert.That(CombatBalance.AttackCooldown, Is.GreaterThan(0.1f));
    }
}
