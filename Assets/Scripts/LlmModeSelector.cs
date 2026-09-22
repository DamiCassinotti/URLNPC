using System.Threading;
using System.Threading.Tasks;

// The LLM arm of the selector comparison (issue #130): the snapshot goes to a
// model behind ILlmEndpoint — a local Ollama by default — and the mode it names
// is what gets commanded. Nothing here knows which model that is; swapping 3B
// for 8B, or the endpoint for a cloud one, is LlmSelectorConfig.
//
// The failure ladder, from the issue: schema-constrained decode, then defensive
// parsing, then Retries more attempts on output that names no mode, then NoMode
// so the driver keeps the channel's mode and counts a failure toward its
// fallback selector. A transport failure or a timeout throws instead — the
// driver records it the same way and never blocks on any of it.
public class LlmModeSelector : IReportingModeSelector, IStatefulModeSelector
{
    // An answer that is no mode. The driver's Enum.IsDefined check turns this
    // into an Invalid mode_decision line: the mode is kept, the failure counts.
    internal const NpcMode NoMode = (NpcMode)(-1);

    // Too little of the budget left for a retry to be worth issuing.
    internal const float MinimumAttemptSeconds = 0.25f;

    readonly ILlmEndpoint endpoint;
    readonly ModePrompt prompt;

    // The few-shot block, rendered once (#132): the bank and the shot count are
    // fixed for a run, so the examples are the same text on every call. Empty
    // is the zero-shot arm, which collapses the prompt's slot.
    readonly string exemplars;
    // The prompt id plus the bank and shot count that filled it — one field, so
    // a mode_decision line still names the whole text it was answered from.
    readonly string promptLabel;

    // The last few decisions, which is what lets the model read a trend across
    // calls instead of answering each one from scratch (issue #131).
    readonly ModePromptHistory history = new ModePromptHistory();

    public LlmSelectorConfig Config;

    public LlmModeSelector(ILlmEndpoint endpoint, LlmSelectorConfig config, ModePrompt prompt,
        ModeExemplars bank = null)
    {
        this.endpoint = endpoint;
        this.prompt = prompt;
        Config = config.Sanitized();
        exemplars = bank == null ? "" : bank.Render(Config.Shots);
        int shots = bank == null ? 0 : bank.Take(Config.Shots).Count;
        promptLabel = prompt == null ? ""
            : shots == 0 ? prompt.Id
            : $"{prompt.Id}+{bank.Id}x{shots}";
    }

    // Per episode, from the driver: the previous round's decisions describe a
    // fight that is over.
    public void ResetState() => history.Clear();

    public Task<NpcMode> SelectModeAsync(GameStateSnapshot snapshot, CancellationToken cancellation)
    {
        return SelectModeAsync(snapshot, new ModeDecisionReport(), cancellation);
    }

    public async Task<NpcMode> SelectModeAsync(
        GameStateSnapshot snapshot, ModeDecisionReport report, CancellationToken cancellation)
    {
        report.ModelName = Config.Model;
        report.PromptId = promptLabel;
        if (snapshot == null)
        {
            throw new System.InvalidOperationException(
                "no game-state snapshot to send — the body carries no GameStateSnapshotBuilder");
        }
        if (prompt == null)
        {
            throw new System.InvalidOperationException(
                "no prompt variant to render — see ModePromptLibrary");
        }

        string rendered = prompt.Render(snapshot, history.Turns, exemplars);
        report.Prompt = rendered;

        // TimeoutSeconds is the budget for the whole call, retries included —
        // per attempt it would let (Retries + 1) attempts outlive the decision
        // period, and the driver cancelling a retry mid-flight reports a
        // timeout where the ladder had actually reached a verdict.
        var budget = System.Diagnostics.Stopwatch.StartNew();
        string lastText = "";
        int attempts = Config.Retries + 1;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            float remaining = Config.TimeoutSeconds - (float)budget.Elapsed.TotalSeconds;
            // Nothing useful left to ask in: the answer would land after the
            // driver had given up on it anyway.
            if (attempt > 0 && remaining < MinimumAttemptSeconds) break;
            if (attempt > 0) report.RetryUsed = true;
            // The retry has to ask something different. At temperature 0 the
            // decode is greedy, so the same prompt returns the same unusable
            // text however the seed moves — a retry that changed only the seed
            // would spend the budget reproducing the first answer.
            string attemptPrompt = attempt == 0 ? rendered : rendered + RetryNote(lastText);
            string text = await Complete(attemptPrompt, attempt, remaining, cancellation);
            report.RawResponse = attempt == 0 ? text : report.RawResponse + "\n--- retry ---\n" + text;
            lastText = text;

            if (LlmModeResponse.TryParse(text, out LlmModeResponse parsed))
            {
                report.Parsed = true;
                report.Reason = parsed.Reason;
                // Only an answer that still matters joins the history: a call
                // the driver gave up on was never commanded, and recording it
                // would tell the next prompt about a decision that never ran.
                if (!cancellation.IsCancellationRequested) history.Add(snapshot, parsed.Mode);
                return parsed.Mode;
            }
        }

        report.Parsed = false;
        return NoMode;
    }

    // One attempt. Throws rather than answering: a dead server or a model too
    // slow to be useful is not a mode.
    async Task<string> Complete(string prompt, int attempt, float timeoutSeconds, CancellationToken cancellation)
    {
        var request = new LlmRequest
        {
            Model = Config.Model,
            Prompt = prompt,
            Temperature = Config.Temperature,
            // Moves with the attempt so a sampling backend doesn't repeat
            // itself either; a run still replays, the attempt count being a
            // function of the answers.
            Seed = Config.Seed + attempt,
            JsonSchema = LlmModeResponse.Schema(),
            TimeoutSeconds = (int)System.Math.Ceiling(timeoutSeconds),
        };

        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
        {
            Task<LlmCompletion> call = endpoint.CompleteAsync(request, linked.Token);
            int timeoutMs = (int)(timeoutSeconds * 1000f);
            // The timeout is enforced here as well as signalled through the
            // token: an endpoint that ignores cancellation must not be able to
            // hold the decision open past its budget.
            Task finished = await Task.WhenAny(call, Task.Delay(timeoutMs));
            if (finished != call)
            {
                linked.Cancel();
                Observe(call);
                throw new System.TimeoutException(
                    $"{Config.Model} did not answer within {timeoutSeconds:0.##} s " +
                    $"of the call's {Config.TimeoutSeconds:0.##} s budget");
            }

            LlmCompletion completion = await call;
            if (!completion.Ok)
            {
                throw new System.InvalidOperationException(
                    $"{Config.Model} call failed: {completion.Error}");
            }
            return completion.Text ?? "";
        }
    }

    // The abandoned call may still fault; swallow it there rather than letting
    // it surface as unobserved-task noise.
    static void Observe(Task task)
    {
        task.ContinueWith(t => { _ = t.Exception; },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    // What the retry adds: the answer that could not be read, and what was
    // wrong with it. Kept short — the state above it hasn't changed.
    static string RetryNote(string unusable)
    {
        const int Excerpt = 200;
        string quoted = unusable == null ? "" :
            unusable.Length > Excerpt ? unusable.Substring(0, Excerpt) : unusable;
        return "\nYour previous answer named no mode and could not be read: \"" + quoted.Replace("\"", "'") +
            "\"\nAnswer again with JSON only, and with \"mode\" set to one of the modes listed above.";
    }

}
