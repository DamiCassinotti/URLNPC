using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// The scripted writer: it only ever commands enabled modes, honours the forced
// mode, and — because it draws from RunRng's own mode stream — replays the same
// timeline under the same seed without disturbing the other streams.
//
// EditMode, so Awake never runs and Tick is driven with an injected "now",
// the same way GameManager feeds RoundClock its delta.
public class ModeDirectorTests
{
    const NpcModeMask HuntOrRetreat = NpcModeMask.Hunt | NpcModeMask.Retreat;
    const NpcModeMask EveryMode = NpcModeMask.Hunt | NpcModeMask.HoldCover
                                | NpcModeMask.Retreat | NpcModeMask.Patrol;

    readonly List<GameObject> spawned = new List<GameObject>();

    [SetUp]
    public void SetUp()
    {
        RunRng.ResetForNewRun();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in spawned) Object.DestroyImmediate(go);
        spawned.Clear();
        RunRng.ResetForNewRun();
    }

    ModeDirector NewDirector(NpcModeMask enabledModes, float dwell = 1f)
    {
        var go = new GameObject("ModeDirectorTest");
        spawned.Add(go);
        go.AddComponent<ModeChannel>();
        ModeDirector director = go.AddComponent<ModeDirector>();
        director.enabledModes = enabledModes;
        director.minDwellSeconds = dwell;
        return director;
    }

    static List<NpcMode> RunTimeline(ModeDirector director, int ticks)
    {
        ModeChannel channel = director.GetComponent<ModeChannel>();
        var timeline = new List<NpcMode>();
        for (int i = 0; i < ticks; i++)
        {
            director.Tick(i);
            timeline.Add(channel.CurrentMode);
        }
        return timeline;
    }

    [Test]
    public void CommandsOnlyEnabledModes()
    {
        RunRng.EnsureInitialized(42);

        Assert.That(new HashSet<NpcMode>(RunTimeline(NewDirector(HuntOrRetreat), 200)),
            Is.SubsetOf(new[] { NpcMode.Hunt, NpcMode.Retreat }));
    }

    [Test]
    public void EventuallyCommandsEveryEnabledMode()
    {
        RunRng.EnsureInitialized(42);

        Assert.That(new HashSet<NpcMode>(RunTimeline(NewDirector(EveryMode), 400)),
            Is.EquivalentTo(NpcModes.All));
    }

    [Test]
    public void ForcedMode_IgnoresTheScheduleAndTheEnabledMask()
    {
        RunRng.EnsureInitialized(42);
        ModeDirector director = NewDirector(HuntOrRetreat);
        director.useForcedMode = true;
        director.forcedMode = NpcMode.Patrol;

        Assert.That(RunTimeline(director, 50), Is.All.EqualTo(NpcMode.Patrol));
    }

    [Test]
    public void HoldsTheCommandedModeForTheDwellInterval()
    {
        RunRng.EnsureInitialized(42);
        ModeDirector director = NewDirector(HuntOrRetreat, dwell: 10f);
        ModeChannel channel = director.GetComponent<ModeChannel>();

        director.Tick(0f);
        NpcMode first = channel.CurrentMode;
        for (float t = 0.5f; t < 10f; t += 0.5f) director.Tick(t);

        Assert.That(channel.CurrentMode, Is.EqualTo(first));
    }

    [Test]
    public void EmptyMask_LeavesTheChannelAlone()
    {
        RunRng.EnsureInitialized(42);
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ModeDirector"));
        ModeDirector director = NewDirector(NpcModeMask.None);
        director.GetComponent<ModeChannel>().SetMode(NpcMode.HoldCover);

        Assert.That(RunTimeline(director, 20), Is.All.EqualTo(NpcMode.HoldCover));
    }

    // The fresh episode must be commanded a mode before the agent's first
    // observation, not on the director's next tick.
    [Test]
    public void ResetState_CommandsAFreshModeImmediately()
    {
        RunRng.EnsureInitialized(42);
        ModeDirector director = NewDirector(HuntOrRetreat, dwell: 30f);
        ModeChannel channel = director.GetComponent<ModeChannel>();
        director.Tick(0f);
        channel.SetMode(NpcMode.Patrol); // not in the pool, so it can only be left over

        director.ResetState(1f);

        // Without the immediate draw the dwell would hold Patrol until t=30.
        Assert.That(channel.CurrentMode, Is.Not.EqualTo(NpcMode.Patrol));
    }

    // Nothing to draw is still an episode boundary: the channel has to start a
    // fresh interval, or the new episode reads the old one's dwell.
    [Test]
    public void ResetState_StartsANewIntervalEvenWithNothingToDraw()
    {
        RunRng.EnsureInitialized(42);
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ModeDirector"));
        ModeDirector director = NewDirector(NpcModeMask.None);
        ModeChannel channel = director.GetComponent<ModeChannel>();
        channel.SetMode(NpcMode.HoldCover);
        var intervals = new List<(NpcMode, NpcMode)>();
        channel.ModeChanged += (previous, current) => intervals.Add((previous, current));

        director.ResetState(1f);

        Assert.That(channel.CurrentMode, Is.EqualTo(NpcMode.HoldCover));
        Assert.That(intervals, Is.EqualTo(new[] { (NpcMode.HoldCover, NpcMode.HoldCover) }));
    }

    [Test]
    public void SameSeed_ReplaysTheSameModeTimeline()
    {
        RunRng.EnsureInitialized(42);
        List<NpcMode> first = RunTimeline(NewDirector(EveryMode), 100);

        RunRng.ResetForNewRun();
        RunRng.EnsureInitialized(42);

        Assert.That(RunTimeline(NewDirector(EveryMode), 100), Is.EqualTo(first));
    }

    // Mode draws happen a policy-dependent number of times; they must not shift
    // the spawn sequence a replayed run depends on.
    [Test]
    public void ModeDraws_DoNotShiftTheSpawnStream()
    {
        RunRng.EnsureInitialized(42);
        var baseline = new int[10];
        for (int i = 0; i < baseline.Length; i++) baseline[i] = RunRng.Range(RunRng.Stream.Spawn, 0, int.MaxValue);

        RunRng.ResetForNewRun();
        RunRng.EnsureInitialized(42);
        RunTimeline(NewDirector(HuntOrRetreat), 137);

        for (int i = 0; i < baseline.Length; i++)
        {
            Assert.That(RunRng.Range(RunRng.Stream.Spawn, 0, int.MaxValue), Is.EqualTo(baseline[i]));
        }
    }
}
