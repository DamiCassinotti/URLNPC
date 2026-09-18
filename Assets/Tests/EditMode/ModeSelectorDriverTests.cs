using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// The async writer's loop (issue #124), driven with an injected "now" the way
// ModeDirectorTests drive the director: a pending answer never blocks or
// writes, a failed one keeps the channel's mode, a stale one is cancelled
// rather than queued, and the scripted director outranks the driver on the
// shared channel. The cadence rules themselves are SelectorScheduleTests.
public class ModeSelectorDriverTests
{
    readonly List<GameObject> spawned = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in spawned) Object.DestroyImmediate(go);
        spawned.Clear();
    }

    // Hands out tasks the test completes by hand, recording what it was given.
    class ScriptedSelector : IModeSelector
    {
        public readonly List<TaskCompletionSource<NpcMode>> Pending = new List<TaskCompletionSource<NpcMode>>();
        public readonly List<CancellationToken> Tokens = new List<CancellationToken>();
        public readonly List<GameStateSnapshot> Snapshots = new List<GameStateSnapshot>();

        public int Calls => Pending.Count;

        public Task<NpcMode> SelectModeAsync(GameStateSnapshot snapshot, CancellationToken cancellation)
        {
            Snapshots.Add(snapshot);
            Tokens.Add(cancellation);
            var completion = new TaskCompletionSource<NpcMode>();
            Pending.Add(completion);
            return completion.Task;
        }
    }

    ModeSelectorDriver NewDriver(out ModeChannel channel, out ScriptedSelector selector)
    {
        var go = new GameObject("ModeSelectorDriverTest");
        spawned.Add(go);
        channel = go.AddComponent<ModeChannel>();
        ModeSelectorDriver driver = go.AddComponent<ModeSelectorDriver>();
        selector = new ScriptedSelector();
        driver.Selector = selector;
        return driver;
    }

    static void ExpectWarning()
    {
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ModeSelector"));
    }

    [Test]
    public void APendingAnswer_NeitherBlocksNorWrites()
    {
        ModeSelectorDriver driver = NewDriver(out ModeChannel channel, out ScriptedSelector selector);

        driver.Tick(0f);
        Assert.That(selector.Calls, Is.EqualTo(1), "the first tick issues immediately");
        Assert.That(channel.CurrentMode, Is.EqualTo(NpcMode.Hunt), "no answer yet, no write");

        driver.Tick(1f);
        driver.Tick(2f);
        Assert.That(selector.Calls, Is.EqualTo(1), "one call per decision, not per tick");
    }

    [Test]
    public void TheAnswer_LandsOnTheTickAfterItCompletes()
    {
        ModeSelectorDriver driver = NewDriver(out ModeChannel channel, out ScriptedSelector selector);

        driver.Tick(0f);
        selector.Pending[0].SetResult(NpcMode.Retreat);
        driver.Tick(1f);

        Assert.That(channel.CurrentMode, Is.EqualTo(NpcMode.Retreat));
    }

    [Test]
    public void AFaultedAnswer_KeepsTheMode()
    {
        ModeSelectorDriver driver = NewDriver(out ModeChannel channel, out ScriptedSelector selector);
        ExpectWarning();

        driver.Tick(0f);
        selector.Pending[0].SetException(new System.InvalidOperationException("llm down"));
        driver.Tick(1f);

        Assert.That(channel.CurrentMode, Is.EqualTo(NpcMode.Hunt));
    }

    [Test]
    public void AnAnswerNamingNoMode_KeepsTheMode()
    {
        ModeSelectorDriver driver = NewDriver(out ModeChannel channel, out ScriptedSelector selector);
        ExpectWarning();

        driver.Tick(0f);
        selector.Pending[0].SetResult((NpcMode)99);
        driver.Tick(1f);

        Assert.That(channel.CurrentMode, Is.EqualTo(NpcMode.Hunt));
    }

    [Test]
    public void ASelectorThatThrowsSynchronously_KeepsTheModeAndIsNotRetriedEveryTick()
    {
        var go = new GameObject("ThrowingSelectorTest");
        spawned.Add(go);
        ModeChannel channel = go.AddComponent<ModeChannel>();
        ModeSelectorDriver driver = go.AddComponent<ModeSelectorDriver>();
        int calls = 0;
        driver.Selector = new ThrowingSelector(() => calls++);
        ExpectWarning();

        driver.Tick(0f);
        driver.Tick(0.5f);
        driver.Tick(1f);

        Assert.That(calls, Is.EqualTo(1), "a failed attempt still consumed its decision");
        Assert.That(channel.CurrentMode, Is.EqualTo(NpcMode.Hunt));
    }

    class ThrowingSelector : IModeSelector
    {
        readonly System.Action onCall;
        public ThrowingSelector(System.Action onCall) { this.onCall = onCall; }

        public Task<NpcMode> SelectModeAsync(GameStateSnapshot snapshot, CancellationToken cancellation)
        {
            onCall();
            throw new System.InvalidOperationException("boom");
        }
    }

    [Test]
    public void UnansweredAtTheNextDecision_IsCancelledNotQueued()
    {
        ModeSelectorDriver driver = NewDriver(out ModeChannel channel, out ScriptedSelector selector);
        ExpectWarning(); // the timeout

        driver.Tick(0f);
        driver.Tick(5f); // default period

        Assert.That(selector.Calls, Is.EqualTo(2));
        Assert.That(selector.Tokens[0].IsCancellationRequested, Is.True, "the stale call was told to stop");
        Assert.That(selector.Tokens[1].IsCancellationRequested, Is.False);

        // The cancelled call answering late must not reach the channel.
        selector.Pending[0].SetResult(NpcMode.Patrol);
        driver.Tick(6f);
        Assert.That(channel.CurrentMode, Is.EqualTo(NpcMode.Hunt));
    }

    [Test]
    public void RepeatedFailures_HandTheChannelToTheFallback()
    {
        ModeSelectorDriver driver = NewDriver(out ModeChannel channel, out ScriptedSelector selector);
        driver.failuresBeforeFallback = 2;
        var fallback = new ScriptedSelector();
        driver.Fallback = fallback;
        ExpectWarning(); // failure 1
        ExpectWarning(); // failure 2
        ExpectWarning(); // the takeover notice

        driver.Tick(0f);
        selector.Pending[0].SetException(new System.Exception("down"));
        driver.Tick(5f); // failure 1 applied, primary asked again
        selector.Pending[1].SetException(new System.Exception("still down"));
        driver.Tick(10f); // failure 2 applied — the fallback takes this decision

        Assert.That(selector.Calls, Is.EqualTo(2));
        Assert.That(fallback.Calls, Is.EqualTo(1));

        fallback.Pending[0].SetResult(NpcMode.HoldCover);
        driver.Tick(11f);
        Assert.That(channel.CurrentMode, Is.EqualTo(NpcMode.HoldCover));
    }

    // The mutual exclusion the shared channel depends on: the scripted
    // director outranks the driver, exactly as the director stands down
    // outside training.
    [Test]
    public void AnActiveDirector_OutranksTheDriver()
    {
        var go = new GameObject("SharedChannelTest");
        spawned.Add(go);
        go.AddComponent<ModeChannel>();
        ModeDirector director = go.AddComponent<ModeDirector>();
        director.trainingOnly = false; // writes outside training, so it holds the channel
        ModeSelectorDriver driver = go.AddComponent<ModeSelectorDriver>();
        var selector = new ScriptedSelector();
        driver.Selector = selector;

        Assert.That(driver.IsWriter, Is.False);
        driver.Tick(0f);
        Assert.That(selector.Calls, Is.Zero, "the driver must not ask while the director writes");

        director.trainingOnly = true; // no communicator here, so the director stands down
        Assert.That(driver.IsWriter, Is.True);
        driver.Tick(1f);
        Assert.That(selector.Calls, Is.EqualTo(1));
    }

    [Test]
    public void NoSelectorAndKindNone_IsInert()
    {
        var go = new GameObject("InertDriverTest");
        spawned.Add(go);
        ModeChannel channel = go.AddComponent<ModeChannel>();
        ModeSelectorDriver driver = go.AddComponent<ModeSelectorDriver>();

        Assert.That(driver.IsWriter, Is.False);
        driver.Tick(0f);
        Assert.That(channel.CurrentMode, Is.EqualTo(NpcMode.Hunt));
    }

    // '-modeSelector llm' on a build where only the seam exists: say so once,
    // keep the channel's mode.
    [Test]
    public void AKindWithNoSelectorInTheBuild_WarnsAndStaysInert()
    {
        var go = new GameObject("UnbuiltKindTest");
        spawned.Add(go);
        ModeChannel channel = go.AddComponent<ModeChannel>();
        ModeSelectorDriver driver = go.AddComponent<ModeSelectorDriver>();
        driver.selectorKind = ModeSelectorKind.Llm;
        ExpectWarning();

        driver.Tick(0f);
        driver.Tick(1f);

        Assert.That(channel.CurrentMode, Is.EqualTo(NpcMode.Hunt));
    }

    [Test]
    public void ResetState_DropsTheInFlightCallAndIssuesAfresh()
    {
        ModeSelectorDriver driver = NewDriver(out ModeChannel channel, out ScriptedSelector selector);

        driver.Tick(0f);
        driver.ResetState();
        Assert.That(selector.Tokens[0].IsCancellationRequested, Is.True);

        driver.Tick(0.5f); // well inside the old period
        Assert.That(selector.Calls, Is.EqualTo(2), "a fresh episode issues immediately");

        // The last episode's answer arriving late must not land in this one.
        selector.Pending[0].SetResult(NpcMode.Patrol);
        driver.Tick(1f);
        Assert.That(channel.CurrentMode, Is.EqualTo(NpcMode.Hunt));
    }
}
