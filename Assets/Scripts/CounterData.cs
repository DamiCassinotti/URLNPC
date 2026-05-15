using UnityEngine;

public static class CounterData
{
    static int userPoints { get; set; }
    static int npcPoints { get; set; }

    static CounterData()
    {
        userPoints = 0;
        npcPoints = 0;
    }

    // Reset the counter at the start of every Play session in the Editor.
    // RuntimeInitializeLoadType.BeforeSceneLoad fires once per Play entry,
    // before the first scene loads — so the counter resets between training
    // runs (or between Editor Play sessions in general), but in-game scene
    // reloads (e.g. "Play Again" after a match) don't zero it.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ResetOnPlay()
    {
        userPoints = 0;
        npcPoints = 0;
    }

    public static int readUserPoints()
    {
        return userPoints;
    }

    public static int readNpcPoints()
    {
        return npcPoints;
    }

    public static void UserWins()
    {
        userPoints += 1;
    }

    public static void NpcWins()
    {
        npcPoints += 1;
    }
}
