using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// The ModeChannel's writer outside training (issue #124): builds a
// GameStateSnapshot, asks an IModeSelector for the mode off the frame, and
// applies the answer on the tick it lands — the task is polled, never awaited
// on the main thread, so a slow selector costs frames nothing. The cadence and
// failover rules are the SelectorSchedule POCO; this is the engine adapter
// half, the way ModeDirector is for ModeSchedule.
//
// Same execution order as the director and for the same reason: a write
// landing this step has to precede the decision it conditions.
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(ModeChannel))]
public class ModeSelectorDriver : MonoBehaviour
{
    // Mirrors CombatantRig.DriverOverride: the code-level pick, outranked only
    // by the command line.
    public static ModeSelectorKind? KindOverride;

    [Tooltip("Which selector commands modes this run. Overridden by '-modeSelector <none|fixed[:<Mode>]|random|fsm|llm>' on the command line, or KindOverride from code.")]
    [SerializeField] internal ModeSelectorKind selectorKind = ModeSelectorKind.None;

    [Tooltip("Seconds between periodic decisions. Kept at ModeDirector.minDwellSeconds — the switch rate the policy was trained on.")]
    [SerializeField] internal float decisionPeriodSeconds = 5f;

    [Tooltip("Shortest gap between decisions when an event (damage taken, sight gained or lost, low HP crossed) asks for one early, so a noisy fight can't thrash the mode.")]
    [SerializeField] internal float minEventDwellSeconds = 2f;

    [Tooltip("Own-HP fraction whose crossing counts as an event.")]
    [SerializeField] internal float lowHealthFraction = 0.35f;

    [Tooltip("Consecutive failed selections (timeout, exception, invalid answer) before the fallback selector takes the channel for the rest of the episode. Zero or less never fails over.")]
    [SerializeField] internal int failuresBeforeFallback = 3;

    [Header("Baseline selectors (#125)")]
    [Tooltip("The mode the Fixed selector pins.")]
    [SerializeField] internal NpcMode fixedSelectorMode = NpcMode.Hunt;

    [Tooltip("Own-HP percent at or under which the FSM retreats.")]
    [SerializeField] internal int fsmLowHealthPercent = 35;

    [Tooltip("Seconds since the last sighting after which the FSM patrols instead of pursuing the memory.")]
    [SerializeField] internal int fsmUnseenSecondsForPatrol = 6;

    [Header("LLM selector (#130)")]
    [Tooltip("Ollama base URL. Overridable with '-llmEndpoint <url>'.")]
    [SerializeField] internal string llmEndpoint = "http://localhost:11434";

    [Tooltip("Ollama model tag, e.g. 'llama3.1:8b'. Overridable with '-llmModel <tag>'.")]
    [SerializeField] internal string llmModel = "llama3.1:8b";

    [Tooltip("Seconds one model call may take before it counts as a failure. Kept under decisionPeriodSeconds. Overridable with '-llmTimeout <seconds>'.")]
    [SerializeField] internal float llmTimeoutSeconds = 4f;

    [Tooltip("Extra attempts after output that names no mode. Overridable with '-llmRetries <n>'.")]
    [SerializeField] internal int llmRetries = 1;

    [Tooltip("Sampling temperature. 0 with a fixed seed makes a run repeatable; the consistency measurement needs 0.7. Overridable with '-llmTemperature <t>'.")]
    [SerializeField] internal float llmTemperature = 0f;

    [Tooltip("Decode seed. Overridable with '-llmSeed <n>'.")]
    [SerializeField] internal int llmSeed = 1;

    [Tooltip("Which prompt variant is sent — the id of a text asset under Resources/Prompts. Overridable with '-llmPrompt <id>'.")]
    [SerializeField] internal string llmPromptId = ModePrompt.DefaultId;

    [Tooltip("Log one line to the console per decision (transition plus the snapshot fields the FSM reads), for watching a match live. Only fires while a selector is running, so training and plain human play stay quiet. Telemetry records every decision regardless.")]
    [SerializeField] internal bool logDecisions = true;

