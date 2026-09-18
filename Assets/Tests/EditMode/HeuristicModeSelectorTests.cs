using System.Threading;
using NUnit.Framework;

// Pins the FSM baseline's transition table snapshot by snapshot (issue #125):
// low HP retreats over everything, a visible target is hunted, damage with no
// target holds cover, a stale or absent memory patrols, and a fresh memory is
// pursued.
public class HeuristicModeSelectorTests
{
    static GameStateSnapshot Snapshot(
        int hpPercent = 100,
        bool targetVisible = false,
        DistanceBucket targetDistance = DistanceBucket.None,
        int secondsSinceSeen = -1,
        bool recentlyDamaged = false)
    {
        return new GameStateSnapshot
        {
            hpPercent = hpPercent,
            targetVisible = targetVisible,
            targetDistance = targetDistance,
            secondsSinceSeen = secondsSinceSeen,
            recentlyDamaged = recentlyDamaged,
        };
    }

    static NpcMode Decide(GameStateSnapshot snapshot)
    {
        return new HeuristicModeSelector().Decide(snapshot);
    }

    [Test]
    public void LowHp_Retreats_WhateverElseIsGoingOn()
    {
        Assert.That(Decide(Snapshot(hpPercent: 35)), Is.EqualTo(NpcMode.Retreat));
        Assert.That(Decide(Snapshot(hpPercent: 20,
            targetVisible: true, targetDistance: DistanceBucket.Near, secondsSinceSeen: 0,
            recentlyDamaged: true)), Is.EqualTo(NpcMode.Retreat));
    }

    [Test]
    public void JustAboveTheHpThreshold_DoesNotRetreat()
    {
        Assert.That(Decide(Snapshot(hpPercent: 40,
            targetVisible: true, targetDistance: DistanceBucket.Mid, secondsSinceSeen: 0)),
            Is.EqualTo(NpcMode.Hunt));
    }

    [Test]
    public void VisibleAndHealthy_Hunts_EvenWhileDamaged()
    {
        var snapshot = Snapshot(
            targetVisible: true, targetDistance: DistanceBucket.Far, secondsSinceSeen: 0,
            recentlyDamaged: true);
        Assert.That(Decide(snapshot), Is.EqualTo(NpcMode.Hunt));
    }

    [Test]
    public void DamagedWithNoTargetVisible_HoldsCover()
    {
        // The shooter was never seen at all…
        Assert.That(Decide(Snapshot(recentlyDamaged: true)), Is.EqualTo(NpcMode.HoldCover));
        // …or the memory of them is fresh: taking fire still outranks pursuit.
        Assert.That(Decide(Snapshot(recentlyDamaged: true,
            targetDistance: DistanceBucket.Mid, secondsSinceSeen: 2)),
            Is.EqualTo(NpcMode.HoldCover));
    }

    [Test]
    public void NothingSeenForAWhile_Patrols()
    {
        // Never seen inside the horizon.
        Assert.That(Decide(Snapshot()), Is.EqualTo(NpcMode.Patrol));
        // Remembered, but the sighting went stale.
        Assert.That(Decide(Snapshot(targetDistance: DistanceBucket.Far, secondsSinceSeen: 6)),
            Is.EqualTo(NpcMode.Patrol));
    }

    [Test]
    public void AFreshMemory_IsPursued()
    {
        Assert.That(Decide(Snapshot(targetDistance: DistanceBucket.Mid, secondsSinceSeen: 5)),
            Is.EqualTo(NpcMode.Hunt));
        Assert.That(Decide(Snapshot(targetDistance: DistanceBucket.Near, secondsSinceSeen: 0,
            targetVisible: false)), Is.EqualTo(NpcMode.Hunt));
    }

    [Test]
    public void Thresholds_AreTheSelectorsOwn()
    {
        var selector = new HeuristicModeSelector
        {
            LowHealthPercent = 50,
            UnseenSecondsForPatrol = 3,
        };
        Assert.That(selector.Decide(Snapshot(hpPercent: 50)), Is.EqualTo(NpcMode.Retreat));
        Assert.That(selector.Decide(Snapshot(targetDistance: DistanceBucket.Mid, secondsSinceSeen: 3)),
            Is.EqualTo(NpcMode.Patrol));
        Assert.That(selector.Decide(Snapshot(targetDistance: DistanceBucket.Mid, secondsSinceSeen: 2)),
            Is.EqualTo(NpcMode.Hunt));
    }

    [Test]
    public void AnswersSynchronously_UnderTheAsyncSignature()
    {
        var task = new HeuristicModeSelector()
            .SelectModeAsync(Snapshot(), CancellationToken.None);
        Assert.That(task.IsCompleted, Is.True);
        Assert.That(task.Result, Is.EqualTo(NpcMode.Patrol));
    }
}
