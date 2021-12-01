using System.Collections;
using System.Collections.Generic;
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
