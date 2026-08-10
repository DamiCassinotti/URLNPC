using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

// The commanded-mode slot: readers see whatever the single writer last set, and
// listeners hear about real changes only.
//
// EditMode, so Awake never runs — the channel must be readable before anything
// has written to it.
public class ModeChannelTests
{
    GameObject go;
    ModeChannel channel;
    readonly List<(NpcMode previous, NpcMode current)> changes = new List<(NpcMode, NpcMode)>();

    // NUnit reuses one fixture instance for the whole class, so the capture
    // list has to be cleared alongside the channel it belongs to.
    [SetUp]
    public void SetUp()
    {
        changes.Clear();
        go = new GameObject("ModeChannelTest");
        channel = go.AddComponent<ModeChannel>();
        channel.ModeChanged += (previous, current) => changes.Add((previous, current));
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(go);
    }

    [Test]
    public void FreshChannel_StartsInHuntAndIsSilent()
    {
        Assert.That(channel.CurrentMode, Is.EqualTo(NpcMode.Hunt));
        Assert.That(changes, Is.Empty);
    }

    [Test]
    public void SetMode_UpdatesCurrentAndAnnouncesTheChange()
    {
        channel.SetMode(NpcMode.Retreat);

        Assert.That(channel.CurrentMode, Is.EqualTo(NpcMode.Retreat));
        Assert.That(changes, Is.EqualTo(new[] { (NpcMode.Hunt, NpcMode.Retreat) }));
    }

    // Re-commanding the same mode must stay silent: the mode timeline the
    // telemetry and compliance tracking read would otherwise be one event per
    // decision step.
    [Test]
    public void SetMode_ToTheSameMode_DoesNotAnnounceAnything()
    {
        channel.SetMode(NpcMode.Retreat);
        changes.Clear();

        channel.SetMode(NpcMode.Retreat);

        Assert.That(changes, Is.Empty);
    }

    [Test]
    public void SetMode_ReportsThePreviousMode()
    {
        channel.SetMode(NpcMode.Retreat);
        channel.SetMode(NpcMode.Patrol);

        Assert.That(changes, Is.EqualTo(new[]
        {
            (NpcMode.Hunt, NpcMode.Retreat),
            (NpcMode.Retreat, NpcMode.Patrol),
        }));
    }

    [Test]
    public void ResetState_ForcesTheModeAndAnnouncesRealChangesOnly()
    {
        channel.ResetState(NpcMode.Retreat);
        Assert.That(channel.CurrentMode, Is.EqualTo(NpcMode.Retreat));
        Assert.That(changes, Is.EqualTo(new[] { (NpcMode.Hunt, NpcMode.Retreat) }));

        // The mode the last episode ended on can be drawn again: no event, but
        // the write still goes through so TimeInMode restarts.
        changes.Clear();
        channel.ResetState(NpcMode.Retreat);
        Assert.That(channel.CurrentMode, Is.EqualTo(NpcMode.Retreat));
        Assert.That(changes, Is.Empty);
    }

    [Test]
    public void TimeInMode_IsNeverNegative()
    {
        channel.SetMode(NpcMode.HoldCover);

        Assert.That(channel.TimeInMode, Is.GreaterThanOrEqualTo(0f));
    }
}
