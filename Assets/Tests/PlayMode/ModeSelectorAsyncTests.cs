using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// The acceptance rules of issue #124 over real frames: a selector that sleeps
// never stalls the game and its answer still lands, one that throws leaves the
// mode where it was, and EnemyBehavior.Awake wires the driver onto every AI
// body with the snapshot builder feeding it. The loop's rules tick by tick are
// ModeSelectorDriverTests (EditMode).
public class ModeSelectorAsyncTests : PlayModeTestBase
{
    class SleepingSelector : IModeSelector
    {
        public NpcMode Answer = NpcMode.Retreat;
        public int SleepMilliseconds = 200;

        public Task<NpcMode> SelectModeAsync(GameStateSnapshot snapshot, CancellationToken cancellation)
        {
            int sleep = SleepMilliseconds;
            NpcMode answer = Answer;
            // Off the main thread, as the IModeSelector contract requires of a
            // selector that does slow work.
            return Task.Run(() => { Thread.Sleep(sleep); return answer; });
        }
    }

    class ThrowingSelector : IModeSelector
    {
        public Task<NpcMode> SelectModeAsync(GameStateSnapshot snapshot, CancellationToken cancellation)
        {
            return Task.FromException<NpcMode>(new System.InvalidOperationException("llm down"));
        }
    }

    class RecordingSelector : IModeSelector
    {
        public int Calls;
        public GameStateSnapshot LastSnapshot;

        public Task<NpcMode> SelectModeAsync(GameStateSnapshot snapshot, CancellationToken cancellation)
        {
            Calls++;
            LastSnapshot = snapshot;
            // Never answers; teardown's destroy cancels it.
            return new TaskCompletionSource<NpcMode>().Task;
        }
    }

    ModeSelectorDriver CreateDriverBody(out ModeChannel channel)
    {
        GameObject go = Track(new GameObject("SelectorBody"));
        channel = go.AddComponent<ModeChannel>();
        return go.AddComponent<ModeSelectorDriver>();
    }

    [UnityTest]
    public IEnumerator ASleepingSelector_NeverBlocksAFrame_AndItsAnswerStillLands()
    {
        ModeSelectorDriver driver = CreateDriverBody(out ModeChannel channel);
        driver.Selector = new SleepingSelector { Answer = NpcMode.Retreat };

        int framesWhilePending = 0;
        float deadline = Time.realtimeSinceStartup + 5f;
        while (channel.CurrentMode != NpcMode.Retreat)
        {
            Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "the answer never landed");
            framesWhilePending++;
            yield return null;
        }

        Assert.That(framesWhilePending, Is.GreaterThan(1),
            "a 200 ms selector must span many frames — a single frame means something blocked on it");
    }

    [UnityTest]
    public IEnumerator AThrowingSelector_LeavesTheModeWhereItWas()
    {
        ModeSelectorDriver driver = CreateDriverBody(out ModeChannel channel);
        driver.Selector = new ThrowingSelector();
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ModeSelector"));

        float until = Time.time + 0.3f; // one decision at most: the period is 5 s
        while (Time.time < until) yield return null;

        Assert.That(channel.CurrentMode, Is.EqualTo(NpcMode.Hunt));
    }

    [UnityTest]
    public IEnumerator EnemyBehaviorAwake_AutoAddsTheDriver_AndTheSnapshotReachesTheSelector()
    {
        // Same fixture shape as GameStateSnapshotBuilderTests: no NavMesh, so
        // the behavior stays disabled and only Awake's wiring runs.
        GameObject go = Track(new GameObject("TestEnemy"));
        go.SetActive(false);
        go.AddComponent<UnityEngine.AI.NavMeshAgent>().enabled = false;
        go.AddComponent<Health>();
        EnemyBehavior behavior = go.AddComponent<EnemyBehavior>();
        behavior.enabled = false;
        go.SetActive(true);

        ModeSelectorDriver driver = go.GetComponent<ModeSelectorDriver>();
        Assert.That(driver, Is.Not.Null, "EnemyBehavior.Awake must auto-add the selector driver");

        var selector = new RecordingSelector();
        driver.Selector = selector;
        yield return new WaitForFixedUpdate();
        yield return null;

        Assert.That(selector.Calls, Is.EqualTo(1), "the first decision issues on the first tick");
        Assert.That(selector.LastSnapshot, Is.Not.Null,
            "the driver must hand the selector the body's own snapshot");
        Assert.That(selector.LastSnapshot.targetDistance, Is.EqualTo(DistanceBucket.None),
            "nothing was ever seen in this fixture");
    }
}
