using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class Counter : MonoBehaviour
{
    [SerializeField] TMP_Text counter;
    int userPoints = 0;
    int npcPoints = 0;

    void Start()
    {
        counter = GetComponent<TMP_Text>();
    }

    void Update()
    {
        counter.text = "User: " + userPoints + "\nNPC: " + npcPoints;
    }

    public void UserWins()
    {
        userPoints += 1;
    }

    public void NpcWins()
    {
        npcPoints += 1;
    }

}
