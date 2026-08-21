using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.InferenceEngine;
using Unity.MLAgents.Policies;

// Drives a headless evaluation run (issue #52): scores a trained policy over a
// fixed number of rounds with no trainer attached, then quits so scripts/eval.sh
// can summarize the telemetry. Self-bootstrapping like TelemetryLogger — the
// scene is binary serialized — and inert unless -evalEpisodes is on the command
// line, so ordinary play and training sessions never see it.
//
// The engine adapter half; the settings live in EvalSettings.
public class EvalSession : MonoBehaviour
{
    // GameManager asks: on a timeout the agents have to flush their per-episode
    // stats here too, the way they do during training.
    public static bool IsActive { get; private set; }

    static EvalSettings pending;
    static EvalSession instance;

    EvalSettings settings;
    ModelAsset model;
    int completed;
    bool applyPending;
    bool reloadPending;
    bool finished;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoBootstrap()
    {
        EvalSettings parsed = EvalSettings.Parse(System.Environment.GetCommandLineArgs());
        if (!parsed.Enabled) return;
        if (parsed.Error != null)
        {
            Debug.LogError($"[Eval] {parsed.Error}");
            Application.Quit(2);
            return;
        }
        pending = parsed;
        new GameObject("EvalSession").AddComponent<EvalSession>();
    }

    void Awake()
    {
        settings = pending;
        if (settings == null || instance != null) { Destroy(gameObject); return; }

        model = Resources.Load<ModelAsset>(settings.ModelResource);
        if (model == null)
        {
            // Nothing is marked active on this path: Quit only takes effect at
            // the end of the frame, and a half-configured session must not
            // change how the round that is still running behaves.
            Debug.LogError($"[Eval] No model at Resources/{settings.ModelResource} — rebuild the player with the model to evaluate (scripts/eval.sh does this).");
            Application.Quit(2);
            return;
        }

        instance = this;
        IsActive = true;
        DontDestroyOnLoad(gameObject);
        ApplyClock();
        GameManager.RoundEnded += HandleRoundEnded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        applyPending = true;
        Debug.Log($"[Eval] {settings.Episodes} episodes, model {settings.ModelResource}, subject {settings.Subject}, opponent {settings.Opponent}, modes {settings.ModeSource}, timeScale {settings.TimeScale}.");
    }

    void OnDestroy()
    {
        if (instance != this) return;
        GameManager.RoundEnded -= HandleRoundEnded;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        instance = null;
        IsActive = false;
    }

    // Everything runs a frame late on purpose: the agents and CombatantRig's
    // composed player are built during the same AfterSceneLoad pass this object
    // is, so configuring them from Awake would race the ones not built yet.
    void Update()
    {
        if (applyPending)
        {
            applyPending = false;
            ConfigureAgents();
        }
        if (finished)
        {
            // A frame after the last RoundEnded, so the other listeners — the
            // telemetry log above all — have written the final episode.
            Debug.Log($"[Eval] Completed {completed} episodes.");
            Application.Quit(0);
            enabled = false;
            return;
        }
        if (reloadPending)
        {
            reloadPending = false;
            // FinishRound froze the scene; the reload is what starts the next
            // round, exactly like the end-of-round button in human play.
            ApplyClock();
            SceneManager.LoadScene(0);
        }
    }

    // Game time advances by a fixed step per frame instead of by the wall
    // clock: a round then covers the same number of decisions whatever frame
    // rate the machine hits, and frames aren't throttled to real time, so an
    // eval runs far faster than the fight it simulates. Putting timeScale back
    // to 1 also undoes the freeze FinishRound left behind.
    void ApplyClock()
    {
        Time.timeScale = 1f;
        Time.captureDeltaTime = Time.fixedDeltaTime * settings.TimeScale;
    }

    void HandleSceneLoaded(Scene scene, LoadSceneMode mode) => applyPending = true;

    void HandleRoundEnded(string winner)
    {
        completed++;
        if (completed >= settings.Episodes) finished = true;
        else reloadPending = true;
    }

    void ConfigureAgents()
    {
        foreach (EnemyAgent agent in FindObjectsByType<EnemyAgent>(FindObjectsSortMode.None))
        {
            // The NPC is what is being scored; anything else on the field is
            // the opponent. The subject may be a control (heuristic or random)
            // rather than the model; the opponent only ever runs the model or
            // the heuristic.
            bool isSubject = agent.CompareTag("NPC");
            bool runsModel = isSubject
                ? settings.Subject == EvalSettings.SubjectKind.Policy
                : settings.Opponent == EvalSettings.OpponentKind.Policy;
            // Only the subject runs a control: a random or fleeing far side is
            // a weaker opponent, not a condition anything in the plan is
            // scored against.
            agent.control = isSubject ? ControlFor(settings.Subject) : EnemyAgent.ControlPolicy.Heuristic;
            ConfigurePolicy(agent, runsModel);
            ConfigureModeSource(agent);
        }
    }

    // Policy runs the model, so what Heuristic would play never comes up.
    static EnemyAgent.ControlPolicy ControlFor(EvalSettings.SubjectKind subject)
    {
        switch (subject)
        {
            case EvalSettings.SubjectKind.Random: return EnemyAgent.ControlPolicy.Random;
            case EvalSettings.SubjectKind.Flee: return EnemyAgent.ControlPolicy.Flee;
            default: return EnemyAgent.ControlPolicy.Heuristic;
        }
    }

    void ConfigurePolicy(EnemyAgent agent, bool runsModel)
    {
        var parameters = agent.GetComponent<BehaviorParameters>();
        if (parameters == null) return;
        if (runsModel)
        {
            // SetModel over assigning the field: it reloads the policy of an
            // agent that has already initialized.
            agent.SetModel(parameters.BehaviorName, model);
            // Explicitly, not Default: with no communicator and no model,
            // Default quietly falls back to the heuristic, which would score
            // the scripted baseline while claiming to score the policy.
            parameters.BehaviorType = BehaviorType.InferenceOnly;
        }
        else
        {
            parameters.BehaviorType = BehaviorType.HeuristicOnly;
        }
    }

    // Nothing commands modes outside training by default (ModeDirector stands
    // itself down), so an eval run has to say which selector writes the channel.
    void ConfigureModeSource(EnemyAgent agent)
    {
        ModeDirector director = agent.GetComponent<ModeDirector>();
        if (director == null) return;

        switch (settings.ModeSource)
        {
            case EvalSettings.ModeSourceKind.Scripted:
                director.trainingOnly = false;
                director.useForcedMode = false;
                // Both bodies draw from the same pool, for the reason training
                // owns the mask from code (#82).
                director.enabledModes = NpcModes.AllMask;
                break;
            case EvalSettings.ModeSourceKind.Fixed:
                director.trainingOnly = false;
                director.useForcedMode = true;
                director.forcedMode = settings.FixedMode;
                break;
            case EvalSettings.ModeSourceKind.None:
                director.trainingOnly = true; // no communicator here, so it stays quiet
                return;
        }
        director.ResetState(); // command a mode now rather than after the first dwell
    }
}
