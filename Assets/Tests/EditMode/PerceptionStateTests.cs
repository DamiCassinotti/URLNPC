using NUnit.Framework;
using UnityEngine;

// The remember/freeze state machine behind the sensory contract, as pure state
// with injected time. PerceptionContractTests covers the same rules end to end
// with real raycasts.
public class PerceptionStateTests
{
    [Test]
    public void FreshState_HasSeenNothing()
    {
        var state = new PerceptionState();
        Assert.That(state.CurrentlyVisible, Is.False);
        Assert.That(state.HasEverSeen, Is.False);
        Assert.That(state.TimeSinceSeen(123f), Is.EqualTo(Mathf.Infinity));
        Assert.That(state.LastSeenPosition, Is.EqualTo(Vector3.zero));
    }

    [Test]
    public void Sighting_RecordsPositionAndTime()
    {
        var state = new PerceptionState();
        var pos = new Vector3(1f, 0f, 2f);

        state.Observe(true, pos, now: 10f);

        Assert.That(state.CurrentlyVisible, Is.True);
        Assert.That(state.HasEverSeen, Is.True);
        Assert.That(state.LastSeenPosition, Is.EqualTo(pos));
        Assert.That(state.TimeSinceSeen(10f), Is.Zero);
        Assert.That(state.TimeSinceSeen(12.5f), Is.EqualTo(2.5f));
    }

    [Test]
    public void WhileVisible_PositionTracksTheTarget()
    {
        var state = new PerceptionState();
        state.Observe(true, new Vector3(1f, 0f, 2f), 10f);
        state.Observe(true, new Vector3(3f, 0f, 4f), 11f);

        Assert.That(state.LastSeenPosition, Is.EqualTo(new Vector3(3f, 0f, 4f)));
        Assert.That(state.TimeSinceSeen(11f), Is.Zero);
    }

    [Test]
    public void LosingSight_FreezesTheMemory()
    {
        var state = new PerceptionState();
        var lastSighting = new Vector3(1f, 0f, 2f);
        state.Observe(true, lastSighting, 10f);

        // Out of sight: whatever position the caller passes must be ignored.
        state.Observe(false, new Vector3(99f, 0f, 99f), 11f);

        Assert.That(state.CurrentlyVisible, Is.False);
        Assert.That(state.HasEverSeen, Is.True, "memory outlives sight");
        Assert.That(state.LastSeenPosition, Is.EqualTo(lastSighting), "position freezes at the last sighting");
        Assert.That(state.TimeSinceSeen(13f), Is.EqualTo(3f), "the sighting ages while blind");
    }

    [Test]
    public void Forget_ResetsToNeverSeen()
    {
        var state = new PerceptionState();
        state.Observe(true, new Vector3(1f, 0f, 2f), 10f);

        state.Forget();

        Assert.That(state.CurrentlyVisible, Is.False);
        Assert.That(state.HasEverSeen, Is.False);
        Assert.That(state.LastSeenPosition, Is.EqualTo(Vector3.zero));
        Assert.That(state.TimeSinceSeen(11f), Is.EqualTo(Mathf.Infinity));
    }
}
