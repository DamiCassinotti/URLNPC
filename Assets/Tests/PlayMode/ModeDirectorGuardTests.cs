using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

// Writer ownership: the scripted director must leave the channel alone outside
// training, or it would overwrite the LLM selector's mode every dwell. PlayMode
// because the guard reads the Academy and the tick is a real FixedUpdate; the
// test runner has no communicator, which is exactly the inference case.
public class ModeDirectorGuardTests : PlayModeTestBase
{
    // A forced mode the channel does not already hold, so any write shows.
    ModeDirector CreateDirector(bool trainingOnly)
    {
        GameObject go = Track(new GameObject("ModeDirectorGuardTest"));
        go.SetActive(false);
        go.AddComponent<ModeChannel>();
        ModeDirector director = go.AddComponent<ModeDirector>();
        director.trainingOnly = trainingOnly;
        director.useForcedMode = true;
        director.forcedMode = NpcMode.Patrol;
        go.SetActive(true);
        return director;
    }

    [UnityTest]
    public IEnumerator TrainingOnlyDirector_DoesNotWriteWithoutACommunicator()
    {
        ModeDirector director = CreateDirector(trainingOnly: true);

        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        Assert.That(director.IsWriter, Is.False);
        Assert.That(director.GetComponent<ModeChannel>().CurrentMode, Is.EqualTo(NpcMode.Hunt));
    }

    [UnityTest]
    public IEnumerator DirectorWritesWhenTrainingOnlyIsOff()
    {
        ModeDirector director = CreateDirector(trainingOnly: false);

        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        Assert.That(director.IsWriter, Is.True);
        Assert.That(director.GetComponent<ModeChannel>().CurrentMode, Is.EqualTo(NpcMode.Patrol));
    }

    [UnityTest]
    public IEnumerator ResetState_AlsoStandsDownOutsideTraining()
    {
        ModeDirector director = CreateDirector(trainingOnly: true);
        yield return null;

        director.ResetState();

        Assert.That(director.GetComponent<ModeChannel>().CurrentMode, Is.EqualTo(NpcMode.Hunt));
    }
}