    // The seam itself: an instance assigned from code (the way tests and
    // EvalSession pick one) outranks whatever the kind would build.
    public IModeSelector Selector { get; set; }

    // Takes the channel after failuresBeforeFallback consecutive failures.
    // With none assigned the primary keeps being asked and every failure keeps
    // the channel's mode — the floor the failure handling guarantees anyway.
    public IModeSelector Fallback { get; set; }

    readonly SelectorSchedule schedule = new SelectorSchedule();

    ModeChannel channel;
    ModeDirector director;
    EnemyBehavior behavior;
    Health selfHealth;
    GameStateSnapshotBuilder snapshotBuilder;
    bool componentsResolved;

    // One selector call and everything its mode_decision line needs (#126):
    // the report travels with the task, so a late answer from an abandoned
    // call can't write over the next one's diagnostics.
    class PendingDecision
    {
        public Task<NpcMode> Task;
        public CancellationTokenSource Cancellation;
        public ModeDecisionReport Report;
        public GameStateSnapshot Snapshot;
        public int Id;
        public bool ByFallback;
        // Wall clock, not game time: the latency of a real selector is IO.
        public readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    }

    // Session-wide, not per driver: the sibling decisions file is keyed by id,
    // and both self-play bodies write into the same session.
    static int nextDecisionId;

    // EditMode seam: lets a test observe the records without a TelemetryLogger.
    internal System.Action<ModeDecisionRecord> onDecision;

    PendingDecision pending;
    ModeSelectorSelection? resolvedSelection;
    bool warnedNoSelector;

    // Command line > code override > Inspector, resolved once — the argument
    // can't change mid-process.
    ModeSelectorSelection ResolvedSelection
    {
        get
        {
            if (!resolvedSelection.HasValue)
            {
                resolvedSelection = ModeSelectorChoice.ResolveSelection(
                    System.Environment.GetCommandLineArgs(), KindOverride, selectorKind);
            }
            return resolvedSelection.Value;
        }
    }

    public ModeSelectorKind ResolvedKind => ResolvedSelection.Kind;

    // One writer at a time (see ModeChannel): the scripted director outranks
    // this driver — during training always, and whenever an eval or the
    // Inspector told it to write. The driver only claims the channel when it
    // actually has a selector to ask.
    internal bool IsWriter
    {
        get
        {
            ResolveComponents();
            return channel != null && ActiveSelector != null
                && !(director != null && director.IsWriter);
        }
    }

    IModeSelector ActiveSelector => Selector ?? BuiltSelector;

    IModeSelector built;
    bool buildAttempted;

    // Built once — the kind can't change mid-process and the random selector
    // must keep one draw sequence, not restart per access.
    IModeSelector BuiltSelector
    {
        get
        {
            if (!buildAttempted)
            {
                buildAttempted = true;
                built = BuildSelector(ResolvedKind);
            }
            return built;
        }
    }

