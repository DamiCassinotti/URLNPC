using NUnit.Framework;
using UnityEngine;

// The tally lives in PlayerPrefs, so the fixture snapshots the real on-disk
// values and restores them — running the suite must not clobber a real score.
public class CounterDataTests
{
    // Must match the private keys in CounterData.cs.
    const string UserKey = "URLNPC.userPoints";
    const string NpcKey = "URLNPC.npcPoints";
    const string DrawsKey = "URLNPC.draws";
    static readonly string[] Keys = { UserKey, NpcKey, DrawsKey };

    readonly bool[] hadKey = new bool[Keys.Length];
    readonly int[] savedValue = new int[Keys.Length];

    [SetUp]
    public void SnapshotRealScores()
    {
        for (int i = 0; i < Keys.Length; i++)
        {
            hadKey[i] = PlayerPrefs.HasKey(Keys[i]);
            savedValue[i] = PlayerPrefs.GetInt(Keys[i], 0);
        }
        CounterData.ResetScores();
    }

    [TearDown]
    public void RestoreRealScores()
    {
        for (int i = 0; i < Keys.Length; i++)
        {
            if (hadKey[i]) PlayerPrefs.SetInt(Keys[i], savedValue[i]);
            else PlayerPrefs.DeleteKey(Keys[i]);
        }
        PlayerPrefs.Save();
    }

    [Test]
    public void FreshTally_ReadsAllZero()
    {
        Assert.That(CounterData.readUserPoints(), Is.Zero);
        Assert.That(CounterData.readNpcPoints(), Is.Zero);
        Assert.That(CounterData.readDraws(), Is.Zero);
    }

    [Test]
    public void UserWins_IncrementsOnlyUserPoints()
    {
        CounterData.UserWins();
        CounterData.UserWins();
        Assert.That(CounterData.readUserPoints(), Is.EqualTo(2));
        Assert.That(CounterData.readNpcPoints(), Is.Zero);
        Assert.That(CounterData.readDraws(), Is.Zero);
    }

    [Test]
    public void NpcWins_IncrementsOnlyNpcPoints()
    {
        CounterData.NpcWins();
        Assert.That(CounterData.readNpcPoints(), Is.EqualTo(1));
        Assert.That(CounterData.readUserPoints(), Is.Zero);
        Assert.That(CounterData.readDraws(), Is.Zero);
    }

    [Test]
    public void Draw_IncrementsOnlyDraws()
    {
        CounterData.Draw();
        Assert.That(CounterData.readDraws(), Is.EqualTo(1));
        Assert.That(CounterData.readUserPoints(), Is.Zero);
        Assert.That(CounterData.readNpcPoints(), Is.Zero);
    }

    [Test]
    public void ResetScores_ZeroesEverything()
    {
        CounterData.UserWins();
        CounterData.NpcWins();
        CounterData.Draw();
        CounterData.ResetScores();
        Assert.That(CounterData.readUserPoints(), Is.Zero);
        Assert.That(CounterData.readNpcPoints(), Is.Zero);
        Assert.That(CounterData.readDraws(), Is.Zero);
    }
}
