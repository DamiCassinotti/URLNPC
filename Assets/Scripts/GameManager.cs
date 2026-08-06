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

    [Header("Round clock")]
    [Tooltip("Round length in seconds (game time, so it scales with the trainer's time_scale). Timeout ends the round as a DRAW. Set to 0 or negative to disable the clock.")]
    [SerializeField] internal float roundDurationSeconds = 120f;
    [Tooltip("Optional HUD text for the clock. Left empty (the scene is binary serialized), one is created at runtime on the HUD canvas.")]
    [SerializeField] TMP_Text timerText;

    // Public because the future GameStateSnapshot / LLM context needs it.
    public float RemainingRoundTime => roundClock.Remaining;

    readonly RoundClock roundClock = new RoundClock();
    Counter counter;
    bool roundOver;
    string playerTag = "Player";
    string npcTag = "NPC";

    void Start()
    {
        counter = FindAnyObjectByType<Counter>();
        if (finishedRoundCanvas != null) finishedRoundCanvas.enabled = false;
        CreateResetScoreButton();
        ResetRoundClock();
        CreateTimerText();
    }

    void Update()
    {
        if (roundOver || !roundClock.Enabled) return;
        bool expired = roundClock.Tick(Time.deltaTime);
        UpdateTimerText();
        if (expired) ProcessTimeout();
    }

    // Also called by EnemyAgent.OnEpisodeBegin, so training episodes get a full
    // time budget without a scene reload. Idempotent: several agents resetting
    // in the same frame is fine.
    public void ResetRoundClock()
    {
        roundClock.Duration = roundDurationSeconds;
        roundClock.Reset();
    }

    // Public so it can also be wired to an OnClick in the Inspector, if the
    // canvas is ever rebuilt by hand.
    public void ResetScore()
    {
        CounterData.ResetScores();
    }

    // The FPS scene is force-binary serialized, so button wiring can't be
    // authored there. Parented to the end-of-round canvas, which makes it
    // show and hide with that canvas's enabled state.
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

    // Round outcome hook (issue #12): the winner's tag, or DrawResult on a
    // timeout. Static so listeners (TelemetryLogger) survive the scene reload
    // that follows; fired before the training early-returns below, so
    // training rounds are reported too.
    public static event System.Action<string> RoundEnded;

    public const string DrawResult = "Draw";

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
            FinishRound(playerTag + " wins!");
        }
    }

    void ProcessPlayerDeath(string loser)
    {
        if (loser == playerTag)
        {
            FinishRound(npcTag + " wins!");
        }
    }

    // The clock ran out: a draw, no winner forced. Agents take a small negative
    // terminal reward instead, so stalling is discouraged by incentive rather
    // than by handing someone the win.
    void ProcessTimeout()
    {
        if (counter != null) counter.Draw();

        RoundEnded?.Invoke(DrawResult);

        // Training: every agent takes the penalty and resets in place, so the
        // clock is rearmed and the scene keeps running — no reload.
        if (Academy.IsInitialized && Academy.Instance.IsCommunicatorOn)
        {
            foreach (EnemyAgent agent in FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None))
            {
                agent.OnRoundTimeout();
            }
            ResetRoundClock();
            return;
        }

        // Human play: a draw ends the round exactly like a win, and the
        // end-of-round button reloads into a fresh one. No agent reset here —
        // the reload recreates everything, and skipping it keeps the enemy
        // from visibly teleporting behind the draw screen.
        FinishRound("Draw!");
    }

    void FinishRound(string bannerText)
    {
        roundOver = true;
        if (winnerText != null) winnerText.text = bannerText;
        if (playerController != null) playerController.enabled = false;
        if (finishedRoundCanvas != null) finishedRoundCanvas.enabled = true;
        Time.timeScale = 0;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // ------------------------------------------------------------ timer UI

    // Built in code for the same reason as the reset button; hung on the same
    // canvas as the win counter, not the end-of-round one.
    void CreateTimerText()
    {
        if (timerText != null || roundDurationSeconds <= 0f) return;

        Canvas canvas = counter != null ? counter.GetComponentInParent<Canvas>() : null;
        if (canvas == null || canvas == finishedRoundCanvas)
        {
            canvas = null;
            foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (c != finishedRoundCanvas) { canvas = c; break; }
            }
        }
        if (canvas == null) return;

        var obj = new GameObject("RoundTimer", typeof(RectTransform));
        obj.transform.SetParent(canvas.transform, false);
        var rt = obj.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -20f);
        rt.sizeDelta = new Vector2(220f, 50f);

        var label = obj.AddComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 36;
        label.color = Color.white;
        timerText = label;
        UpdateTimerText();
    }

    void UpdateTimerText()
    {
        if (timerText == null) return;
        int t = Mathf.CeilToInt(RemainingRoundTime);
        timerText.text = $"{t / 60}:{t % 60:00}";
    }
}
