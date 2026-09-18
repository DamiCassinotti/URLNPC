using NUnit.Framework;
using UnityEngine;

// GameStateSnapshot.Build: the LLM tier's bucketing and rounding rules (#123).
// The values are decided at build time, not in the prompt formatter, because
// the prompt labels have to be written against the numbers the model sees.
public class GameStateSnapshotTests
{
    // Full health, never seen, empty round context — each test overrides what
    // it is about.
    static GameStateInput Input()
    {
        return new GameStateInput
        {
            health = 100f,
            maxHealth = 100f,
            timeSinceSeen = Mathf.Infinity,
            seenHorizonSeconds = 10f,
            arenaName = "Courtyard",
        };
    }

    [TestCase(100f, 100)]
    [TestCase(85f, 85)]
    [TestCase(87f, 85)]
    [TestCase(88f, 90)]
    [TestCase(0f, 0)]
    public void HpPercent_RoundsToStepsOfFive(float health, int expected)
    {
        GameStateInput input = Input();
        input.health = health;

        Assert.That(GameStateSnapshot.Build(input).hpPercent, Is.EqualTo(expected));
    }

    [Test]
    public void HpPercent_ClampsAndSurvivesAZeroMaxHealth()
    {
        GameStateInput input = Input();
        input.health = 150f;
        Assert.That(GameStateSnapshot.Build(input).hpPercent, Is.EqualTo(100));

        input.health = -10f;
        Assert.That(GameStateSnapshot.Build(input).hpPercent, Is.EqualTo(0));

        input.maxHealth = 0f;
        Assert.That(GameStateSnapshot.Build(input).hpPercent, Is.EqualTo(0),
            "no division by zero");
    }

    [TestCase(0f, DistanceBucket.Near)]
    [TestCase(10f, DistanceBucket.Near)]
    [TestCase(10.1f, DistanceBucket.Mid)]
    [TestCase(25f, DistanceBucket.Mid)]
    [TestCase(25.1f, DistanceBucket.Far)]
    [TestCase(45f, DistanceBucket.Far)]
    public void VisibleTarget_BucketsTheDistance(float metres, DistanceBucket expected)
    {
        GameStateInput input = Input();
        input.targetVisible = true;
        input.hasEverSeen = true;
        input.timeSinceSeen = 0f;
        input.distanceToLastSeen = metres;

        GameStateSnapshot snapshot = GameStateSnapshot.Build(input);
        Assert.That(snapshot.targetDistance, Is.EqualTo(expected));
        Assert.That(snapshot.secondsSinceSeen, Is.Zero, "visible means seen right now");
    }

    [Test]
    public void RememberedSighting_InsideTheHorizon_KeepsBucketAndAge()
    {
        GameStateInput input = Input();
        input.hasEverSeen = true;
        input.timeSinceSeen = 3.4f;
        input.distanceToLastSeen = 18f;

        GameStateSnapshot snapshot = GameStateSnapshot.Build(input);
        Assert.That(snapshot.targetVisible, Is.False);
        Assert.That(snapshot.targetDistance, Is.EqualTo(DistanceBucket.Mid));
        Assert.That(snapshot.secondsSinceSeen, Is.EqualTo(3));
    }

    [Test]
    public void NeverSeen_IsTheSingleFixedPoint()
    {
        GameStateSnapshot snapshot = GameStateSnapshot.Build(Input());

        Assert.That(snapshot.targetVisible, Is.False);
        Assert.That(snapshot.targetDistance, Is.EqualTo(DistanceBucket.None));
        Assert.That(snapshot.secondsSinceSeen, Is.EqualTo(-1));
    }

    [Test]
    public void SightingOlderThanTheHorizon_LapsesToNeverSeen()
    {
        // PerceptionMemory lapses on its own refresh, but a snapshot built
        // before that refresh has run must read never-seen all the same.
        GameStateInput input = Input();
        input.hasEverSeen = true;
        input.timeSinceSeen = 10.5f;
        input.distanceToLastSeen = 5f;

        GameStateSnapshot snapshot = GameStateSnapshot.Build(input);
        Assert.That(snapshot.targetDistance, Is.EqualTo(DistanceBucket.None));
        Assert.That(snapshot.secondsSinceSeen, Is.EqualTo(-1));
    }

