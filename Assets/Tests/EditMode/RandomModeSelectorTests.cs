using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;

public class RandomModeSelectorTests
{
    [SetUp]
    public void SetUp()
    {
        RunRng.ResetForNewRun();
    }

    [TearDown]
    public void TearDown()
    {
        RunRng.ResetForNewRun();
    }

    static List<NpcMode> Draw(RandomModeSelector selector, int count)
    {
        var drawn = new List<NpcMode>();
        for (int i = 0; i < count; i++)
        {
            var task = selector.SelectModeAsync(null, CancellationToken.None);
            Assert.That(task.IsCompleted, Is.True, "the baseline answers synchronously");
            drawn.Add(task.Result);
        }
        return drawn;
    }

    [Test]
    public void SameSeed_ReplaysTheSameModeSequence()
    {
        RunRng.EnsureInitialized(42);
        List<NpcMode> first = Draw(new RandomModeSelector(), 30);

        RunRng.ResetForNewRun();
        RunRng.EnsureInitialized(42);
        Assert.That(Draw(new RandomModeSelector(), 30), Is.EqualTo(first));
    }

    [Test]
    public void EveryMode_IsReachable()
    {
        RunRng.EnsureInitialized(42);
        List<NpcMode> drawn = Draw(new RandomModeSelector(), 200);
        foreach (NpcMode mode in NpcModes.All)
        {
            Assert.That(drawn, Does.Contain(mode));
        }
    }

    [Test]
    public void DrawsOnItsOwnStream_SelectorActivityDoesNotShiftTheModeStream()
    {
        RunRng.EnsureInitialized(42);
        var modeBaseline = new int[10];
        for (int i = 0; i < modeBaseline.Length; i++)
        {
            modeBaseline[i] = RunRng.Range(RunRng.Stream.Mode, 0, int.MaxValue);
        }

        RunRng.ResetForNewRun();
        RunRng.EnsureInitialized(42);
        Draw(new RandomModeSelector(), 137);
        for (int i = 0; i < modeBaseline.Length; i++)
        {
            Assert.That(RunRng.Range(RunRng.Stream.Mode, 0, int.MaxValue),
                Is.EqualTo(modeBaseline[i]));
        }
    }

    [Test]
    public void AnInjectedDraw_PicksByIndexInDeclarationOrder()
    {
        for (int index = 0; index < NpcModes.All.Length; index++)
        {
            int wanted = index;
            var selector = new RandomModeSelector(count => wanted);
            Assert.That(selector.SelectModeAsync(null, CancellationToken.None).Result,
                Is.EqualTo(NpcModes.All[index]));
        }
    }
}
