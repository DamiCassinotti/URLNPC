using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class GameManager : MonoBehaviour
{
    [SerializeField] TMP_Text winnerText;
    Counter counter;
    string playerTag = "Player";
    string npcTag = "NPC";

    void Start()
    {
        counter = FindObjectOfType<Counter>();
    }

    public void ProcessDeath(string loser)
    {
        ProcessNpcDeath(loser);
        ProcessPlayerDeath(loser);
        ChoseNewLevel();
    }

    void ProcessNpcDeath(string loser)
    {
        if (loser == npcTag)
        {
            counter.UserWins();
            Destroy(gameObject);
            FinishRound(playerTag);
        }
    }

    void ProcessPlayerDeath(string loser)
    {
        if (loser == playerTag)
        {
            counter.NpcWins();
            FinishRound(npcTag);
        }
    }

    void FinishRound(string winner)
    {
        winnerText.text = winner + " wins!";
    }

    void ChoseNewLevel()
    {
        int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
        SceneManager.LoadScene(currentSceneIndex);
    }
}