    [Test]
    public void SightingAtExactlyTheHorizon_StillCounts()
    {
        GameStateInput input = Input();
        input.hasEverSeen = true;
        input.timeSinceSeen = 10f;
        input.distanceToLastSeen = 30f;

        GameStateSnapshot snapshot = GameStateSnapshot.Build(input);
        Assert.That(snapshot.targetDistance, Is.EqualTo(DistanceBucket.Far));
        Assert.That(snapshot.secondsSinceSeen, Is.EqualTo(10));
    }

    [Test]
    public void DamageMemory_PassesThroughFlagAndDirection()
    {
        GameStateInput input = Input();
        input.recentlyDamaged = true;
        input.hitDirection = HitDirection.Back;

        GameStateSnapshot snapshot = GameStateSnapshot.Build(input);
        Assert.That(snapshot.recentlyDamaged, Is.True);
        Assert.That(snapshot.damagedFrom, Is.EqualTo(HitDirection.Back));
    }

    [Test]
    public void RoundClock_CeilsLikeTheHudAndNeverGoesNegative()
    {
        GameStateInput input = Input();
        input.roundTimeRemaining = 42.3f;
        Assert.That(GameStateSnapshot.Build(input).roundSecondsRemaining, Is.EqualTo(43));

        input.roundTimeRemaining = -5f;
        Assert.That(GameStateSnapshot.Build(input).roundSecondsRemaining, Is.Zero,
            "a disabled or expired clock reads as no time, not negative time");
    }

    [Test]
    public void ScoreAndArena_PassThrough()
    {
        GameStateInput input = Input();
        input.playerWins = 3;
        input.npcWins = 2;
        input.draws = 1;
        input.arenaIndex = 4;
        input.arenaName = "Ramparts";
        input.coverDensity = 0.128f;

        GameStateSnapshot snapshot = GameStateSnapshot.Build(input);
        Assert.That(snapshot.playerWins, Is.EqualTo(3));
        Assert.That(snapshot.npcWins, Is.EqualTo(2));
        Assert.That(snapshot.draws, Is.EqualTo(1));
        Assert.That(snapshot.arenaIndex, Is.EqualTo(4));
        Assert.That(snapshot.arenaName, Is.EqualTo("Ramparts"));
        Assert.That(snapshot.coverDensity, Is.EqualTo(0.13f).Within(1e-5f));
    }

    [Test]
    public void ObservedStats_AreRoundedCoarse()
    {
        GameStateInput input = Input();
        input.observedSeconds = 2.34f;
        input.meanEngagementDistance = 17.6f;
        input.shotsPer10Seconds = 1.26f;
        input.observedMeanSpeed = 3.14f;

        GameStateSnapshot snapshot = GameStateSnapshot.Build(input);
        Assert.That(snapshot.observedSeconds, Is.EqualTo(2.3f).Within(1e-5f));
        Assert.That(snapshot.meanEngagementDistanceMetres, Is.EqualTo(18));
        Assert.That(snapshot.shotsHeardPer10Seconds, Is.EqualTo(1.3f).Within(1e-5f));
        Assert.That(snapshot.observedMeanSpeed, Is.EqualTo(3.1f).Within(1e-5f));
    }

    [Test]
    public void ModeAndDwell_RoundToWholeSeconds()
    {
        GameStateInput input = Input();
        input.mode = NpcMode.Retreat;
        input.timeInMode = 4.6f;

        GameStateSnapshot snapshot = GameStateSnapshot.Build(input);
        Assert.That(snapshot.mode, Is.EqualTo(NpcMode.Retreat));
        Assert.That(snapshot.secondsInMode, Is.EqualTo(5));
    }
}
