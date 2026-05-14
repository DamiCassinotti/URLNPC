using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using Unity.MLAgents;

public class GameManager : MonoBehaviour
{
    [SerializeField] TMP_Text winnerText;
    [SerializeField] Canvas finishedRoundCanvas;
    [SerializeField] Behaviour playerController;

    Counter counter;
    string playerTag = "Player";
    string npcTag = "NPC";

    void Start()
    {
        counter = FindAnyObjectByType<Counter>();
        if (finishedRoundCanvas != null) finishedRoundCanvas.enabled = false;
    }

    public void LoadNewLevel()
    {
        SceneManager.LoadScene(0);
        Time.timeScale = 1;
    }

    public void ProcessDeath(string loser)
    {
        // While training (or running inference against a connected trainer),
        // the EnemyAgent handles episode resets itself — don't freeze the
        // scene or destroy the NPC, that would break training.
        if (Academy.IsInitialized && Academy.Instance.IsCommunicatorOn) return;

        ProcessNpcDeath(loser);
        ProcessPlayerDeath(loser);
    }

    void ProcessNpcDeath(string loser)
    {
        if (loser == npcTag)
        {
            if (counter != null) counter.UserWins();
            GameObject npc = GameObject.FindWithTag(npcTag);
            if (npc != null) Destroy(npc);
            FinishRound(playerTag);
        }
    }

    void ProcessPlayerDeath(string loser)
    {
        if (loser == playerTag)
        {
            if (counter != null) counter.NpcWins();
            FinishRound(npcTag);
        }
    }

    void FinishRound(string winner)
    {
        if (winnerText != null) winnerText.text = winner + " wins!";
        if (playerController != null) playerController.enabled = false;
        if (finishedRoundCanvas != null) finishedRoundCanvas.enabled = true;
        Time.timeScale = 0;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
