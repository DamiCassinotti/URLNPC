using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
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
        CreateResetScoreButton();
    }

    // Clears the persisted win/loss tally. Public so it can also be wired to
    // an OnClick in the Inspector if you ever rebuild the canvas by hand.
    public void ResetScore()
    {
        CounterData.ResetScores();
    }

    // The FPS scene is force-binary serialized, so we can't author button
    // wiring in the scene file — build the "Reset Score" button in code and
    // parent it to the end-of-round canvas. As a child of that canvas it
    // shows/hides automatically with the canvas's enabled state.
    void CreateResetScoreButton()
    {
        if (finishedRoundCanvas == null) return;

        var btnObj = new GameObject("ResetScoreButton", typeof(RectTransform), typeof(Image), typeof(Button));
        btnObj.transform.SetParent(finishedRoundCanvas.transform, false);

        var rt = btnObj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f);
        rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 40f);
        rt.sizeDelta = new Vector2(240f, 60f);

        btnObj.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 0.9f);
        btnObj.GetComponent<Button>().onClick.AddListener(ResetScore);

        var labelObj = new GameObject("Label", typeof(RectTransform));
        labelObj.transform.SetParent(btnObj.transform, false);
        var lrt = labelObj.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;

        var label = labelObj.AddComponent<TextMeshProUGUI>();
        label.text = "Reset Score";
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 28;
        label.color = Color.white;
    }

    public void LoadNewLevel()
    {
        SceneManager.LoadScene(0);
        Time.timeScale = 1;
    }

    /// <summary>
    /// Round outcome hook (issue #12): fired with the winner's tag every
    /// time a round is decided. Static so listeners (TelemetryLogger)
    /// survive the scene reload that follows. Fired BEFORE the training
    /// early-return below, so training rounds are reported too.
    /// </summary>
    public static event System.Action<string> RoundEnded;

    public void ProcessDeath(string loser)
    {
        // Counter increments either way — useful as a visual readout during
        // training so you can see who's winning rounds without alt-tabbing
        // to the trainer terminal.
        if (counter != null)
        {
            if (loser == npcTag) counter.UserWins();
            else if (loser == playerTag) counter.NpcWins();
        }

        RoundEnded?.Invoke(loser == npcTag ? playerTag : npcTag);

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
            GameObject npc = GameObject.FindWithTag(npcTag);
            if (npc != null) Destroy(npc);
            FinishRound(playerTag);
        }
    }

    void ProcessPlayerDeath(string loser)
    {
        if (loser == playerTag)
        {
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