    // The baselines (#125) and the LLM selector (#130) construct here. A kind
    // this build can't construct resolves to nothing and the driver stays
    // inert unless a selector was assigned from code.
    IModeSelector BuildSelector(ModeSelectorKind kind)
    {
        switch (kind)
        {
            case ModeSelectorKind.Fixed:
                // "-modeSelector fixed:<Mode>" names the pin; plain "fixed"
                // falls back to the serialized default.
                return new FixedModeSelector(ResolvedSelection.FixedMode ?? fixedSelectorMode);
            case ModeSelectorKind.Random:
                return new RandomModeSelector();
            case ModeSelectorKind.Fsm:
                return Fsm();
            case ModeSelectorKind.Llm:
                // An unreachable model would otherwise leave the channel stuck
                // on initialMode for the rest of the episode: the failover ladder
                // exists but nothing outside the tests ever filled the slot.
                if (Fallback == null) Fallback = Fsm();
                LlmSelectorConfig config = LlmConfig;
                if (!ModePromptLibrary.TryLoad(config.PromptId, out ModePrompt prompt))
                {
                    // Inert rather than prompted with something else: a run
                    // scored against a prompt other than the one it names
                    // would be an LLM condition nobody can reproduce.
                    Debug.LogError($"[ModeSelector] no usable prompt '{config.PromptId}' under " +
                        $"Resources/{ModePromptLibrary.ResourceFolder} — the LLM selector stays inert.", this);
                    return null;
                }
                Debug.Log($"[ModeSelector] LLM selector on {config.Model} at {config.Endpoint} " +
                    $"(prompt {prompt.Id}, temp {config.Temperature}, seed {config.Seed}, " +
                    $"timeout {config.TimeoutSeconds:0.##} s, {config.Retries} retries).", this);
                return new LlmModeSelector(new OllamaEndpoint(config.GenerateUrl), config, prompt);
            default:
                return null;
        }
    }

    HeuristicModeSelector Fsm()
    {
        return new HeuristicModeSelector
        {
            LowHealthPercent = fsmLowHealthPercent,
            UnseenSecondsForPatrol = fsmUnseenSecondsForPatrol,
        };
    }

    // Serialized defaults, then the launch arguments on top — the batch runs
    // sweep models and temperatures without a rebuild.
    internal LlmSelectorConfig LlmConfig => new LlmSelectorConfig
    {
        Endpoint = llmEndpoint,
        Model = llmModel,
        TimeoutSeconds = llmTimeoutSeconds,
        Retries = llmRetries,
        Temperature = llmTemperature,
        Seed = llmSeed,
        PromptId = llmPromptId,
    }.WithCommandLine(System.Environment.GetCommandLineArgs());

    void FixedUpdate()
    {
        Tick(Time.fixedTime);
    }

    // Internal seam: EditMode tests drive the loop with an injected "now", the
    // way ModeDirectorTests drive the director.
    internal void Tick(float now)
    {
        ResolveComponents();
        if (channel == null) return;
        if (director != null && director.IsWriter)
        {
            // Lost the channel: a pending answer must not land later as a
            // stale write.
            AbandonInFlight();
            return;
        }
        IModeSelector primary = ActiveSelector;
        if (primary == null)
        {
            WarnNoSelectorOnce();
            return;
        }

        schedule.DecisionPeriodSeconds = decisionPeriodSeconds;
        schedule.MinEventDwellSeconds = minEventDwellSeconds;
        schedule.LowHealthFraction = lowHealthFraction;
        schedule.FailuresBeforeFallback = failuresBeforeFallback;

        ApplyCompletedAnswer();
        if (!schedule.ShouldIssue(now, TargetVisible, RecentlyDamaged, HealthFraction)) return;

        Issue(now, primary);
    }

    // Episode resets, called by EnemyAgent alongside ModeDirector.ResetState:
    // drop the stale in-flight answer and start the cadence over — the first
    // tick of the new episode issues immediately. Failures clear too: a
    // fallback takeover is per-episode, so a recovered selector gets retried.
    public void ResetState()
    {
        AbandonInFlight();
        schedule.Reset();
        // Whatever a stateful selector carries across calls (the LLM tier's
        // snapshot history) belonged to the episode that just ended. Read off
        // the built field rather than the property, so resetting a driver that
        // never ran doesn't construct a selector.
        (Selector as IStatefulModeSelector)?.ResetState();
        (built as IStatefulModeSelector)?.ResetState();
        (Fallback as IStatefulModeSelector)?.ResetState();
    }

    void OnDestroy()
    {
        AbandonInFlight();
    }

    void ResolveComponents()
    {
        if (componentsResolved) return;
        componentsResolved = true;
        channel = GetComponent<ModeChannel>();
        director = GetComponent<ModeDirector>();
        behavior = GetComponent<EnemyBehavior>();
        selfHealth = GetComponent<Health>();
        snapshotBuilder = GetComponent<GameStateSnapshotBuilder>();
    }

