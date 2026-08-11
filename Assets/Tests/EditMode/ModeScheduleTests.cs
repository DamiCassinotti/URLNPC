using System.Collections.Generic;
using NUnit.Framework;

// The scripted director's sampling rules: a mode is held for at least the dwell
// interval, draws come from the enabled pool only, and repeats are allowed.
public class ModeScheduleTests
{
    readonly List<int> requestedCounts = new List<int>();

    // NUnit reuses one fixture instance for the whole class.
    [SetUp]
    public void SetUp()
    {
        requestedCounts.Clear();
    }

    ModeSchedule Schedule(float dwell, params NpcMode[] pool)
    {
        return new ModeSchedule { MinDwellSeconds = dwell, Pool = pool };
    }

    // Records the pool size it was asked for, so the tests can check the
    // schedule draws against the whole pool.
    System.Func<int, int> Picking(params int[] indices)
    {
        int call = 0;
        return count =>
        {
            requestedCounts.Add(count);
            int index = call < indices.Length ? call : indices.Length - 1;
            call++;
            return indices[index];
        };
    }

    [Test]
    public void FirstTick_DrawsImmediately()
    {
        ModeSchedule schedule = Schedule(5f, NpcMode.Hunt, NpcMode.Retreat);

        Assert.That(schedule.HasDrawn, Is.False);
        Assert.That(schedule.TryAdvance(0f, Picking(1)), Is.True);
        Assert.That(schedule.Current, Is.EqualTo(NpcMode.Retreat));
        Assert.That(requestedCounts, Is.EqualTo(new[] { 2 }));
    }

    [Test]
    public void DoesNotDrawAgainBeforeTheDwellElapses()
    {
        ModeSchedule schedule = Schedule(5f, NpcMode.Hunt, NpcMode.Retreat);
        schedule.TryAdvance(10f, Picking(0));

        Assert.That(schedule.TryAdvance(14.9f, Picking(1)), Is.False);
        Assert.That(schedule.Current, Is.EqualTo(NpcMode.Hunt));
    }

    [Test]
    public void DrawsAgainOnceTheDwellElapses()
    {
        ModeSchedule schedule = Schedule(5f, NpcMode.Hunt, NpcMode.Retreat);
        schedule.TryAdvance(10f, Picking(0));

        Assert.That(schedule.TryAdvance(15f, Picking(1)), Is.True);
        Assert.That(schedule.Current, Is.EqualTo(NpcMode.Retreat));
    }

    // A repeat is a draw, not a no-op: the dwell restarts from it, so the mode
    // is held another full interval instead of being resampled immediately.
    [Test]
    public void RepeatedDraw_KeepsTheModeAndRearmsTheDwell()
    {
        ModeSchedule schedule = Schedule(5f, NpcMode.Hunt, NpcMode.Retreat);
        schedule.TryAdvance(0f, Picking(0));

        Assert.That(schedule.TryAdvance(5f, Picking(0)), Is.True);
        Assert.That(schedule.Current, Is.EqualTo(NpcMode.Hunt));
        Assert.That(schedule.TryAdvance(9f, Picking(1)), Is.False);
        Assert.That(schedule.TryAdvance(10f, Picking(1)), Is.True);
        Assert.That(schedule.Current, Is.EqualTo(NpcMode.Retreat));
    }

    [Test]
    public void EmptyPool_NeverDraws()
    {
        ModeSchedule schedule = Schedule(0f);

        Assert.That(schedule.TryAdvance(0f, Picking(0)), Is.False);
        Assert.That(schedule.TryAdvance(100f, Picking(0)), Is.False);
        Assert.That(schedule.HasDrawn, Is.False);
        Assert.That(requestedCounts, Is.Empty);
    }

    [Test]
    public void NonPositiveDwell_DrawsEveryTick()
    {
        ModeSchedule schedule = Schedule(0f, NpcMode.Hunt, NpcMode.Retreat);

        Assert.That(schedule.TryAdvance(0f, Picking(0)), Is.True);
        Assert.That(schedule.TryAdvance(0f, Picking(1)), Is.True);
        Assert.That(schedule.Current, Is.EqualTo(NpcMode.Retreat));
    }

    [Test]
    public void DrawsOnlyFromThePool()
    {
        ModeSchedule schedule = Schedule(0f, NpcMode.HoldCover, NpcMode.Patrol);
        var seen = new HashSet<NpcMode>();
        var rng = new System.Random(7);

        for (int i = 0; i < 200; i++)
        {
            schedule.TryAdvance(i, count => rng.Next(0, count));
            seen.Add(schedule.Current);
        }

        Assert.That(seen, Is.EquivalentTo(new[] { NpcMode.HoldCover, NpcMode.Patrol }));
    }

    // Episode resets: the fresh episode draws on its first tick instead of
    // finishing the dwell the previous one left running.
    [Test]
    public void Reset_ArmsAnImmediateDraw()
    {
        ModeSchedule schedule = Schedule(5f, NpcMode.Hunt, NpcMode.Retreat);
        schedule.TryAdvance(10f, Picking(0));

        schedule.Reset();
        Assert.That(schedule.HasDrawn, Is.False);
        Assert.That(schedule.TryAdvance(11f, Picking(1)), Is.True);
        Assert.That(schedule.Current, Is.EqualTo(NpcMode.Retreat));
    }
}
