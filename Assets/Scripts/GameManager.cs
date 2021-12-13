using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

public class GameManager : MonoBehaviour
{
    [SerializeField] TMP_Text winnerText;
    [SerializeField] Canvas finishedRoundCanvas;
    Counter counter;
    string playerTag = "Player";
    string npcTag = "NPC";

    void Start()
    {
        counter = FindObjectOfType<Counter>();
        finishedRoundCanvas.enabled = false;
    }

    public void LoadNewLevel()
    {
        Debug.Log("Reloading scene");
        SceneManager.LoadScene(0);
        Time.timeScale = 1;
    }

    public void ProcessDeath(string loser)
    {
        ProcessNpcDeath(loser);
        ProcessPlayerDeath(loser);
    }

    void ProcessNpcDeath(string loser)
    {
        if (loser == npcTag)
        {
            counter.UserWins();
            Destroy(GameObject.FindWithTag(npcTag));
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
        finishedRoundCanvas.enabled = true;
        Time.timeScale = 0;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
