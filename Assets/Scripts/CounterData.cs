using UnityEngine;

public static class CounterData
{
    // PlayerPrefs keys — backs the score with Unity's on-disk store so the
    // tally survives quitting the game (Editor Play sessions and standalone
    // builds alike). This lets you stop and resume a training run later
    // without losing the win/loss count.
    const string UserPointsKey = "URLNPC.userPoints";
    const string NpcPointsKey = "URLNPC.npcPoints";
    const string DrawsKey = "URLNPC.draws";

    public static int readUserPoints()
    {
        return PlayerPrefs.GetInt(UserPointsKey, 0);
    }

    public static int readNpcPoints()
    {
        return PlayerPrefs.GetInt(NpcPointsKey, 0);
    }

    public static void UserWins()
    {
        PlayerPrefs.SetInt(UserPointsKey, readUserPoints() + 1);
        PlayerPrefs.Save();
    }

    public static void NpcWins()
    {
        PlayerPrefs.SetInt(NpcPointsKey, readNpcPoints() + 1);
        PlayerPrefs.Save();
    }

    public static int readDraws()
    {
        return PlayerPrefs.GetInt(DrawsKey, 0);
    }

    // Round clock ran out with both combatants alive — nobody scores.
    public static void Draw()
    {
        PlayerPrefs.SetInt(DrawsKey, readDraws() + 1);
        PlayerPrefs.Save();
    }

    // Manual reset — wire this to a UI button or call it from a menu when you
    // want to start a fresh tally. Nothing zeroes the score automatically
    // anymore, so the count persists until you clear it here.
    public static void ResetScores()
    {
        PlayerPrefs.DeleteKey(UserPointsKey);
        PlayerPrefs.DeleteKey(NpcPointsKey);
        PlayerPrefs.DeleteKey(DrawsKey);
        PlayerPrefs.Save();
    }
}
