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

    // The threshold can also be crossed by the timeout inside the issue path —
    // a primary that simply never answers. The decision that crosses it must
    // already be the fallback's, not one more call to the stuck primary.
    [Test]
    public void AThresholdCrossingTimeout_HandsTheSameDecisionToTheFallback()
    {
        ModeSelectorDriver driver = NewDriver(out ModeChannel channel, out ScriptedSelector selector);
        driver.failuresBeforeFallback = 2;
        var fallback = new ScriptedSelector();
        driver.Fallback = fallback;
        ExpectWarning(); // timeout 1
        ExpectWarning(); // timeout 2
        ExpectWarning(); // the takeover notice

        driver.Tick(0f);  // call 1 to the primary, never answered
        driver.Tick(5f);  // timeout 1, call 2 to the primary
        driver.Tick(10f); // timeout 2 crosses the threshold

        Assert.That(selector.Calls, Is.EqualTo(2), "the stuck primary must not be asked a third time");
        Assert.That(fallback.Calls, Is.EqualTo(1));

        fallback.Pending[0].SetResult(NpcMode.Retreat);
        driver.Tick(11f);
        Assert.That(channel.CurrentMode, Is.EqualTo(NpcMode.Retreat));
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

    // The kinds construct their baselines now (#125): a driver left on a kind
    // with no code-assigned selector builds and runs it.
    [Test]
    public void AKindWithABaseline_BuildsAndRunsIt()
    {
        var go = new GameObject("BuiltKindTest");
        spawned.Add(go);
        ModeChannel channel = go.AddComponent<ModeChannel>();
        ModeSelectorDriver driver = go.AddComponent<ModeSelectorDriver>();
        driver.selectorKind = ModeSelectorKind.Fixed;
        driver.fixedSelectorMode = NpcMode.Patrol;

        driver.Tick(0f);
        driver.Tick(1f); // the answer lands on the tick after it completes

        Assert.That(channel.CurrentMode, Is.EqualTo(NpcMode.Patrol));
    }

    [Test]
    public void ACodeAssignedSelector_OutranksTheBuiltKind()
    {
        var go = new GameObject("AssignedOverBuiltTest");
        spawned.Add(go);
        ModeChannel channel = go.AddComponent<ModeChannel>();
        ModeSelectorDriver driver = go.AddComponent<ModeSelectorDriver>();
        driver.selectorKind = ModeSelectorKind.Fixed;
        driver.fixedSelectorMode = NpcMode.Patrol;
        driver.Selector = new FixedModeSelector(NpcMode.HoldCover);

        driver.Tick(0f);
        driver.Tick(1f);

        Assert.That(channel.CurrentMode, Is.EqualTo(NpcMode.HoldCover));
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

    // ---------------------------------------------- mode_decision records (#126)

    List<ModeDecisionRecord> Recorded(ModeSelectorDriver driver)
    {
        var records = new List<ModeDecisionRecord>();
        driver.onDecision = records.Add;
        return records;
    }

    [Test]
    public void AnAppliedAnswer_EmitsAnAppliedRecordBeforeTheModeChanges()
    {
        ModeSelectorDriver driver = NewDriver(out ModeChannel channel, out ScriptedSelector selector);
        List<ModeDecisionRecord> records = Recorded(driver);

        driver.Tick(0f);
        selector.Pending[0].SetResult(NpcMode.Retreat);
        driver.Tick(1f);

        Assert.That(records, Has.Count.EqualTo(1));
        Assert.That(records[0].outcome, Is.EqualTo(ModeDecisionOutcome.Applied));
        Assert.That(records[0].chosenMode, Is.EqualTo(NpcMode.Retreat));
        Assert.That(records[0].fromMode, Is.EqualTo(NpcMode.Hunt), "from is the mode before the write");
        Assert.That(records[0].parsed, Is.True);
        Assert.That(records[0].fallback, Is.False);
        Assert.That(records[0].selectorKind, Is.EqualTo("code"), "an assigned instance reports as code");
    }

    [Test]
    public void ATimedOutCall_EmitsATimeoutRecordWithNoChosenMode()
    {
        ModeSelectorDriver driver = NewDriver(out ModeChannel channel, out ScriptedSelector selector);
        List<ModeDecisionRecord> records = Recorded(driver);
        ExpectWarning();

        driver.Tick(0f);
        driver.Tick(5f); // next decision due: the stale call is the timeout

        Assert.That(records, Has.Count.EqualTo(1));
        Assert.That(records[0].outcome, Is.EqualTo(ModeDecisionOutcome.Timeout));
        Assert.That(records[0].chosenMode, Is.Null);
        Assert.That(records[0].parsed, Is.False);
    }

    [Test]
    public void AnAnswerNamingNoMode_EmitsAnInvalidUnparsedRecord()
    {
        ModeSelectorDriver driver = NewDriver(out ModeChannel channel, out ScriptedSelector selector);
        List<ModeDecisionRecord> records = Recorded(driver);
        ExpectWarning();

        driver.Tick(0f);
        selector.Pending[0].SetResult((NpcMode)99);
        driver.Tick(1f);

        Assert.That(records, Has.Count.EqualTo(1));
        Assert.That(records[0].outcome, Is.EqualTo(ModeDecisionOutcome.Invalid));
        Assert.That(records[0].parsed, Is.False);
        Assert.That(records[0].chosenMode, Is.Null);
    }

    [Test]
    public void AFaultedAnswer_EmitsAnErrorRecord()
    {
        ModeSelectorDriver driver = NewDriver(out ModeChannel channel, out ScriptedSelector selector);
        List<ModeDecisionRecord> records = Recorded(driver);
        ExpectWarning();

        driver.Tick(0f);
        selector.Pending[0].SetException(new System.Exception("down"));
        driver.Tick(1f);

        Assert.That(records, Has.Count.EqualTo(1));
        Assert.That(records[0].outcome, Is.EqualTo(ModeDecisionOutcome.Error));
    }

    [Test]
    public void AFallbackAnswer_IsFlaggedAsTheFallbacks()
    {
        ModeSelectorDriver driver = NewDriver(out ModeChannel channel, out ScriptedSelector selector);
        driver.failuresBeforeFallback = 1;
        var fallback = new ScriptedSelector();
        driver.Fallback = fallback;
        List<ModeDecisionRecord> records = Recorded(driver);
        ExpectWarning(); // the failure
        ExpectWarning(); // the takeover notice

        driver.Tick(0f);
        selector.Pending[0].SetException(new System.Exception("down"));
        driver.Tick(5f); // failure counted, the fallback takes this decision
        fallback.Pending[0].SetResult(NpcMode.HoldCover);
        driver.Tick(6f);

        ModeDecisionRecord applied = records.Find(r => r.outcome == ModeDecisionOutcome.Applied);
        Assert.That(applied, Is.Not.Null);
        Assert.That(applied.fallback, Is.True);
        Assert.That(applied.selectorKind, Is.EqualTo("fallback"), "named for who actually answered");
        ModeDecisionRecord failed = records.Find(r => r.outcome == ModeDecisionOutcome.Error);
        Assert.That(failed.fallback, Is.False);
        Assert.That(failed.selectorKind, Is.EqualTo("code"));
    }

    // Fills the report the way the LLM selector will (#130).
    class ReportingSelector : IReportingModeSelector
    {
        public Task<NpcMode> SelectModeAsync(GameStateSnapshot snapshot, CancellationToken cancellation)
        {
            return Task.FromResult(NpcMode.Hunt);
        }

        public Task<NpcMode> SelectModeAsync(GameStateSnapshot snapshot, ModeDecisionReport report, CancellationToken cancellation)
        {
            report.ModelName = "test-model";
            report.Reason = "because";
            report.RetryUsed = true;
            return Task.FromResult(NpcMode.Retreat);
        }
    }

    [Test]
    public void AReportingSelector_GetsTheReportOverloadAndItsFieldsLandOnTheRecord()
    {
        var go = new GameObject("ReportingSelectorTest");
        spawned.Add(go);
        ModeChannel channel = go.AddComponent<ModeChannel>();
        ModeSelectorDriver driver = go.AddComponent<ModeSelectorDriver>();
        driver.Selector = new ReportingSelector();
        List<ModeDecisionRecord> records = Recorded(driver);

        driver.Tick(0f);
        driver.Tick(1f);

        Assert.That(channel.CurrentMode, Is.EqualTo(NpcMode.Retreat), "the report overload answered");
        Assert.That(records, Has.Count.EqualTo(1));
        Assert.That(records[0].modelName, Is.EqualTo("test-model"));
        Assert.That(records[0].reason, Is.EqualTo("because"));
        Assert.That(records[0].retryUsed, Is.True);
    }

    [Test]
    public void AnAbandonedCall_EmitsNoRecord()
    {
        ModeSelectorDriver driver = NewDriver(out ModeChannel channel, out ScriptedSelector selector);
        List<ModeDecisionRecord> records = Recorded(driver);

        driver.Tick(0f);
        driver.ResetState(); // the answer stopped mattering — no verdict, no line

        Assert.That(records, Is.Empty);
    }

    [Test]
    public void DecisionIds_AreUniqueAcrossDrivers()
    {
        ModeSelectorDriver first = NewDriver(out _, out ScriptedSelector firstSelector);
        ModeSelectorDriver second = NewDriver(out _, out ScriptedSelector secondSelector);
        List<ModeDecisionRecord> firstRecords = Recorded(first);
        List<ModeDecisionRecord> secondRecords = Recorded(second);

        first.Tick(0f);
        second.Tick(0f);
        firstSelector.Pending[0].SetResult(NpcMode.Hunt);
        secondSelector.Pending[0].SetResult(NpcMode.Hunt);
        first.Tick(1f);
        second.Tick(1f);

        Assert.That(firstRecords[0].decisionId, Is.Not.EqualTo(secondRecords[0].decisionId));
    }
}
