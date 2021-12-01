using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class Counter : MonoBehaviour
{
    [SerializeField] TMP_Text counter;

    void Update()
    {
        counter.text = "User: " + CounterData.readUserPoints() + "\nNPC: " + CounterData.readNpcPoints();
    }

    public void UserWins()
    {
        CounterData.UserWins();
    }

    public void NpcWins()
    {
        CounterData.NpcWins();
    }

}