    bool TargetVisible => behavior != null && behavior.Perception != null
        && behavior.Perception.CurrentlyVisible;

    bool RecentlyDamaged => behavior != null && behavior.Damage != null
        && behavior.Damage.RecentlyDamaged;

    float HealthFraction => selfHealth != null && selfHealth.maxHealth > 0f
        ? selfHealth.health / selfHealth.maxHealth : 1f;

    void ApplyCompletedAnswer()
    {
        if (pending == null || !pending.Task.IsCompleted) return;
        PendingDecision finished = pending;
        pending = null;
        finished.Cancellation.Dispose();

        if (finished.Task.Status == TaskStatus.RanToCompletion)
        {
            NpcMode answer = finished.Task.Result;
            if (System.Enum.IsDefined(typeof(NpcMode), answer))
            {
                schedule.RecordSuccess();
                // The line before the write, so its mode_change follows the
                // decision that caused it on the timeline.
                EmitDecision(finished, ModeDecisionOutcome.Applied, answer);
                channel.SetMode(answer);
            }
            else
            {
                EmitDecision(finished, ModeDecisionOutcome.Invalid, null);
                Fail($"selector answered {(int)answer}, which is no mode");
            }
        }
        else if (finished.Task.Status == TaskStatus.Canceled)
        {
            EmitDecision(finished, ModeDecisionOutcome.Error, null);
            Fail("selector cancelled itself");
        }
        else
        {
            EmitDecision(finished, ModeDecisionOutcome.Error, null);
            Fail($"selector threw: {finished.Task.Exception?.GetBaseException().Message}");
        }
    }

    void Issue(float now, IModeSelector primary)
    {
        if (pending != null)
        {
            // Still unanswered when the next decision came due: cancelled, not
            // queued — this cancellation is the timeout of the failure rules.
            EmitDecision(pending, ModeDecisionOutcome.Timeout, null);
            Fail("no answer by the next decision — cancelling the stale call");
            AbandonInFlight();
        }

        // Chosen after the timeout above is counted: when that timeout is the
        // failure that crosses the threshold, this very decision is already
        // the fallback's — not the next one, a full period later.
        bool byFallback = schedule.FallbackActive && Fallback != null;
        IModeSelector selector = byFallback ? Fallback : primary;
        var issued = new PendingDecision
        {
            Cancellation = new CancellationTokenSource(),
            Report = new ModeDecisionReport(),
            Snapshot = snapshotBuilder != null ? snapshotBuilder.BuildSnapshot() : null,
            Id = ++nextDecisionId,
            ByFallback = byFallback,
        };
        Task<NpcMode> task = null;
        string failure = null;
        try
        {
            task = selector is IReportingModeSelector reporting
                ? reporting.SelectModeAsync(issued.Snapshot, issued.Report, issued.Cancellation.Token)
                : selector.SelectModeAsync(issued.Snapshot, issued.Cancellation.Token);
        }
        catch (System.Exception e)
        {
            failure = $"selector threw synchronously: {e.GetBaseException().Message}";
        }
        if (task == null)
        {
            issued.Cancellation.Dispose();
            EmitDecision(issued, ModeDecisionOutcome.Error, null);
            Fail(failure ?? "selector returned no task");
        }
        else
        {
            issued.Task = task;
            pending = issued;
        }
        // Even a failed attempt was this decision's: retrying every tick would
        // hammer a broken selector at the physics rate.
        schedule.MarkIssued(now);
    }

