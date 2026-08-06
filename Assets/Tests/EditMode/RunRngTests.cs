using NUnit.Framework;

// The reproducibility contract (CLAUDE.md "Reproducibility"): a run seed fully
// determines every random sequence that matters, and each domain draws from its
// own sub-stream so activity in one can never shift the others.
public class RunRngTests
{
    [SetUp]
    public void SetUp()
    {
        RunRng.ResetForNewRun();
    }

    [TearDown]
    public void TearDown()
    {
        // Leave the statics as a fresh play session would find them.
        RunRng.ResetForNewRun();
    }

    // The editor process itself can carry -runSeed, which outranks the
    // inspector seed and would invalidate the tests that rely on two
    // different effective seeds. The determinism tests are unaffected.
    static void RequireNoCommandLineSeed()
    {
        RunRng.EnsureInitialized(1);
        bool overridden = RunRng.SeedSource == "command line";
        RunRng.ResetForNewRun();
        if (overridden) Assert.Ignore("Editor was launched with -runSeed; inspector-seed tests do not apply.");
    }

    static int[] DrawInts(RunRng.Stream stream, int count)
    {
        var values = new int[count];
        for (int i = 0; i < count; i++) values[i] = RunRng.Range(stream, 0, int.MaxValue);
        return values;
    }

    static float[] DrawFloats(RunRng.Stream stream, int count)
    {
        var values = new float[count];
        for (int i = 0; i < count; i++) values[i] = RunRng.Range(stream, -100f, 100f);
        return values;
    }

    [Test]
    public void SameSeed_ProducesIdenticalSequences_PerStream()
    {
        RunRng.EnsureInitialized(42);
        int[] arena1 = DrawInts(RunRng.Stream.Arena, 20);
        int[] spawn1 = DrawInts(RunRng.Stream.Spawn, 20);
        float[] wander1 = DrawFloats(RunRng.Stream.Wander, 20);

        RunRng.ResetForNewRun();
        RunRng.EnsureInitialized(42);
        Assert.That(DrawInts(RunRng.Stream.Arena, 20), Is.EqualTo(arena1));
        Assert.That(DrawInts(RunRng.Stream.Spawn, 20), Is.EqualTo(spawn1));
        Assert.That(DrawFloats(RunRng.Stream.Wander, 20), Is.EqualTo(wander1));
    }

    [Test]
    public void DifferentSeeds_ProduceDifferentSequences()
    {
        RequireNoCommandLineSeed();

        RunRng.EnsureInitialized(42);
        int[] first = DrawInts(RunRng.Stream.Arena, 20);

        RunRng.ResetForNewRun();
        RunRng.EnsureInitialized(43);
        Assert.That(DrawInts(RunRng.Stream.Arena, 20), Is.Not.EqualTo(first));
    }

    [Test]
    public void Streams_AreIndependent_DrawsInOneDoNotShiftAnother()
    {
        RunRng.EnsureInitialized(42);
        int[] arenaBaseline = DrawInts(RunRng.Stream.Arena, 10);
        int[] spawnBaseline = DrawInts(RunRng.Stream.Spawn, 10);

        RunRng.ResetForNewRun();
        RunRng.EnsureInitialized(42);
        // A policy-dependent number of wander draws happens between the
        // arena/spawn draws in a real run — it must not affect them.
        DrawFloats(RunRng.Stream.Wander, 137);
        Assert.That(DrawInts(RunRng.Stream.Arena, 10), Is.EqualTo(arenaBaseline));
        DrawFloats(RunRng.Stream.Wander, 61);
        Assert.That(DrawInts(RunRng.Stream.Spawn, 10), Is.EqualTo(spawnBaseline));
    }

    [Test]
    public void EnsureInitialized_IsIdempotent_LaterSeedsAreIgnored()
    {
        RunRng.EnsureInitialized(42);
        int seed = RunRng.Seed;
        int[] partial = DrawInts(RunRng.Stream.Arena, 5);

        // A scene reload calls EnsureInitialized again (ArenaManager.Awake) —
        // the streams must keep advancing, not restart.
        RunRng.EnsureInitialized(99);
        Assert.That(RunRng.Seed, Is.EqualTo(seed));

        RunRng.ResetForNewRun();
        RunRng.EnsureInitialized(42);
        int[] full = DrawInts(RunRng.Stream.Arena, 10);
        Assert.That(partial, Is.EqualTo(System.Linq.Enumerable.Take(full, 5)));
    }

    [Test]
    public void IntRange_RespectsBounds_MaxExclusive()
    {
        RunRng.EnsureInitialized(42);
        for (int i = 0; i < 1000; i++)
        {
            int v = RunRng.Range(RunRng.Stream.Spawn, -3, 5);
            Assert.That(v, Is.InRange(-3, 4));
        }
        Assert.That(RunRng.Range(RunRng.Stream.Spawn, 2, 3), Is.EqualTo(2));
    }

    [Test]
    public void FloatRange_RespectsBounds()
    {
        RunRng.EnsureInitialized(42);
        for (int i = 0; i < 1000; i++)
        {
            float v = RunRng.Range(RunRng.Stream.Wander, -2.5f, 7.25f);
            Assert.That(v, Is.InRange(-2.5f, 7.25f));
        }
    }

    [Test]
    public void SeedSource_ReportsInspectorForNonZero_RandomForZero()
    {
        RequireNoCommandLineSeed();

        RunRng.EnsureInitialized(7);
        Assert.That(RunRng.SeedSource, Is.EqualTo("inspector"));
        Assert.That(RunRng.Seed, Is.EqualTo(7));

        RunRng.ResetForNewRun();
        RunRng.EnsureInitialized(0);
        Assert.That(RunRng.SeedSource, Is.EqualTo("random"));
        Assert.That(RunRng.Initialized, Is.True);
    }
}
