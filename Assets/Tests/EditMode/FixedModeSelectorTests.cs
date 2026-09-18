using System.Threading;
using NUnit.Framework;

public class FixedModeSelectorTests
{
    [Test]
    public void AnswersThePinnedMode_ForEveryMode()
    {
        foreach (NpcMode mode in NpcModes.All)
        {
            var selector = new FixedModeSelector(mode);
            var task = selector.SelectModeAsync(null, CancellationToken.None);

            // Synchronous under the async signature: the answer is already
            // there on the tick that asked.
            Assert.That(task.IsCompleted, Is.True);
            Assert.That(task.Result, Is.EqualTo(mode));
        }
    }

    [Test]
    public void TheAnswerDoesNotDrift_AcrossCalls()
    {
        var selector = new FixedModeSelector(NpcMode.Retreat);
        for (int i = 0; i < 5; i++)
        {
            Assert.That(selector.SelectModeAsync(null, CancellationToken.None).Result,
                Is.EqualTo(NpcMode.Retreat));
        }
    }
}