    // Timeout, exception or invalid answer: the channel keeps its mode and the
    // failure counts toward the fallback takeover.
    void Fail(string reason)
    {
        bool alreadyOver = schedule.FallbackActive;
        schedule.RecordFailure();
        Debug.LogWarning($"[ModeSelector] {reason} — keeping {channel.CurrentMode}.", this);
        if (!alreadyOver && schedule.FallbackActive)
        {
            Debug.LogWarning(Fallback != null
                ? $"[ModeSelector] {schedule.ConsecutiveFailures} failures in a row — the fallback selector takes over this episode."
                : $"[ModeSelector] {schedule.ConsecutiveFailures} failures in a row and no fallback assigned — the channel keeps its mode.", this);
        }
    }

    // One mode_decision line per call that reached a verdict (#126); a call
    // abandoned because its answer stopped mattering (episode reset, the
    // director took the channel) gets none. The bulky prompt/response pair
    // goes to the sibling decisions file keyed by the same id.
    void EmitDecision(PendingDecision decision, ModeDecisionOutcome outcome, NpcMode? chosen)
    {
        ModeDecisionReport report = decision.Report;
        var record = new ModeDecisionRecord
        {
            decisionId = decision.Id,
            entity = tag,
            // Named for whoever actually answered: a fallback-takeover line
            // labelled with the primary's kind would point debugging at the
            // wrong selector.
            selectorKind = decision.ByFallback ? "fallback"
                : Selector != null ? "code"
                : ResolvedKind.ToString().ToLowerInvariant(),
            modelName = report.ModelName,
            promptId = report.PromptId,
            snapshot = decision.Snapshot,
            fromMode = channel.CurrentMode,
            chosenMode = chosen,
            reason = report.Reason,
            latencyMs = (int)decision.Clock.ElapsedMilliseconds,
            // Only an applied answer can claim its output parsed — and even
            // then the selector may say no, having answered with its own
            // default after unusable model output.
            parsed = outcome == ModeDecisionOutcome.Applied && report.Parsed,
            retryUsed = report.RetryUsed,
            fallback = decision.ByFallback,
            outcome = outcome,
        };
        onDecision?.Invoke(record);
        if (logDecisions) LogDecisionToConsole(record, outcome, chosen);
        if (TelemetryLogger.Instance == null) return;
        TelemetryLogger.Instance.LogEvent("mode_decision", record.Fields());
        if (report.Prompt.Length > 0 || report.RawResponse.Length > 0)
        {
            TelemetryLogger.Instance.LogDecisionDetail(decision.Id, report.Prompt, report.RawResponse);
        }
    }

    // A per-decision console line for watching a match live: the transition
    // plus the snapshot fields the FSM keys on, so the chosen mode can be
    // checked against the rule that produced it. Telemetry keeps the full
    // record; this is the eyeball view.
    void LogDecisionToConsole(ModeDecisionRecord record, ModeDecisionOutcome outcome, NpcMode? chosen)
    {
        string to = chosen.HasValue ? chosen.Value.ToString() : "(kept)";
        GameStateSnapshot s = record.snapshot;
        string ctx = s != null
            ? $"hp {s.hpPercent}% · vis {s.targetVisible} · dist {s.targetDistance} · seen {s.secondsSinceSeen}s · dmg {s.recentlyDamaged}"
            : "no snapshot";
        Debug.Log($"[ModeDecision] {record.entity} {record.fromMode}→{to} " +
            $"({record.selectorKind}, {outcome}) — {ctx}", this);
    }

    // Cancel and forget without judging: the answer stopped mattering (episode
    // reset, the director claimed the channel, teardown).
    void AbandonInFlight()
    {
        if (pending == null) return;
        pending.Cancellation.Cancel();
        // The dropped task may still fault later; observe the exception so it
        // can't surface as unobserved-task noise.
        pending.Task.ContinueWith(t => { _ = t.Exception; },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        pending.Cancellation.Dispose();
        pending = null;
    }

    void WarnNoSelectorOnce()
    {
        if (warnedNoSelector || ResolvedKind == ModeSelectorKind.None) return;
        warnedNoSelector = true;
        Debug.LogWarning($"[ModeSelector] no {ResolvedKind} selector in this build — the channel keeps its mode until one is assigned from code.", this);
    }
}
